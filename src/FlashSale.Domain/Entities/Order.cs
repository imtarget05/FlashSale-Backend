namespace FlashSale.Domain.Entities;

public enum OrderStatus
{
    PendingPayment,
    Confirmed,
    Cancelled
}

public sealed class Order
{
    public int Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AvailableStock { get; set; }
    public decimal Price { get; set; } = 0;
}
