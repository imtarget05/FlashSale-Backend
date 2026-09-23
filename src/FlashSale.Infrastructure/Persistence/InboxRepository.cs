using FlashSale.Application.Outbox;
using FlashSale.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of IInboxRepository for deduplication (Phase 10).
/// </summary>
public sealed class InboxRepository(AppDbContext db) : IInboxRepository
{
    public async Task<bool> HasBeenProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        return await db.InboxMessages.AnyAsync(
            i => i.MessageId == messageId && i.ConsumerName == consumerName, ct);
    }

    public async Task MarkProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            ProcessedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Already recorded idempotently
        }
    }
}
