namespace FlashSale.Domain.Reporting;

/// <summary>
/// One persisted daily business report (spec §7). ALL numbers are derived from
/// the database by the reporting automation — no AI, no estimates. Top products
/// and low-stock lists are stored as JSON so the report row is self-contained
/// and reproducible.
/// </summary>
public sealed class DailyReport
{
    public int Id { get; set; }

    /// <summary>Report day (UTC, midnight) — unique, so a rerun upserts.</summary>
    public DateTime ReportDate { get; set; }

    public int TotalOrders { get; set; }
    public int ConfirmedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public int FailedPayments { get; set; }

    public decimal Revenue { get; set; }
    public decimal AverageOrderValue { get; set; }

    /// <summary>
    /// Always 0 today: no refund workflow exists yet (spec §19 keeps autonomous
    /// refunds out of scope). Reported explicitly rather than omitted so the
    /// gap is visible, never silently assumed away.
    /// </summary>
    public int RefundCount { get; set; }

    /// <summary>JSON array of {productId,name,quantity,revenue}.</summary>
    public string TopProductsJson { get; set; } = "[]";

    /// <summary>JSON array of {productId,name,availableStock,reorderThreshold}.</summary>
    public string LowStockProductsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
    public Guid CorrelationId { get; set; }
}