namespace FlashSale.Application.Persistence;

/// <summary>Read model for a product (queries never go through the write model).</summary>
public sealed record ProductView(int Id, string Name, int AvailableStock);

/// <summary>Read model for order status polling (202-accepted -> completed).</summary>
public sealed record OrderStatusView(
    string IdempotencyKey,
    int OrderId,
    int ProductId,
    int Quantity,
    DateTime CreatedAt);

/// <summary>
/// Port: query side (CQRS-lite). Keeps presentation handlers free of EF/DB
/// details — the API depends only on this contract.
/// </summary>
public interface IOrderReadModel
{
    Task<int?> GetStockAsync(int productId, CancellationToken ct = default);
    Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default);
    Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default);
}
