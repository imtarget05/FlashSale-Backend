using FlashSale.Application.Automation;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Outbox;
using FlashSale.Domain.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderEntity = FlashSale.Domain.Entities.Order;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Persistence;

public sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;
    public OrderRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default) =>
        _db.Orders.AsNoTracking().AnyAsync(o => o.IdempotencyKey == idempotencyKey, ct);

    public async Task<bool> PersistAsync(OrderMessage message, CancellationToken ct = default)
    {
        var product = await _db.Products.FindAsync(new object?[] { message.ProductId }, ct);
        if (product is null || product.AvailableStock < message.Quantity)
            return false;

        product.AvailableStock -= message.Quantity;
        _db.Orders.Add(new OrderEntity
        {
            IdempotencyKey = message.IdempotencyKey,
            ProductId = message.ProductId,
            Quantity = message.Quantity,
            Status = OrderStatus.PendingPayment,
            CreatedAt = message.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int?> GetOrderIdAsync(string idempotencyKey, CancellationToken ct = default) =>
        _db.Orders.AsNoTracking().Where(o => o.IdempotencyKey == idempotencyKey)
            .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
}

public sealed class OutboxRepository : IOutboxRepository
{
    private readonly AppDbContext _db;
    public OutboxRepository(AppDbContext db) => _db = db;

    public Task EnqueueAsync(OutboxMessage message, CancellationToken ct = default)
    {
        _db.OutboxMessages.Add(new OutboxMessageRow
        {
            MessageId = message.MessageId,
            EventType = message.EventType,
            AggregateType = message.AggregateType,
            AggregateId = message.AggregateId,
            CorrelationId = message.CorrelationId,
            Payload = message.Payload,
            CreatedAt = message.CreatedAt
        });
        return Task.CompletedTask;
    }

    public Task<bool> HasEventAsync(string eventType, string aggregateType, string aggregateId, Guid correlationId, CancellationToken ct = default) =>
        _db.OutboxMessages.AsNoTracking().AnyAsync(m =>
            m.EventType == eventType && m.AggregateType == aggregateType &&
            m.AggregateId == aggregateId && m.CorrelationId == correlationId, ct);

    public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize = 50, CancellationToken ct = default)
    {
        var rows = _db.OutboxMessages.AsNoTracking().Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt).Take(batchSize)
            .Select(m => ToMessage(m)).ToList();
        return Task.FromResult<IReadOnlyList<OutboxMessage>>(rows);
    }
    public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(Guid claimToken, int batchSize = 50, TimeSpan? leaseDuration = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());

    public Task MarkProcessedAsync(long id, Guid claimToken, CancellationToken ct = default) => Task.CompletedTask;
    public Task<OutboxFailureOutcome> MarkFailedAsync(long id, Guid claimToken, string error, CancellationToken ct = default) =>
        Task.FromResult(new OutboxFailureOutcome(0, null, false));
    public Task<IReadOnlyList<OutboxMessage>> GetStuckAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
    public Task<int> CountStuckAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<int> CountPendingAsync(CancellationToken ct = default) =>
        _db.OutboxMessages.AsNoTracking().CountAsync(m => m.ProcessedAt == null, ct);    public Task<int> RequeueStuckAsync(CancellationToken ct = default) => Task.FromResult(0);

    private static OutboxMessage ToMessage(OutboxMessageRow r) =>
        new(r.MessageId, r.EventType, r.AggregateType, r.AggregateId, r.CorrelationId, r.Payload, r.CreatedAt);
}

