using FlashSale.Application.Automation;
using FlashSale.Application.Events;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Orders;

/// <summary>Outcome of a submit-order attempt.</summary>
public enum SubmitOrderOutcome
{
    /// <summary>Order accepted and enqueued for asynchronous fulfillment.</summary>
    Accepted,
    /// <summary>No stock left — fast-rejected via the reservation tier or the DB.</summary>
    OutOfStock,
    /// <summary>This idempotency key was already used.</summary>
    DuplicateRequest,
    /// <summary>The requested product does not exist.</summary>
    ProductNotFound,
    /// <summary>Queue is full — caller should retry after a short delay.</summary>
    SystemBusy,
    /// <summary>Order placed synchronously (reservation tier unavailable).</summary>
    CompletedSynchronously,
    /// <summary>Quantity must be a positive integer.</summary>
    InvalidQuantity,
    /// <summary>
    /// Reservation tier and PostgreSQL disagreed (ADR-002 guard refused the
    /// conditional UPDATE instead of overselling) — alerted, not retried.
    /// </summary>
    StockDrift
}

/// <summary>
/// Result of <see cref="SubmitOrderUseCase.ExecuteAsync"/>. The presentation
/// layer maps each <see cref="Outcome"/> to the appropriate HTTP status code.
/// </summary>
public sealed record SubmitOrderResult(
    SubmitOrderOutcome Outcome,
    string IdempotencyKey);

/// <summary>
/// Use case: accept a flash-sale purchase order (ADR-003, ADR-005).
///
/// Orchestrates a multi-tier reservation and fulfillment flow:
///   T1  — fast reservation via the caching tier (Redis Lua CAS).
///   T1b — unknown product: seed the cache from the DB, retry once.
///   T2  — hand off to the async fulfillment queue.
///   T2b — backpressure: release the reservation if the queue is full.
///   T1c — reservation tier unavailable: fall back to synchronous DB processing.
///
/// Depends only on Application ports — no Infrastructure or Presentation references.
/// </summary>
public sealed class SubmitOrderUseCase(
    IStockReservationGateway reservationGateway,
    IOrderQueueProducer queueProducer,
    IOrderReadModel readModel,
    OrderProcessor processor,
    IDomainEventPublisher eventPublisher,
    PaymentAutomationOptions paymentOptions,
    ILogger<SubmitOrderUseCase> logger)
{
    public async Task<SubmitOrderResult> ExecuteAsync(
        int productId,
        int quantity,
        string idempotencyKey,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return new SubmitOrderResult(SubmitOrderOutcome.InvalidQuantity, idempotencyKey);

        // Spec §1: one correlation id per submission ties events + audit rows.
        var correlationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var message = new OrderMessage(
            productId, quantity, idempotencyKey, now,
            UserId: userId,
            CorrelationId: correlationId,
            // Spec §5: the payment window starts at acceptance; worker persists it.
            PaymentDueAt: now.AddMinutes(paymentOptions.TimeoutMinutes));

        // T1 — fast reservation (also deduplicates by idempotency key).
        var reservation = await reservationGateway.TryReserveAsync(productId, quantity, idempotencyKey);
        if (reservation == ReservationResult.Duplicate)
            return new SubmitOrderResult(SubmitOrderOutcome.DuplicateRequest, idempotencyKey);
        if (reservation == ReservationResult.SoldOut)
            return new SubmitOrderResult(SubmitOrderOutcome.OutOfStock, idempotencyKey);

        // T1b — unknown product: seed the reservation tier from the DB once, then retry.
        if (reservation == ReservationResult.UnknownProduct)
        {
            var stock = await readModel.GetStockAsync(productId, ct);
            if (stock is null)
                return new SubmitOrderResult(SubmitOrderOutcome.ProductNotFound, idempotencyKey);

            await reservationGateway.SetStockAsync(productId, stock.Value);
            reservation = await reservationGateway.TryReserveAsync(productId, quantity, idempotencyKey);
            if (reservation == ReservationResult.SoldOut)
                return new SubmitOrderResult(SubmitOrderOutcome.OutOfStock, idempotencyKey);
        }

        if (reservation == ReservationResult.Reserved)
        {
            // T2 — hand off to the fulfillment pipeline.
            if (await queueProducer.EnqueueAsync(message, ct))
            {
                // Spec §4: stock is genuinely reserved in Redis at this point.
                // Best-effort publish — a bus outage must NOT fail acceptance
                // (the order row is the truth; no-outbox is a documented gap).
                // OrderId=0: the row does not exist yet; correlationId links
                // this event to order.created, which carries the real id.
                try
                {
                    await eventPublisher.PublishAsync(new InventoryReservedEvent(
                        Guid.NewGuid().ToString("N"), correlationId, "order-api",
                        DateTimeOffset.UtcNow, 0, productId, quantity, idempotencyKey), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "inventory.reserved publish failed for {Key} (acceptance proceeds; best-effort).",
                        idempotencyKey);
                }

                return new SubmitOrderResult(SubmitOrderOutcome.Accepted, idempotencyKey);
            }

            // Queue full -> backpressure: give the reservation back and shed load.
            await reservationGateway.ReleaseReservationAsync(productId, quantity, idempotencyKey);
            return new SubmitOrderResult(SubmitOrderOutcome.SystemBusy, idempotencyKey);
        }

        // T1c — reservation tier unavailable: fall back to the synchronous use case
        // (Phase 3 path; still idempotent thanks to the repository constraint).
        logger.LogWarning(
            "Reservation tier unavailable ({Reservation}) — falling back to the synchronous path.",
            reservation);
        try
        {
            await processor.ProcessAsync(message, ct);
            return new SubmitOrderResult(SubmitOrderOutcome.CompletedSynchronously, idempotencyKey);
        }
        catch (StockDriftException)
        {
            return new SubmitOrderResult(SubmitOrderOutcome.StockDrift, idempotencyKey);
        }
    }
}
