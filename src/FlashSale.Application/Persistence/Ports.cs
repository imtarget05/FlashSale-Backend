namespace FlashSale.Application.Persistence;

using FlashSale.Application.Messaging;

public interface IOrderRepository
{
    Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default);
    Task<bool> PersistAsync(OrderMessage message, CancellationToken ct = default);
    Task<int?> GetOrderIdAsync(string idempotencyKey, CancellationToken ct = default);
}

public interface IWriteTransaction
{
    Task BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
    bool InTransaction { get; }
}

public interface IOutboxRepository
{
    Task EnqueueAsync(FlashSale.Domain.Outbox.OutboxMessage message, CancellationToken ct = default);
    Task<bool> HasEventAsync(string eventType, string aggregateType, string aggregateId, Guid correlationId, CancellationToken ct = default);
    Task<IReadOnlyList<FlashSale.Domain.Outbox.OutboxMessage>> GetUnprocessedAsync(int batchSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<FlashSale.Domain.Outbox.OutboxMessage>> ClaimBatchAsync(Guid claimToken, int batchSize = 50, TimeSpan? leaseDuration = null, CancellationToken ct = default);
    Task MarkProcessedAsync(long id, Guid claimToken, CancellationToken ct = default);
    Task<FlashSale.Domain.Outbox.OutboxFailureOutcome> MarkFailedAsync(long id, Guid claimToken, string error, CancellationToken ct = default);
    Task<IReadOnlyList<FlashSale.Domain.Outbox.OutboxMessage>> GetStuckAsync(int limit = 50, CancellationToken ct = default);
    Task<int> CountStuckAsync(CancellationToken ct = default);
    Task<int> CountPendingAsync(CancellationToken ct = default);
    Task<int> RequeueStuckAsync(CancellationToken ct = default);
}

public sealed record ProductView(int ProductId, string Name, int AvailableStock, decimal Price, string Currency);
public sealed record OrderStatusView(int OrderId, string Status, int Quantity, DateTimeOffset CreatedAt);
public sealed record OrderSummaryView(int OrderId, int ProductId, int Quantity, string Status);
public sealed record PendingPaymentView(int OrderId, string IdempotencyKey, DateTimeOffset CreatedAt);

public interface IOrderReadModel
{
    Task<int?> GetStockAsync(int productId, CancellationToken ct = default);
    Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default);
    Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingPaymentView>> GetPendingPaymentOrdersAsync(int take, CancellationToken ct = default);
}

public interface IPaymentRepository
{
    Task<bool> MarkPaidAsync(int orderId, CancellationToken ct = default);
    Task<bool> MarkPaymentFailedAsync(int orderId, CancellationToken ct = default);
    Task<bool> CancelAndReleaseStockAsync(int orderId, CancellationToken ct = default);
    Task<int> IncrementReminderAsync(int orderId, CancellationToken ct = default);
    Task<PaymentOrderView?> GetByKeyAsync(string idempotencyKey, CancellationToken ct = default);
}

public sealed record PaymentOrderView(int OrderId, string IdempotencyKey, string Status, DateTimeOffset CreatedAt);

public interface ICheckoutSagaRepository
{
    Task<FlashSale.Domain.Saga.CheckoutSagaState?> GetBySagaIdAsync(Guid sagaId, CancellationToken ct = default);
    Task<FlashSale.Domain.Saga.CheckoutSagaState?> GetByOrderIdAsync(int orderId, CancellationToken ct = default);
    Task<FlashSale.Domain.Saga.CheckoutSagaState?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task SaveAsync(FlashSale.Domain.Saga.CheckoutSagaState saga, CancellationToken ct = default);
}
