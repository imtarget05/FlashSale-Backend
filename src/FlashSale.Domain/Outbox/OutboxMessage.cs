namespace FlashSale.Domain.Outbox;

public sealed record OutboxMessage(
    Guid MessageId,
    string EventType,
    string AggregateType,
    string AggregateId,
    Guid CorrelationId,
    string Payload,
    DateTimeOffset CreatedAt);

public sealed record OutboxFailureOutcome(
    int PendingCount,
    string? NextAttemptAt,
    bool Requeued);

public sealed class DuplicateOutboxMessageException : Exception
{
    public DuplicateOutboxMessageException(Guid messageId)
        : base($"Duplicate outbox message id: {messageId}") { }
}
