namespace OpsBridge.Domain.InboxOutbox;

/// <summary>
/// Durable Inbox pattern entity for deduplication and idempotent consumption.
/// </summary>
public class InboxMessage
{
    public Guid MessageId { get; set; }
    public string ConsumerGroup { get; set; } = "default";
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Transactional Outbox pattern entity for reliable event publishing.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
}
