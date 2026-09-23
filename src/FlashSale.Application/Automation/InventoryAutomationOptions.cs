namespace FlashSale.Application.Automation;

/// <summary>
/// Inventory automation configuration (spec §6: reorder threshold must be
/// configurable, never hard-coded). Bound from "Automation:Inventory".
/// </summary>
public sealed class InventoryAutomationOptions
{
    public const string SectionName = "Automation:Inventory";

    /// <summary>Fallback threshold for products with ReorderThreshold = 0.</summary>
    public int DefaultReorderThreshold { get; set; } = 5;

    /// <summary>Background scan cadence in seconds (LowStockScanHostedService).</summary>
    public int ScanIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// Pure low-stock rule (spec §6): <c>IF available_stock &lt;= reorder_threshold
/// THEN LOW_STOCK</c>. Boundary is INCLUSIVE — stock equal to the threshold is
/// already "low" (the reorder point is the last acceptable level, not the first
/// bad one). No I/O, no AI: the numbers come from the database only.
/// </summary>
public static class LowStockRule
{
    public static bool IsLowStock(int availableStock, int reorderThreshold) =>
        availableStock <= reorderThreshold;
}

public sealed record LowStockScanResult(int Scanned, int AlertsCreated, IReadOnlyList<int> ProductIds);