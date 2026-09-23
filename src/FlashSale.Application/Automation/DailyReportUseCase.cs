using FlashSale.Application.Events;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Reporting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FlashSale.Application.Automation;

/// <summary>
/// Daily business report (spec §7): aggregate the day's orders, revenue, failed
/// payments, cancellations, top products and low-stock products from the
/// database, persist one row per day (rerun = upsert), and record an
/// AutomationRun. No AI summary is generated — spec §7 marks it optional and
/// this implementation deliberately ships the honest core first (numbers from
/// the DB only).
/// </summary>
public sealed class DailyReportUseCase(
    IDailyReportRepository reports,
    IStockAlertRepository alerts,
    IAutomationRunRepository runs,
    InventoryAutomationOptions inventoryOptions,
    ReportingAutomationOptions options,
    ILogger<DailyReportUseCase> logger)
{
    public async Task<DailyReport> ExecuteAsync(
        DateTime? dateUtc = null, string triggerType = "manual", CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = ReportWindow.ForDay(dateUtc ?? DateTime.UtcNow);

        var run = new AutomationRun
        {
            WorkflowName = AutomationWorkflow.DailyReport.ToString(),
            TriggerType = triggerType,
            TriggerId = fromUtc.ToString("yyyy-MM-dd"),
            Status = AutomationRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid()
        };
        await runs.CreateAsync(run, ct);

        var report = await reports.GetByDateAsync(fromUtc, ct) ?? new DailyReport
        {
            ReportDate = fromUtc,
            CorrelationId = run.CorrelationId
        };

        try
        {
            var metrics = await reports.ComputeMetricsAsync(fromUtc, toUtc, options.TopProductCount, ct);
            var lowStock = await alerts.GetLowStockProductsAsync(
                inventoryOptions.DefaultReorderThreshold, 200, ct);

            report.TotalOrders = metrics.TotalOrders;
            report.ConfirmedOrders = metrics.ConfirmedOrders;
            report.CancelledOrders = metrics.CancelledOrders;
            report.FailedPayments = metrics.FailedPayments;
            report.Revenue = metrics.Revenue;
            // Average of CONFIRMED (revenue-bearing) orders; 0 when there are none
            // — never divide by all orders, which would understate AOV.
            report.AverageOrderValue = metrics.ConfirmedOrders > 0
                ? Math.Round(metrics.Revenue / metrics.ConfirmedOrders, 2)
                : 0m;
            report.RefundCount = 0; // no refund workflow exists (spec §19 note)
            report.TopProductsJson = JsonSerializer.Serialize(metrics.TopProducts);
            report.LowStockProductsJson = JsonSerializer.Serialize(lowStock);
            report.CreatedAt = DateTimeOffset.UtcNow;
            report.CorrelationId = run.CorrelationId;

            report = await reports.UpsertAsync(report, ct);

            run.Status = AutomationRunStatus.Success;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ResultSummary =
                $"date={fromUtc:yyyy-MM-dd} orders={report.TotalOrders} revenue={report.Revenue} " +
                $"cancelled={report.CancelledOrders} failed_payments={report.FailedPayments}";

            logger.LogInformation(
                "Daily report {Date}: orders={Orders} revenue={Revenue} confirmed={Confirmed} cancelled={Cancelled} failedPayments={Failed} lowStock={LowStock}.",
                fromUtc.ToString("yyyy-MM-dd"), report.TotalOrders, report.Revenue,
                report.ConfirmedOrders, report.CancelledOrders, report.FailedPayments, lowStock.Count);
        }
        catch (Exception ex)
        {
            run.Status = AutomationRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = ex.GetType().Name;
            run.ErrorMessage = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            logger.LogError(ex, "Daily report failed for {Date}.", fromUtc.ToString("yyyy-MM-dd"));
        }

        await runs.UpdateAsync(run, ct);
        return report;
    }
}