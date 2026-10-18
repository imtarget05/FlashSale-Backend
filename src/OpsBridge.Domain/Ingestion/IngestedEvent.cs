namespace OpsBridge.Domain.Ingestion;

public enum EventProcessingStatus
{
    Received = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Duplicate = 5
}

public class IngestedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public Guid CorrelationId { get; set; }
    public string RawPayload { get; set; } = string.Empty;
    public EventProcessingStatus Status { get; set; } = EventProcessingStatus.Received;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
