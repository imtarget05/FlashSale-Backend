namespace FlashSale.Domain.Outbox;

/// <summary>
/// Deduplication Inbox pattern entity (Phase 10).
/// Records processed event message IDs per consumer group to guarantee idempotency.
/// </summary>
public class InboxMessage
{
    public Guid MessageId { get; set; }
    public string ConsumerName { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
