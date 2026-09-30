namespace FlashSale.Domain.Entities;

/// <summary>Aggregate root for flash-sale inventory.</summary>
public class Product
{
    public int Id { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal FlashSalePrice { get; set; }
    public int AvailableStock { get; set; }

    /// <summary>
    /// Low-stock alert threshold (spec §6): when AvailableStock falls to this
    /// value or below, the inventory automation raises a LOW_STOCK alert.
    /// 0 means "use the platform default" (Automation:Inventory).
    /// </summary>
    public int ReorderThreshold { get; set; }

    /// <summary>
    /// GATE 1 (EXPAND phase): snapshot of <see cref="AvailableStock"/> at the
    /// moment the column was introduced, so "how many were sold" is derivable
    /// without joining history.
    ///
    /// Nullable on purpose. A NOT NULL column with no default would break
    /// every existing INSERT, and a non-null default would silently claim the
    /// snapshot happened at migration time for rows that predate it. Nullable
    /// + explicit backfill is the only option that cannot lie.
    ///
    /// It is NOT part of the V1 contract: application code that predates this
    /// migration neither reads nor writes it, which is exactly what makes
    /// "DB V2 + App V1" safe.
    /// </summary>
    public int? OriginalStock { get; set; }
}
