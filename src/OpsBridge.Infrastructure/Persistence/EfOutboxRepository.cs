using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Domain.InboxOutbox;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfOutboxRepository(OpsBridgeDbContext db) : IOutboxRepository
{
    public async Task AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        await db.OutboxMessages.AddAsync(message, ct);
        // Note: SaveChangesAsync will be called by UnitOfWork or caller
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 20, CancellationToken ct = default)
    {
        return await db.OutboxMessages
            .Where(o => o.Status == "Pending" && o.ProcessedAt == null)
            .OrderBy(o => o.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default)
    {
        var msg = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == messageId, ct);
        if (msg != null)
        {
            msg.Status = "Published";
            msg.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RecordFailureAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        var msg = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == messageId, ct);
        if (msg != null)
        {
            msg.RetryCount++;
            msg.ErrorMessage = error;
            if (msg.RetryCount >= 5)
            {
                msg.Status = "Failed";
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
