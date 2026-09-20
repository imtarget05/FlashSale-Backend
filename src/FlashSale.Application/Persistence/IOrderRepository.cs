using FlashSale.Domain.Messaging;

namespace FlashSale.Application.Persistence;

/// <summary>
/// Port: durable order persistence with the atomic conditional stock update
/// (ADR-002). Implementations must keep decrement + insert in ONE transaction.
/// </summary>
public interface IOrderRepository
{
    /// <summary>True when an order with this idempotency key already exists.</summary>
    Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Atomically decrement stock and persist the order.
    /// Returns false when the product has insufficient stock (drift).
    /// </summary>
    Task<bool> PersistAsync(OrderMessage message, CancellationToken ct = default);
}
