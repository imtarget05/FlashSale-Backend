using Microsoft.EntityFrameworkCore;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Domain.InboxOutbox;

namespace OpsBridge.Infrastructure.Persistence;

public sealed class EfInboxRepository(OpsBridgeDbContext db) : IInboxRepository
{
    public async Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerGroup = "default", CancellationToken ct = default)
    {
        return await db.InboxMessages.AnyAsync(
            i => i.MessageId == messageId && i.ConsumerGroup == consumerGroup, ct);
    }

    public async Task AddAsync(InboxMessage inboxMessage, CancellationToken ct = default)
    {
        await db.InboxMessages.AddAsync(inboxMessage, ct);
    }

    public async Task MarkProcessedAsync(
        Guid messageId,
        string consumerGroup,
        string eventType,
        string source,
        string payloadHash = "",
        CancellationToken ct = default)
    {
        var inboxEntry = new InboxMessage
        {
            MessageId = messageId,
            ConsumerGroup = consumerGroup,
            EventType = eventType,
            Source = source,
            PayloadHash = payloadHash,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        db.InboxMessages.Add(inboxEntry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Idempotent duplicate: primary key already exists
        }
    }
}
