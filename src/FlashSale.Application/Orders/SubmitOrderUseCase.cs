using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
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

        var message = new OrderMessage(productId, quantity, idempotencyKey, DateTimeOffset.UtcNow, UserId: userId);

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
                return new SubmitOrderResult(SubmitOrderOutcome.Accepted, idempotencyKey);

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
