using FlashSale.Application.Events;
using FlashSale.Application.Inventory;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Automation;

/// <summary>Outcome of one timeout scan (spec §5) — feeds the endpoint/worker logs.</summary>
public sealed record PaymentScanResult(int Scanned, int Reminded, int Cancelled);

/// <summary>
/// Abandoned-payment recovery (spec §5 + §4 cancel branch): scan
/// PENDING_PAYMENT orders → remind inside grace → past grace cancel, release
/// inventory (DB transactional, Redis best-effort) and publish payment.expired /
/// order.cancelled / inventory.released. Every scan writes exactly one
/// AutomationRun audit row (spec §11), success or failure.
/// </summary>
public sealed class PaymentAutomationUseCase(
    IOrderReadModel readModel,
    IPaymentRepository payments,
    IStockReservationGateway reservationGateway,
    IAutomationRunRepository runs,
    IDomainEventPublisher publisher,
    PaymentAutomationOptions options,
    ILogger<PaymentAutomationUseCase> logger)
{
    /// <summary>Upper bound per scan so one run can never be unbounded (§12).</summary>
    public const int BatchSize = 200;

    public async Task<PaymentScanResult> ExecuteAsync(string triggerType, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new AutomationRun
        {
            WorkflowName = AutomationWorkflow.PaymentTimeout.ToString(),
            TriggerType = triggerType,
            TriggerId = Guid.NewGuid().ToString("N"),
            Status = AutomationRunStatus.Running,
            StartedAt = now,
            CorrelationId = Guid.NewGuid()
        };
        await runs.CreateAsync(run, ct);

        var reminded = 0;
        var cancelled = 0;
        var scanned = 0;

        try
        {
            var due = await readModel.GetPendingPaymentOrdersAsync(BatchSize, ct);
            scanned = due.Count;

            foreach (var order in due)
            {
                ct.ThrowIfCancellationRequested();
                var decision = PaymentTimeoutRule.Evaluate(
                    order.PaymentDueAt,
                    order.PaymentReminderCount,
                    options.MaxPaymentReminders,
                    options.GracePeriodMinutes,
                    now);

                switch (decision)
                {
                    case PaymentTimeoutRule.Decision.SendReminder:
                        if (await payments.IncrementReminderAsync(order.OrderId, ct) > 0)
                        {
                            reminded++;
                            // Notification channel (email/webhook/n8n) is PLANNED —
                            // until wired, the reminder = structured log + audit row.
                            logger.LogInformation(
                                "Payment reminder {Count}/{Max} for order {OrderId} (key {Key}).",
                                order.PaymentReminderCount + 1, options.MaxPaymentReminders,
                                order.OrderId, order.IdempotencyKey);
                        }
                        break;

                    case PaymentTimeoutRule.Decision.Cancel:
                        if (await payments.CancelAndReleaseStockAsync(order.OrderId, ct))
                        {
                            await reservationGateway.ReleaseReservationAsync(
                                order.ProductId, order.Quantity, order.IdempotencyKey);

                            var cid = run.CorrelationId;
                            await PublishSafeAsync(new PaymentExpiredEvent(
                                Guid.NewGuid().ToString("N"), cid, "automation/payment-timeout",
                                DateTimeOffset.UtcNow, order.OrderId, options.TimeoutMinutes), ct);
                            await PublishSafeAsync(new OrderCancelledEvent(
                                Guid.NewGuid().ToString("N"), cid, "automation/payment-timeout",
                                DateTimeOffset.UtcNow, order.OrderId, "payment_timeout"), ct);
                            await PublishSafeAsync(new InventoryReleasedEvent(
                                Guid.NewGuid().ToString("N"), cid, "automation/payment-timeout",
                                DateTimeOffset.UtcNow, order.OrderId, order.ProductId, order.Quantity), ct);

                            cancelled++;
                            logger.LogInformation(
                                "Order {OrderId} cancelled after payment timeout — stock released (product {ProductId}, qty {Quantity}).",
                                order.OrderId, order.ProductId, order.Quantity);
                        }
                        break;
                }
            }

            run.Status = AutomationRunStatus.Success;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ResultSummary = $"scanned={scanned} reminded={reminded} cancelled={cancelled}";
        }
        catch (Exception ex)
        {
            run.Status = AutomationRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = ex.GetType().Name;
            run.ErrorMessage = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            logger.LogError(ex, "Payment timeout scan failed after {Scanned} rows.", scanned);
        }

        await runs.UpdateAsync(run, ct);
        return new PaymentScanResult(scanned, reminded, cancelled);
    }

    /// <summary>
    /// Events are best-effort (at-most-once): an event-bus outage must never
    /// roll back a committed cancellation — the order row + audit record ARE
    /// the source of truth. Outbox-style redelivery is a documented limitation.
    /// </summary>
    private async Task PublishSafeAsync(DomainEvent domainEvent, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(domainEvent, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Event publish failed for {EventType} {EventId} (best-effort, audit row still written).",
                domainEvent.EventType, domainEvent.EventId);
        }
    }
}