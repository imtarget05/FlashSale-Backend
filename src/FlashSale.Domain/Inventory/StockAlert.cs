namespace FlashSale.Domain.Inventory;

/// <summary>Low-stock alert lifecycle (spec §6).</summary>
public enum StockAlertStatus
{
    /// <summary>Raised, nobody has acted on it yet — the only state that dedupes.</summary>
    Open,
    /// <summary>A human has seen it and is reordering.</summary>
    Acknowledged,
    /// <summary>Stock is healthy again (resolution lands with the dashboard phase).</summary>
    Resolved
}

/// <summary>
/// One LOW_STOCK alert (spec §6). Created ONLY by the inventory automation —
/// the threshold comparison never runs in AI, and the alert carries the exact
/// stock/threshold that triggered it.
/// </summary>
public sealed class StockAlert
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int AvailableStock { get; set; }
    public int ReorderThreshold { get; set; }
    public StockAlertStatus Status { get; set; } = StockAlertStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CorrelationId { get; set; }
}