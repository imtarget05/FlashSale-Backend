namespace FlashSale.Domain.Events;

public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string AggregateType { get; init; } = string.Empty;
    public string AggregateId { get; init; } = string.Empty;
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string? EventType { get; init; }
}

public sealed record OrderCreatedEvent(
    string EventId,
    Guid CorrelationId,
    string AggregateType,
    DateTimeOffset OccurredAt,
    int OrderId,
    int ProductId,
    int Quantity,
    string IdempotencyKey) : DomainEvent
{
    public OrderCreatedEvent() : this(Guid.NewGuid().ToString("D"), Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow, 0, 0, 0, string.Empty) { }
}

public sealed record OrderConfirmedEvent(
    string EventId,
    Guid CorrelationId,
    string AggregateType,
    DateTimeOffset OccurredAt,
    int OrderId,
    string IdempotencyKey) : DomainEvent
{
    public OrderConfirmedEvent() : this(Guid.NewGuid().ToString("D"), Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow, 0, string.Empty) { }
}

public sealed record OrderCancelledEvent(
    string EventId,
    Guid CorrelationId,
    string AggregateType,
    DateTimeOffset OccurredAt,
    int OrderId,
    string Reason) : DomainEvent
{
    public OrderCancelledEvent() : this(Guid.NewGuid().ToString("D"), Guid.NewGuid(), string.Empty, DateTimeOffset.UtcNow, 0, string.Empty) { }
}

public static class EventEnvelope
{
    public static Envelope? TryParse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return new Envelope(
                root.GetProperty("eventId").GetGuid(),
                root.GetProperty("aggregateType").GetString() ?? string.Empty,
                root.GetProperty("aggregateId").GetString() ?? string.Empty,
                root.GetProperty("correlationId").GetGuid(),
                root.GetProperty("occurredAt").GetDateTimeOffset());
        }
        catch
        {
            return null;
        }
    }
}

public sealed record Envelope(
    Guid EventId,
    string AggregateType,
    string AggregateId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);
