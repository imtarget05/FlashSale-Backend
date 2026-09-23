using FlashSale.Domain.Inventory;

namespace FlashSale.Application.Persistence;

/// <summary>Product row evaluated by the low-stock rule (spec §6).</summary>
public sealed record LowStockProductView(
    int ProductId,
    string Name,
    int AvailableStock,
    int ReorderThreshold);

/// <summary>
/// Port: low-stock alert persistence (spec §6). Creation is DEDUPED by a
/// partial unique index on (ProductId) WHERE Status='Open', so a rescan can
/// never spam duplicate alerts for the same product.
/// </summary>
public interface IStockAlertRepository
{
    /// <summary>
    /// Products at/below their effective threshold (product value when set,
    /// otherwise <paramref name="defaultThreshold"/>), lowest stock first.
    /// </summary>
    Task<IReadOnlyList<LowStockProductView>> GetLowStockProductsAsync(
        int defaultThreshold, int take, CancellationToken ct = default);

    /// <summary>Inserts the alert; returns null when an Open alert already exists (dedupe).</summary>
    Task<StockAlert?> TryCreateAsync(StockAlert alert, CancellationToken ct = default);

    Task<IReadOnlyList<StockAlert>> ListOpenAsync(int take, CancellationToken ct = default);
}