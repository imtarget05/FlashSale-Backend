namespace FlashSale.Domain.Outbox;

/// <summary>A durable receipt for one human-approved recovery command.</summary>
public sealed class OutboxRecoveryCommand
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string IntentId { get; set; } = string.Empty;
    public string IntentHash { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTimeOffset ExpectedDeadLetteredAt { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
}
