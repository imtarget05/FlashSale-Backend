using FlashSale.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>EF Core implementation of the query-side port (read-only queries).</summary>
public sealed class OrderReadModel(AppDbContext db) : IOrderReadModel
{
    public Task<int?> GetStockAsync(int productId, CancellationToken ct = default) =>
        db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => (int?)p.AvailableStock)
            .FirstOrDefaultAsync(ct);

    public async Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default) =>
        await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductView(p.Id, p.Name, p.AvailableStock))
            .FirstOrDefaultAsync(ct);

    public async Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.IdempotencyKey == idempotencyKey)
            .Select(o => new OrderStatusView(o.IdempotencyKey, o.Id, o.ProductId, o.Quantity, o.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryView(o.Id, o.ProductId, o.Quantity, o.CreatedAt))
            .ToListAsync(ct);
}
