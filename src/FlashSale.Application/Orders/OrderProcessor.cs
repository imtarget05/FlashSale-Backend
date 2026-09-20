using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Messaging;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Orders;

/// <summary>
/// Use case: fulfill one order, idempotently (ADR-004).
/// Depends only on ports — delivery may be at-least-once, business effects
/// are exactly-once via the repository's unique idempotency constraint.
/// </summary>
public sealed class OrderProcessor(IOrderRepository repository, ILogger<OrderProcessor> logger)
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
    }
}
