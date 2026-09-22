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
}
