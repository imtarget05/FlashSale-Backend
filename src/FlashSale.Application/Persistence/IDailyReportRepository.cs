using FlashSale.Domain.Reporting;

namespace FlashSale.Application.Persistence;

/// <summary>One row of the "top products" ranking (spec §7).</summary>
public sealed record TopProductView(int ProductId, string Name, int Quantity, decimal Revenue);

/// <summary>
/// Raw database aggregates for one report day (spec §7). Every field comes from
/// a SQL aggregate — the use case only formats, never invents numbers.
/// </summary>
public sealed record DailyReportMetrics(
    int TotalOrders,
    int ConfirmedOrders,
    int CancelledOrders,
    int FailedPayments,
    decimal Revenue,
    IReadOnlyList<TopProductView> TopProducts);

/// <summary>
/// Port: daily report aggregation + persistence (spec §7). The report date is
/// unique, so re-running a day UPDATES the same row instead of duplicating it.
/// </summary>
public interface IDailyReportRepository
{
    Task<DailyReportMetrics> ComputeMetricsAsync(
        DateTime fromUtc, DateTime toUtc, int topProductCount, CancellationToken ct = default);

    Task<DailyReport?> GetByDateAsync(DateTime reportDateUtc, CancellationToken ct = default);
    Task<DailyReport?> GetLatestAsync(CancellationToken ct = default);
    Task<DailyReport> UpsertAsync(DailyReport report, CancellationToken ct = default);
}