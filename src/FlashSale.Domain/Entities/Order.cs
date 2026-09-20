namespace FlashSale.Domain.Entities;

/// <summary>
/// A persisted purchase. IdempotencyKey carries the client's purchase
/// intention — the unique index on it makes redelivery harmless (ADR-004).
/// </summary>
public class Order
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
