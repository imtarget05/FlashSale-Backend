namespace FlashSale.Domain.Entities;

/// <summary>
/// A persisted purchase. IdempotencyKey carries the client's purchase
/// intention — the unique index on it makes redelivery harmless (ADR-004).
/// </summary>
/// <remarks>
/// <see cref="UserId"/> is nullable on purpose (ADR-013 §6): the anonymous
/// order path predates authentication and must keep working unchanged, so an
/// anonymous order simply has no owner.
/// </remarks>
public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Owner, or <c>null</c> for an anonymous order.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Order lifecycle status (spec §4/§5 automation).</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>For payment automation (spec §5): when payment is expected.</summary>
    public DateTimeOffset? PaymentDueAt { get; set; }

    /// <summary>For payment automation (spec §5): when payment was completed/failed/expired.</summary>
    public DateTimeOffset? PaymentProcessedAt { get; set; }

    /// <summary>For payment automation (spec §5): last payment attempt result.</summary>
    public string? LastPaymentResult { get; set; }

    /// <summary>For automation audit (spec §11): correlation id tying events together.</summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>For automation audit (spec §11): workflow that triggered this order's automation.</summary>
    public string? AutomationWorkflow { get; set; }

    /// <summary>For automation audit (spec §11): automation run id that processed this order.</summary>
    public int? AutomationRunId { get; set; }
}
