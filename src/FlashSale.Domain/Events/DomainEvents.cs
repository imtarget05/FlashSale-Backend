namespace FlashSale.Domain.Events;

/// <summary>Base for all automation events (spec §1).</summary>
public abstract record DomainEvent(
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    string Source)
{
    public abstract object Payload { get; }
}

/// <summary>Order lifecycle: customer creates order (spec §4).</summary>
public sealed record OrderCreatedEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    int ProductId,
    int Quantity,
    Guid? UserId,
    string IdempotencyKey,
    OrderStatus Status) : DomainEvent(EventId, "order.created", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, ProductId, Quantity, UserId, IdempotencyKey, Status };
}

/// <summary>Stock reserved in Redis for an order (spec §4).</summary>
public sealed record InventoryReservedEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    int ProductId,
    int Quantity,
    string IdempotencyKey) : DomainEvent(EventId, "inventory.reserved", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, ProductId, Quantity, IdempotencyKey };
}

/// <summary>Payment completed for an order (spec §4).</summary>
public sealed record PaymentCompletedEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    decimal Amount,
    string PaymentMethod) : DomainEvent(EventId, "payment.completed", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, Amount, PaymentMethod };
}

/// <summary>Payment failed for an order (spec §4).</summary>
public sealed record PaymentFailedEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    string Reason) : DomainEvent(EventId, "payment.failed", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, Reason };
}

/// <summary>Payment expired (timeout) for an order (spec §4/§5).</summary>
public sealed record PaymentExpiredEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    int TimeoutMinutes) : DomainEvent(EventId, "payment.expired", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, TimeoutMinutes };
}

/// <summary>Order confirmed after payment (spec §4).</summary>
public sealed record OrderConfirmedEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId) : DomainEvent(EventId, "order.confirmed", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId };
}

/// <summary>Order cancelled (spec §4/§5).</summary>
public sealed record OrderCancelledEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    string Reason) : DomainEvent(EventId, "order.cancelled", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, Reason };
}

/// <summary>Stock released after cancellation (spec §4/§5).</summary>
public sealed record InventoryReleasedEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int OrderId,
    int ProductId,
    int Quantity) : DomainEvent(EventId, "inventory.released", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { OrderId, ProductId, Quantity };
}

/// <summary>Low-stock alert raised (spec §6).</summary>
public sealed record InventoryLowStockEvent(
    string EventId,
    Guid CorrelationId,
    string Source,
    DateTimeOffset OccurredAt,
    int ProductId,
    int AvailableStock,
    int ReorderThreshold) : DomainEvent(EventId, "inventory.low_stock", OccurredAt, CorrelationId, Source)
{
    public override object Payload => new { ProductId, AvailableStock, ReorderThreshold };
}