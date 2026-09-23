using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of the reporting port (spec §7). All figures are
/// SQL aggregates over the orders/products tables — the report can never
/// disagree with the database, and no estimate is ever substituted.
/// </summary>
public sealed class DailyReportRepository(AppDbContext db) : IDailyReportRepository
{
    public async Task<DailyReportMetrics> ComputeMetricsAsync(
        DateTime fromUtc, DateTime toUtc, int topProductCount, CancellationToken ct = default)
    {
        var totalOrders = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc, ct);

        var confirmedOrders = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                          && o.Status == OrderStatus.Confirmed, ct);

        var cancelledOrders = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                          && o.Status == OrderStatus.Cancelled, ct);

        var failedPayments = await db.Orders.AsNoTracking()
            .CountAsync(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                          && o.LastPaymentResult == "failed", ct);

        // Revenue is recognised on CONFIRMED (paid) orders only, priced at the
        // product's flash-sale price — the same number a customer paid.
        var revenue = await (from o in db.Orders.AsNoTracking()
                             join p in db.Products.AsNoTracking() on o.ProductId equals p.Id
                             where o.Status == OrderStatus.Confirmed
                                && o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                             select p.FlashSalePrice * o.Quantity).SumAsync(ct);

        var topProducts = await (from o in db.Orders.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on o.ProductId equals p.Id
                                 where o.Status == OrderStatus.Confirmed
                                    && o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                                 group new { o, p } by new { p.Id, p.Name } into g
                                 orderby g.Sum(x => x.o.Quantity) descending
                                 select new TopProductView(
                                     g.Key.Id, g.Key.Name,
                                     g.Sum(x => x.o.Quantity),
                                     g.Sum(x => x.p.FlashSalePrice * x.o.Quantity)))
                                .Take(topProductCount)
                                .ToListAsync(ct);

        return new DailyReportMetrics(
            totalOrders, confirmedOrders, cancelledOrders, failedPayments, revenue, topProducts);
    }

    public async Task<DailyReport?> GetByDateAsync(DateTime reportDateUtc, CancellationToken ct = default) =>
        await db.DailyReports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReportDate == reportDateUtc, ct);

    public async Task<DailyReport?> GetLatestAsync(CancellationToken ct = default) =>
        await db.DailyReports.AsNoTracking()
            .OrderByDescending(r => r.ReportDate)
            .FirstOrDefaultAsync(ct);

    public async Task<DailyReport> UpsertAsync(DailyReport report, CancellationToken ct = default)
    {
        var existing = await db.DailyReports
            .FirstOrDefaultAsync(r => r.ReportDate == report.ReportDate, ct);

        if (existing is null)
        {
            db.DailyReports.Add(report);
        }
        else
        {
            // Same day rerun refreshes the figures — the report is a snapshot of
            // "as of now", and the unique date index guarantees one row per day.
            existing.TotalOrders = report.TotalOrders;
            existing.ConfirmedOrders = report.ConfirmedOrders;
            existing.CancelledOrders = report.CancelledOrders;
            existing.FailedPayments = report.FailedPayments;
            existing.Revenue = report.Revenue;
            existing.AverageOrderValue = report.AverageOrderValue;
            existing.RefundCount = report.RefundCount;
            existing.TopProductsJson = report.TopProductsJson;
            existing.LowStockProductsJson = report.LowStockProductsJson;
            existing.CreatedAt = report.CreatedAt;
            existing.CorrelationId = report.CorrelationId;
            report = existing;
        }

        await db.SaveChangesAsync(ct);
        return report;
    }
}