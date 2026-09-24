
namespace FlashSale.Application.Persistence;

/// <summary>Read model for a product (queries never go through the write model).</summary>
public sealed record ProductView(int Id, string Name, int AvailableStock, decimal FlashSalePrice, string Description);

/// <summary>Read model for order status polling (202-accepted -> completed).</summary>
public sealed record OrderStatusView(
    string IdempotencyKey,
    int OrderId,
    int ProductId,
    int Quantity,
    DateTime CreatedAt);

/// <summary>One row of a user's own order history (<c>GET /orders/me</c>).</summary>
public sealed record OrderSummaryView(
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

    /// <summary>
    /// Orders owned by one user, newest first. Anonymous orders (UserId null)
    /// are never returned, so this cannot leak another caller's history.
    /// </summary>
    Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Pending-payment orders for the payment-timeout scan (spec §5), oldest
    /// due date first, capped at <paramref name="take"/> rows per scan so one
    /// run stays bounded.
    /// </summary>
    Task<IReadOnlyList<PendingPaymentView>> GetPendingPaymentOrdersAsync(int take, CancellationToken ct = default);
}
