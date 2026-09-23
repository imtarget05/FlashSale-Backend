using FlashSale.Application.Events;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Events;
using FlashSale.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Automation;

/// <summary>
/// Inventory low-stock automation (spec §6): evaluate REAL stock values against
/// the reorder threshold, raise a deduplicated LOW_STOCK alert, publish
/// inventory.low_stock and record one AutomationRun per scan. The AI layer is
/// never involved — stock values are database truth only (spec §6 note).
/// </summary>
public sealed class LowStockAlertUseCase(
    IStockAlertRepository alerts,
    IAutomationRunRepository runs,
    IDomainEventPublisher publisher,
    InventoryAutomationOptions options,
    ILogger<LowStockAlertUseCase> logger)
{
    public const int BatchSize = 200;

    public async Task<LowStockScanResult> ExecuteAsync(string triggerType, CancellationToken ct = default)
    {
        var run = new AutomationRun
        {
            WorkflowName = AutomationWorkflow.InventoryAutomation.ToString(),
            TriggerType = triggerType,
            TriggerId = Guid.NewGuid().ToString("N"),
            Status = AutomationRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid()
        };
        await runs.CreateAsync(run, ct);

        var created = new List<int>();
        var scanned = 0;

        try
        {
            var products = await alerts.GetLowStockProductsAsync(
                options.DefaultReorderThreshold, BatchSize, ct);
            scanned = products.Count;

            foreach (var product in products)
            {
                ct.ThrowIfCancellationRequested();

                var threshold = product.ReorderThreshold > 0
                    ? product.ReorderThreshold
                    : options.DefaultReorderThreshold;

                // Rule is re-checked in-process (defence in depth): the SQL filter
                // and the rule must agree, and the rule is the tested artifact.
                if (!LowStockRule.IsLowStock(product.AvailableStock, threshold)) continue;

                var alert = await alerts.TryCreateAsync(new StockAlert
                {
                    ProductId = product.ProductId,
                    AvailableStock = product.AvailableStock,
                    ReorderThreshold = threshold,
                    Status = StockAlertStatus.Open,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CorrelationId = run.CorrelationId
                }, ct);

                if (alert is null)
                {
                    logger.LogDebug(
                        "Low stock re-detected for product {ProductId} (stock {Stock} <= {Threshold}) — alert already open.",
                        product.ProductId, product.AvailableStock, threshold);
                    continue; // deduped: one Open alert per product
                }

                created.Add(product.ProductId);
                logger.LogWarning(
                    "LOW_STOCK alert #{AlertId}: product {ProductId} stock {Stock} <= threshold {Threshold} (notification channel PLANNED: dashboard/email).",
                    alert.Id, product.ProductId, product.AvailableStock, threshold);

                await PublishSafeAsync(new InventoryLowStockEvent(
                    Guid.NewGuid().ToString("N"), run.CorrelationId, "automation/inventory",
                    DateTimeOffset.UtcNow, product.ProductId, product.AvailableStock, threshold), ct);
            }

            run.Status = AutomationRunStatus.Success;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ResultSummary = $"scanned={scanned} alerts_created={created.Count}" +
                (created.Count > 0 ? $" products=[{string.Join(",", created)}]" : string.Empty);
        }
        catch (Exception ex)
        {
            run.Status = AutomationRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = ex.GetType().Name;
            run.ErrorMessage = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            logger.LogError(ex, "Low-stock scan failed after {Scanned} rows.", scanned);
        }

        await runs.UpdateAsync(run, ct);
        return new LowStockScanResult(scanned, created.Count, created);
    }

    private async Task PublishSafeAsync(DomainEvent domainEvent, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(domainEvent, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Event publish failed for {EventType} {EventId} (best-effort, alert + audit row still written).",
                domainEvent.EventType, domainEvent.EventId);
        }
    }
}