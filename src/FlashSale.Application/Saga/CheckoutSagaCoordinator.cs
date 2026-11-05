namespace FlashSale.Application.Saga;

using FlashSale.Domain;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Events;
using FlashSale.Domain.Saga;
using FlashSale.Application.Inventory;
using FlashSale.Application.Payment;
using FlashSale.Application.Persistence;
using FlashSale.Application.Outbox;
using Microsoft.Extensions.Logging;
using System.Text.Json;
public sealed record CheckoutSagaCommand(
    string IdempotencyKey,
    int ProductId,
    int Quantity,
    decimal Amount);

public sealed record SagaResult(
    bool Success,
    SagaStatus Status,
    string Message,
    Guid SagaId,
    int OrderId);

public sealed class CheckoutSagaCoordinator
{
    private readonly ICheckoutSagaRepository _sagaRepo;
    private readonly IStockReservationGateway _stockGateway;
    private readonly IOrderRepository _orderRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IPaymentClient _paymentClient;
    private readonly IWriteTransaction _transaction;
    private readonly OutboxEventWriter _outbox;
    private readonly ILogger<CheckoutSagaCoordinator> _logger;

    public CheckoutSagaCoordinator(
        ICheckoutSagaRepository sagaRepo,
        IStockReservationGateway stockGateway,
        IOrderRepository orderRepo,
        IPaymentRepository paymentRepo,
        IPaymentClient paymentClient,
        IWriteTransaction transaction,
        OutboxEventWriter outbox,
        ILogger<CheckoutSagaCoordinator> logger)
    {
        _sagaRepo = sagaRepo;
        _stockGateway = stockGateway;
        _orderRepo = orderRepo;
        _paymentRepo = paymentRepo;
        _paymentClient = paymentClient;
        _transaction = transaction;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<SagaResult> ExecuteSagaAsync(CheckoutSagaCommand command, CancellationToken ct = default)
    {
        var existing = await _sagaRepo.GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is not null)
        {
            return new SagaResult(true, existing.Status, "Saga already processed", existing.SagaId, existing.OrderId);
        }

        var saga = new CheckoutSagaState
        {
            IdempotencyKey = command.IdempotencyKey,
            ProductId = command.ProductId,
            Quantity = command.Quantity,
            Amount = command.Amount
        };

        await _outbox.EnqueueAsync("checkout.saga.started", "saga", command.IdempotencyKey, saga.SagaId, "{}", ct);

        // Reserve inventory
        var reserve = await _stockGateway.TryReserveAsync(command.ProductId, command.Quantity, command.IdempotencyKey);
        if (reserve == ReservationResult.SoldOut)
        {
            saga.Status = SagaStatus.Failed;
            saga.InventoryStatus = InventoryReservationStatus.Rejected;
            saga.FailureReason = "Inventory sold out";
            await _sagaRepo.SaveAsync(saga, ct);
            await _outbox.EnqueueAsync("checkout.saga.failed", "saga", command.IdempotencyKey, saga.SagaId, "{}", ct);
            return new SagaResult(false, SagaStatus.Failed, "Inventory sold out", saga.SagaId, 0);
        }
        if (reserve == ReservationResult.Duplicate)
        {
            saga.Status = SagaStatus.Completed;
            saga.InventoryStatus = InventoryReservationStatus.Reserved;
            await _sagaRepo.SaveAsync(saga, ct);
            await _outbox.EnqueueAsync("checkout.saga.completed", "saga", command.IdempotencyKey, saga.SagaId, "{}", ct);
            return new SagaResult(true, SagaStatus.Completed, "Duplicate reservation", saga.SagaId, 0);
        }
        saga.InventoryStatus = InventoryReservationStatus.Reserved;

        // Process payment (with retry on transient timeout)
        PaymentClientResult paymentResult;
        int attempts = 0;
        const int maxAttempts = 2;
        do
        {
            attempts++;
            paymentResult = await _paymentClient.ProcessPaymentAsync(
                new PaymentClientRequest(command.IdempotencyKey, 0, command.Amount, "VND"), ct);
            if (paymentResult.Success) break;
            if (!paymentResult.IsTransientError) break;
        } while (attempts < maxAttempts);

        if (!paymentResult.Success)
        {
            // Compensate: release inventory
            await _stockGateway.ReleaseReservationAsync(command.ProductId, command.Quantity, command.IdempotencyKey);
            saga.Status = SagaStatus.Compensated;
            saga.PaymentStatus = paymentResult.Status == "TimedOut" ? PaymentTransactionStatus.TimedOut : PaymentTransactionStatus.Declined;
            saga.CompensationReason = paymentResult.FailureReason;
            await _sagaRepo.SaveAsync(saga, ct);
            await _outbox.EnqueueAsync("checkout.saga.compensated", "saga", command.IdempotencyKey, saga.SagaId, "{}", ct);
            await _outbox.EnqueueAsync("order.cancelled", "order", command.IdempotencyKey, saga.SagaId, "{}", ct);
            return new SagaResult(false, SagaStatus.Compensated, paymentResult.FailureReason ?? "Payment failed", saga.SagaId, 0);
        }

        saga.PaymentStatus = PaymentTransactionStatus.Successful;
        saga.Status = SagaStatus.Completed;
        saga.OrderId = 1; // placeholder order id
        await _sagaRepo.SaveAsync(saga, ct);
        await _outbox.EnqueueAsync("checkout.saga.completed", "saga", command.IdempotencyKey, saga.SagaId, "{}", ct);
        await _outbox.EnqueueAsync("order.confirmed", "order", command.IdempotencyKey, saga.SagaId, "{}", ct);
        return new SagaResult(true, SagaStatus.Completed, "Saga completed", saga.SagaId, saga.OrderId);
    }
}