public sealed class OutboxMessageRow
{
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class EfWriteTransaction : IWriteTransaction
{
    private readonly AppDbContext _db;
    private readonly ILogger<EfWriteTransaction> _logger;
    private bool _begun;
    public bool InTransaction => _begun;

    public EfWriteTransaction(AppDbContext db, ILogger<EfWriteTransaction> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task BeginAsync(CancellationToken ct = default)
    {
        _db.Database.BeginTransaction();
        _begun = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        _db.Database.CommitTransaction();
        _begun = false;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        _db.Database.RollbackTransaction();
        _begun = false;
        return Task.CompletedTask;
    }
}

public sealed class OrderReadModel : IOrderReadModel
{
    private readonly AppDbContext _db;
    public OrderReadModel(AppDbContext db) => _db = db;

    public Task<int?> GetStockAsync(int productId, CancellationToken ct = default) =>
        _db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => (int?)p.AvailableStock).FirstOrDefaultAsync(ct);

    public Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default) =>
        _db.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new ProductView(p.Id, p.Name, p.AvailableStock, p.Price, "VND")).FirstOrDefaultAsync(ct);

    public Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default) =>
        _db.Orders.AsNoTracking().Where(o => o.IdempotencyKey == idempotencyKey)
            .Select(o => new OrderStatusView(o.Id, o.Status.ToString(), o.Quantity, o.CreatedAt)).FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderSummaryView>>(Array.Empty<OrderSummaryView>());

    public Task<IReadOnlyList<PendingPaymentView>> GetPendingPaymentOrdersAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PendingPaymentView>>(Array.Empty<PendingPaymentView>());
}

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly AppDbContext _db;
    public PaymentRepository(AppDbContext db) => _db = db;
    public Task<bool> MarkPaidAsync(int orderId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> MarkPaymentFailedAsync(int orderId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> CancelAndReleaseStockAsync(int orderId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<int> IncrementReminderAsync(int orderId, CancellationToken ct = default) => Task.FromResult(1);
    public Task<PaymentOrderView?> GetByKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult<PaymentOrderView?>(null);
}

public sealed class CheckoutSagaRepository : ICheckoutSagaRepository
{
    private readonly AppDbContext _db;
    public CheckoutSagaRepository(AppDbContext db) => _db = db;
    public Task<CheckoutSagaState?> GetBySagaIdAsync(Guid sagaId, CancellationToken ct = default) =>
        _db.CheckoutSagaStates.AsNoTracking().FirstOrDefaultAsync(s => s.SagaId == sagaId, ct);    public Task<CheckoutSagaState?> GetByOrderIdAsync(int orderId, CancellationToken ct = default) =>
        Task.FromResult<CheckoutSagaState?>(null);
    public Task<CheckoutSagaState?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        _db.CheckoutSagaStates.AsNoTracking().FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, ct);
    public Task SaveAsync(CheckoutSagaState saga, CancellationToken ct = default)
    {
        var existing = _db.CheckoutSagaStates.FirstOrDefault(s => s.SagaId == saga.SagaId);
        if (existing is null) _db.CheckoutSagaStates.Add(saga);
        return _db.SaveChangesAsync(ct);
    }
}

public sealed class AutomationRunRepository : IAutomationRunRepository
{
    private readonly AppDbContext _db;
    public AutomationRunRepository(AppDbContext db) => _db = db;
    public Task RecordAsync(string runId, string outcome, CancellationToken ct = default)
    {
        _db.AutomationRuns.Add(new AutomationRun { RunId = runId, Outcome = outcome });
        return _db.SaveChangesAsync(ct);
    }
}

public sealed class RedisStockGateway : IStockReservationGateway
{
    private readonly IDatabase _db;
    public RedisStockGateway(IDatabase db) => _db = db;
    public Task<ReservationResult> TryReserveAsync(int productId, int quantity, string idempotencyKey) =>
        Task.FromResult(ReservationResult.Reserved);
    public Task ReleaseReservationAsync(int productId, int quantity, string idempotencyKey) => Task.CompletedTask;
    public Task SetStockAsync(int productId, int stock) => Task.CompletedTask;
    public Task<int?> GetCachedStockAsync(int productId) => Task.FromResult<int?>(null);
}

public sealed class RabbitMQOrderQueue : IOrderQueueProducer, IOrderQueueConsumer
{
    public static Task<RabbitMQOrderQueue> CreateAsync(string connectionString, string queueName, ILogger<RabbitMQOrderQueue> logger) =>
        Task.FromResult(new RabbitMQOrderQueue());

    public ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default) => ValueTask.FromResult(true);
    public ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default) => ValueTask.FromResult<QueueEntry?>(null);
}
