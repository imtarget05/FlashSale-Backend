namespace OpsBridge.Domain.InboxOutbox;

public enum ConsumerStatus
{
    Started,
    Completed,
    Failed
}

public sealed class ConsumerProcessingState
{
    public Guid EventId { get; set; }
    public string ConsumerGroup { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public ConsumerStatus Status { get; set; } = ConsumerStatus.Started;
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ErrorMessage { get; set; }
}
