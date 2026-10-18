namespace OpsBridge.Domain.DLQ;

/// <summary>
/// Audit entity for messages sent to the Dead-Letter Queue (DLQ).
/// Stores failed message payload and tracks operator manual replays.
/// </summary>
public class DeadLetterEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset FailedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public bool IsReplayed { get; set; }
    public DateTimeOffset? ReplayedAt { get; set; }
    public string? ReplayedBy { get; set; }
}
