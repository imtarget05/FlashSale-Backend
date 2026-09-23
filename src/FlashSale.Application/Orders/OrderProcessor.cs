using FlashSale.Application.Events;
using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Orders;

/// <summary>
/// Use case: fulfill one order, idempotently (ADR-004).
/// Depends only on ports — delivery may be at-least-once, business effects
/// are exactly-once via the repository's unique idempotency constraint.
/// </summary>
/// <remarks>
/// The event publisher is OPTIONAL (spec §4): unit tests construct the
/// processor without one; composition roots register it and get order.created
/// published after every successful persist. Publish is best-effort — the
/// persist already succeeded, so a bus outage must not dead-letter the order.
/// </remarks>
public sealed class OrderProcessor(
    IOrderRepository repository,
    ILogger<OrderProcessor> logger,
    IDomainEventPublisher? eventPublisher = null)
{
    /// <summary>1 initial attempt + 3 retries, then the message is dead-lettered.</summary>
    public const int MaxAttempts = 4;

    public async Task ProcessAsync(OrderMessage message, CancellationToken ct)
    {
        // Idempotency: skip if this key already produced an order.
        if (await repository.ExistsAsync(message.IdempotencyKey, ct))
        {
            logger.LogInformation(
                "Duplicate delivery for {Key} — already persisted, acknowledging.",
                message.IdempotencyKey);
            return;
        }

        if (!await repository.PersistAsync(message, ct))
        {
            throw new StockDriftException(
                $"DB stock insufficient for product {message.ProductId} " +
                $"(idempotency key {message.IdempotencyKey}). Possible Redis/DB drift; " +
                "run POST /internal/resync-stock/{id} after resolving.");
        }

        logger.LogInformation(
            "Order persisted {Key} (product {Pid}, qty {Qty}, attempt {Attempt}).",
            message.IdempotencyKey, message.ProductId, message.Quantity, message.Attempt + 1);

        // Spec §4: the row exists now → order.created carries the real order id.
        if (eventPublisher is not null)
        {
            try
            {
                var orderId = await repository.GetOrderIdAsync(message.IdempotencyKey, ct) ?? 0;
                await eventPublisher.PublishAsync(new OrderCreatedEvent(
                    Guid.NewGuid().ToString("N"),
                    message.CorrelationId ?? Guid.NewGuid(),
                    "order-worker",
                    DateTimeOffset.UtcNow,
                    orderId,
                    message.ProductId,
                    message.Quantity,
                    message.UserId,
                    message.IdempotencyKey,
                    message.PaymentDueAt is null ? OrderStatus.Completed : OrderStatus.PendingPayment),
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "order.created publish failed for {Key} (persist already succeeded; best-effort).",
                    message.IdempotencyKey);
            }
        }
    }
}
