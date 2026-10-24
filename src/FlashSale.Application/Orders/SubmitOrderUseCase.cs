using System.Security.Cryptography;
using System.Text;
using FlashSale.Application.Automation;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Outbox;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Events;
using FlashSale.Domain.Outbox;
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
/// Outcome of the <see cref="CompensateReservationAsync"/> attempt.
/// </summary>
public enum CompensateOutcome
{
    Completed,
    Failed
}

/// <summary>
/// Result of <see cref="SubmitOrderUseCase.ExecuteAsync"/>. The presentation
/// layer maps each <see cref="Outcome"/> to the appropriate HTTP status code.
/// </summary>
public sealed record SubmitOrderResult(
    SubmitOrderOutcome Outcome,
    string IdempotencyKey);

/// <summary>
/// Use case: accept a flash-sale purchase order (ADR-003, ADR-005, ADR-019).
///
/// Orchestrates a multi-tier reservation and fulfillment flow:
///   T1  — fast reservation via the caching tier (Redis Lua CAS).
///   T1b — unknown product: seed the cache from the DB, retry once.
///   T1c — reservation tier unavailable: fall back to synchronous DB processing.
///   T2  — record the reservation fact in the transactional outbox (ADR-019).
///   T2b — hand off to the async fulfillment queue.
///   T2c — backpressure: release the reservation and compensate the event.
///
/// Depends only on Application ports — no Infrastructure or Presentation references.
/// </summary>
public sealed class SubmitOrderUseCase(
    IStockReservationGateway reservationGateway,
    IOrderQueueProducer queueProducer,
    IOrderReadModel readModel,
    OrderProcessor processor,
    IWriteTransaction transaction,
    OutboxEventWriter outboxWriter,
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
            // T2 — ADR-019: the reservation fact is written to the transactional
            // outbox, NOT published to the broker. The row commits inside a
            // database transaction; if the write fails, the whole thing rolls
            // back and the order is not accepted — an accepted order can never
            // be missing its event.
            await transaction.BeginAsync(ct);
            try
            {
                // OrderId=0: the row does not exist yet; correlationId links
                // this event to order.created, which carries the real id.
                await outboxWriter.EnqueueAsync(
                    new InventoryReservedEvent(
                        Guid.NewGuid().ToString("D"), correlationId, "order-api",
                        DateTimeOffset.UtcNow, 0, productId, quantity, idempotencyKey),
                    OutboxEventWriter.OrderAggregate,
                    idempotencyKey,
                    ct);

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                await reservationGateway.ReleaseReservationAsync(productId, quantity, idempotencyKey);
                logger.LogError(ex,
                    "inventory.reserved outbox write failed for {Key} — reservation released, order not accepted.",
                    idempotencyKey);
                return new SubmitOrderResult(SubmitOrderOutcome.SystemBusy, idempotencyKey);
            }

            // T2b — hand off to the fulfillment pipeline.
            if (await queueProducer.EnqueueAsync(message, ct))
            {
                return new SubmitOrderResult(SubmitOrderOutcome.Accepted, idempotencyKey);
            }

            // T2c — queue full -> backpressure: give the reservation back and shed
            // load. The committed inventory.reserved event is compensated by an
            // inventory.released event, so the event stream stays truthful.
            await reservationGateway.ReleaseReservationAsync(productId, quantity, idempotencyKey);
            await CompensateReservationAsync(productId, quantity, idempotencyKey, correlationId, ct);
            return new SubmitOrderResult(SubmitOrderOutcome.SystemBusy, idempotencyKey);
        }

        // T1c — reservation tier unavailable: fall back to the synchronous use case
        // (Phase 3 path; still idempotent thanks to the repository constraint).
        // The order row and its order.created event are written in one
        // transaction by the processor (ADR-019).
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

    /// <summary>
    /// Compensate a committed inventory.reserved event when the order could not
    /// be accepted after all. Bounded retry and critical alert logging:
    /// failures are retried, then logged with structured alert tags without throwing —
    /// the caller still receives its SystemBusy answer.
    /// </summary>
    private async Task<CompensateOutcome> CompensateReservationAsync(
        int productId,
        int quantity,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Pre-upgrade releases used random IDs. Check committed rows,
                // including published/dead-lettered rows, before writing.
                if (await outboxWriter.HasEventAsync("inventory.released", OutboxEventWriter.OrderAggregate, idempotencyKey, correlationId, ct))
                    return CompensateOutcome.Completed;

                await transaction.BeginAsync(ct);
                await outboxWriter.EnqueueAsync(
                    new InventoryReleasedEvent(
                        CompensationEventId(idempotencyKey, correlationId), correlationId, "order-api",
                        DateTimeOffset.UtcNow, 0, productId, quantity),
                    OutboxEventWriter.OrderAggregate,
                    idempotencyKey,
                    ct);
                await transaction.CommitAsync(ct);
                return CompensateOutcome.Completed;
            }
            catch (Exception ex)
            {
                Exception failure = ex;
                try
                {
                    await transaction.RollbackAsync(ct);
                }
                catch (Exception rollbackEx)
                {
                    failure = new AggregateException(ex, rollbackEx);
                }

                if (ex is DuplicateOutboxMessageException)
                {
                    try
                    {
                        // A 23505 alone is insufficient evidence: verify the
                        // matching release committed, using a fresh DB context.
                        if (await outboxWriter.HasEventAsync("inventory.released", OutboxEventWriter.OrderAggregate, idempotencyKey, correlationId, ct))
                            return CompensateOutcome.Completed;
                    }
                    catch (Exception verifyEx)
                    {
                        failure = new AggregateException(failure, verifyEx);
                    }
                }

                if (attempt == maxAttempts)
                {
                    // Record a durable failure state so a reconciler can repair the
                    // orphan reservation later. The release row is NOT committed here,
                    // so the event stream stays truthful until the repair runs.
                    await RecordCompensationFailedAsync(productId, quantity, idempotencyKey, correlationId, ct);
                    logger.LogCritical(failure,
                        "CRITICAL: inventory.released compensation FAILED after {Attempts} attempts for key {Key}, Product {ProductId}, Quantity {Quantity}. Reconciliation alert triggered.",
                        maxAttempts, idempotencyKey, productId, quantity);
                    return CompensateOutcome.Failed;
                }

                logger.LogWarning(failure,
                    "inventory.released compensation attempt {Attempt}/{Max} failed for {Key}. Retrying...",
                    attempt, maxAttempts, idempotencyKey);
            }
        }

        return CompensateOutcome.Failed;
    }

    /// <summary>
    /// Write an <c>inventory.compensation_failed</c> outbox event inside its own
    /// transaction. An operations process can locate the attempt from this
    /// marker and invoke <see cref="ReconcileOrphanReservationAsync"/> with the
    /// persisted correlation ID. No scheduled repair worker is wired here.
    /// </summary>
    private async Task RecordCompensationFailedAsync(
        int productId,
        int quantity,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken ct)
    {
        try
        {
            await transaction.BeginAsync(ct);
            await outboxWriter.EnqueueAsync(
                new InventoryCompensationFailedEvent(
                    Guid.NewGuid().ToString("D"), idempotencyKey, correlationId, "order-api",
                    DateTimeOffset.UtcNow, productId, quantity),
                OutboxEventWriter.OrderAggregate,
                idempotencyKey,
                ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to record compensation-failed state for key {Key}, Product {ProductId}, Quantity {Quantity}. The reconciler may not be able to repair this orphan reservation automatically.",
                idempotencyKey, productId, quantity);
        }
    }

    /// <summary>
    /// Idempotent reconciler repair for orphan reservations where Redis stock was
    /// released but the compensation outbox write failed during transient DB outage.
    /// The release event has a stable ID for this reservation attempt; PostgreSQL's outbox
    /// unique constraint enforces one release even across competing repair workers.
    /// </summary>
    public async Task<bool> ReconcileOrphanReservationAsync(
        int productId,
        int quantity,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken ct)
    {
        return (await CompensateReservationAsync(productId, quantity, idempotencyKey, correlationId, ct)) == CompensateOutcome.Completed;
    }

    private static string CompensationEventId(string idempotencyKey, Guid correlationId)
    {
        // A 503 permits the same HTTP key to reserve again. CorrelationId is
        // unique to each reservation attempt and persisted on its outbox row.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "inventory.released:" + idempotencyKey + ":" + correlationId.ToString("D")));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
}
