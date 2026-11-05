namespace FlashSale.Application.Orders;

using FlashSale.Domain;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Events;
using FlashSale.Application.Messaging;
using FlashSale.Application.Outbox;
using FlashSale.Application.Persistence;
using Microsoft.Extensions.Logging;

public sealed class OrderProcessor
{
    private readonly IOrderRepository _repository;
    private readonly IWriteTransaction _transaction;
    private readonly OutboxEventWriter _outbox;
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(
        IOrderRepository repository,
        IWriteTransaction transaction,
        OutboxEventWriter outbox,
        ILogger<OrderProcessor> logger)
    {
        _repository = repository;
        _transaction = transaction;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task ProcessAsync(OrderMessage message, CancellationToken ct = default)
    {
        if (await _repository.ExistsAsync(message.IdempotencyKey, ct))
        {
            _logger.LogInformation("Order already exists for idempotency key {Key}", message.IdempotencyKey);
            return;
        }

        await _transaction.BeginAsync(ct);
        try
        {
            var persisted = await _repository.PersistAsync(message, ct);
            if (!persisted)
            {
                _logger.LogWarning("Stock drift rejected for product {ProductId}", message.ProductId);
                throw new StockDriftException($"Insufficient stock for product {message.ProductId}");
            }

            await _outbox.EnqueueAsync(
                "order.created",
                "order",
                message.IdempotencyKey,
                Guid.NewGuid(),
                System.Text.Json.JsonSerializer.Serialize(new OrderCreatedEvent
                {
                    AggregateType = "order",
                    AggregateId = message.IdempotencyKey,
                    OrderId = 0,
                    ProductId = message.ProductId,
                    Quantity = message.Quantity,
                    IdempotencyKey = message.IdempotencyKey
                }),
                ct);

            await _transaction.CommitAsync(ct);
        }
        catch
        {
            await _transaction.RollbackAsync(ct);
            throw;
        }
    }
}
