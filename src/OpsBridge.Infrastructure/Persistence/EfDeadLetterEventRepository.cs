using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Domain.DLQ;
using OpsBridge.Domain.InboxOutbox;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfDeadLetterEventRepository(OpsBridgeDbContext db) : IDeadLetterEventRepository
{
    public async Task<IReadOnlyList<DeadLetterEvent>> GetRecentAsync(int limit, CancellationToken ct = default) =>
        await db.DeadLetterEvents
            .AsNoTracking()
            .OrderByDescending(deadLetter => deadLetter.FailedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

    public async Task<DeadLetterReplayResult> QueueReplayAsync(
        Guid eventId,
        string actor,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var deadLetter = await db.DeadLetterEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.EventId == eventId, ct);

        if (deadLetter is null)
        {
            return new DeadLetterReplayResult(DeadLetterReplayStatus.NotFound, eventId);
        }

        var replayedAt = DateTimeOffset.UtcNow;
        var updated = await db.DeadLetterEvents
            .Where(item => item.EventId == eventId && !item.IsReplayed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsReplayed, true)
                .SetProperty(item => item.ReplayedAt, replayedAt)
                .SetProperty(item => item.ReplayedBy, actor), ct);

        if (updated == 0)
        {
            await transaction.RollbackAsync(ct);
            var existing = await db.DeadLetterEvents
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.EventId == eventId, ct);
            return existing is null
                ? new DeadLetterReplayResult(DeadLetterReplayStatus.NotFound, eventId)
                : new DeadLetterReplayResult(
                    DeadLetterReplayStatus.AlreadyReplayed,
                    eventId,
                    ReplayedAt: existing.ReplayedAt,
                    ReplayedBy: existing.ReplayedBy);
        }

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = deadLetter.EventType,
            Topic = deadLetter.Topic,
            Payload = deadLetter.Payload,
            Status = "Pending",
            CreatedAt = replayedAt
        };

        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new DeadLetterReplayResult(
            DeadLetterReplayStatus.Queued,
            eventId,
            outboxMessage.Id,
            replayedAt,
            actor);
    }
}