using FlashSale.Domain.Saga;

namespace FlashSale.Application.Saga;

/// <summary>
/// Port for persisting and retrieving state of the distributed checkout saga.
/// </summary>
public interface ICheckoutSagaRepository
{
    Task<CheckoutSagaState?> GetBySagaIdAsync(Guid sagaId, CancellationToken ct = default);
    Task<CheckoutSagaState?> GetByOrderIdAsync(int orderId, CancellationToken ct = default);
    Task<CheckoutSagaState?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task SaveAsync(CheckoutSagaState saga, CancellationToken ct = default);
}
