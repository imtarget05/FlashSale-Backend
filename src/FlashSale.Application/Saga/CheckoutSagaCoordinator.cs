using FlashSale.Application.Events;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Payment;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Events;
using FlashSale.Domain.Saga;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Saga;

public sealed record CheckoutSagaCommand(
    string IdempotencyKey,
    int ProductId,
    int Quantity,
    decimal Amount,
    Guid? UserId = null,
    Guid CorrelationId = default);

public sealed record CheckoutSagaResult(
    bool Success,
    Guid SagaId,
    int OrderId,
    SagaStatus Status,
    string Message);

/// <summary>
/// Checkout Saga Coordinator (Phases 9 & 11).
/// Orchestrates distributed state changes across Inventory, Order, and Payment bounded contexts.
/// Enforces forward progress and automated compensating transactions on payment decline or timeout.
/// </summary>
public sealed class CheckoutSagaCoordinator(
    ICheckoutSagaRepository sagaRepository,
    IStockReservationGateway stockGateway,
    IOrderRepository orderRepository,
    IPaymentRepository paymentRepository,
    IPaymentClient paymentClient,
    IDomainEventPublisher domainEventPublisher,
    ILogger<CheckoutSagaCoordinator> logger)
{
    public async Task<CheckoutSagaResult> ExecuteSagaAsync(
        CheckoutSagaCommand command,
        CancellationToken ct = default)
    {
        var correlationId = command.CorrelationId == default ? Guid.NewGuid() : command.CorrelationId;

        // Idempotency check: Return existing result if this key was already coordinated
        var existing = await sagaRepository.GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is not null)
        {
            logger.LogInformation("Saga for IdempotencyKey {Key} already exists in status {Status}.",
                command.IdempotencyKey, existing.Status);

            if (existing.Status == SagaStatus.Completed)
            {
                return new CheckoutSagaResult(
                    Success: true,
                    SagaId: existing.SagaId,
                    OrderId: existing.OrderId,
                    Status: existing.Status,
                    Message: "Saga already completed idempotently.");
            }

            if (existing.Status is SagaStatus.Compensated or SagaStatus.Failed)
            {
                return new CheckoutSagaResult(
                    Success: false,
                    SagaId: existing.SagaId,
                    OrderId: existing.OrderId,
                    Status: existing.Status,
                    Message: existing.CompensationReason ?? existing.FailureReason ?? "Saga previously terminated.");
            }
        }

        // Initialize durable Saga state
        var saga = new CheckoutSagaState
        {
            SagaId = Guid.NewGuid(),
            IdempotencyKey = command.IdempotencyKey,
            ProductId = command.ProductId,
            Quantity = command.Quantity,
            Amount = command.Amount,
            UserId = command.UserId,
            Status = SagaStatus.Started,
            InventoryStatus = InventoryReservationStatus.Pending,
            PaymentStatus = PaymentTransactionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await sagaRepository.SaveAsync(saga, ct);

        await PublishSafeAsync(new CheckoutSagaStartedEvent(
            Guid.NewGuid().ToString("N"),
            correlationId,
            "checkout-saga",
            DateTimeOffset.UtcNow,
            saga.SagaId,
            saga.OrderId,
            command.ProductId,
            command.Quantity,
            command.Amount,
            command.IdempotencyKey), ct);

        // Step 1: Reserve Inventory in fast reservation tier
        var reservation = await stockGateway.TryReserveAsync(command.ProductId, command.Quantity, command.IdempotencyKey);
        if (reservation != ReservationResult.Reserved && reservation != ReservationResult.Duplicate)
        {
            logger.LogWarning("Saga {SagaId}: Inventory reservation rejected with {Result}.",
                saga.SagaId, reservation);

            saga.InventoryStatus = InventoryReservationStatus.Rejected;
            saga.Status = SagaStatus.Failed;
            saga.FailureReason = $"Inventory reservation rejected: {reservation}";
            saga.UpdatedAt = DateTimeOffset.UtcNow;
            await sagaRepository.SaveAsync(saga, ct);

            return new CheckoutSagaResult(false, saga.SagaId, 0, saga.Status, saga.FailureReason);
        }

        // Persist order with pending payment
        var orderMsg = new OrderMessage(
            ProductId: command.ProductId,
            Quantity: command.Quantity,
            IdempotencyKey: command.IdempotencyKey,
            CreatedAt: DateTimeOffset.UtcNow,
            UserId: command.UserId,
            CorrelationId: correlationId,
            PaymentDueAt: DateTimeOffset.UtcNow.AddMinutes(15));

        var persisted = await orderRepository.PersistAsync(orderMsg, ct);
        if (!persisted)
        {
            logger.LogWarning("Saga {SagaId}: DB conditional persist failed (stock drift). Releasing Redis reservation.", saga.SagaId);
            await stockGateway.ReleaseReservationAsync(command.ProductId, command.Quantity, command.IdempotencyKey);

            saga.InventoryStatus = InventoryReservationStatus.Rejected;
            saga.Status = SagaStatus.Failed;
            saga.FailureReason = "Database stock verification failed";
            saga.UpdatedAt = DateTimeOffset.UtcNow;
            await sagaRepository.SaveAsync(saga, ct);

            return new CheckoutSagaResult(false, saga.SagaId, 0, saga.Status, saga.FailureReason);
        }

        var orderId = await orderRepository.GetOrderIdAsync(command.IdempotencyKey, ct);
        saga.OrderId = orderId ?? 0;
        saga.InventoryStatus = InventoryReservationStatus.Reserved;
        saga.Status = SagaStatus.InventoryReserved;
        saga.UpdatedAt = DateTimeOffset.UtcNow;
        await sagaRepository.SaveAsync(saga, ct);

        // Step 2: Payment Execution with bounded retries for transient timeouts
        saga.Status = SagaStatus.PaymentProcessing;
        saga.PaymentStatus = PaymentTransactionStatus.Pending;
        saga.UpdatedAt = DateTimeOffset.UtcNow;
        await sagaRepository.SaveAsync(saga, ct);

        PaymentClientResult paymentResult = null!;
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            logger.LogInformation("Saga {SagaId}: Processing payment attempt {Attempt}/{Max}...",
                saga.SagaId, attempt, maxAttempts);

            paymentResult = await paymentClient.ProcessPaymentAsync(new PaymentClientRequest(
                PaymentId: Guid.NewGuid(),
                OrderId: saga.OrderId,
                IdempotencyKey: command.IdempotencyKey,
                Amount: command.Amount), ct);

            if (!paymentResult.IsTransientError || attempt == maxAttempts)
            {
                break;
            }

            await Task.Delay(50 * attempt, ct);
        }

        // Step 3: Handle Outcome & Compensations
        if (paymentResult.Success)
        {
            logger.LogInformation("Saga {SagaId}: Payment succeeded (tx={Tx}). Confirming order {OrderId}.",
                saga.SagaId, paymentResult.TransactionId, saga.OrderId);

            saga.PaymentStatus = PaymentTransactionStatus.Successful;
            saga.Status = SagaStatus.Completed;
            saga.UpdatedAt = DateTimeOffset.UtcNow;

            await paymentRepository.MarkPaidAsync(saga.OrderId, ct);
            await sagaRepository.SaveAsync(saga, ct);

            await PublishSafeAsync(new CheckoutSagaCompletedEvent(
                Guid.NewGuid().ToString("N"),
                correlationId,
                "checkout-saga",
                DateTimeOffset.UtcNow,
                saga.SagaId,
                saga.OrderId,
                paymentResult.TransactionId ?? string.Empty), ct);

            await PublishSafeAsync(new OrderConfirmedEvent(
                Guid.NewGuid().ToString("N"),
                correlationId,
                "checkout-saga",
                DateTimeOffset.UtcNow,
                saga.OrderId), ct);

            return new CheckoutSagaResult(
                Success: true,
                SagaId: saga.SagaId,
                OrderId: saga.OrderId,
                Status: saga.Status,
                Message: "Checkout saga completed successfully.");
        }

        // Failure path -> Trigger Compensating Transactions
        logger.LogWarning("Saga {SagaId}: Payment failed ({Reason}, transient={Transient}). Triggering compensations...",
            saga.SagaId, paymentResult.FailureReason, paymentResult.IsTransientError);

        saga.Status = SagaStatus.Compensating;
        saga.PaymentStatus = paymentResult.IsTransientError ? PaymentTransactionStatus.TimedOut : PaymentTransactionStatus.Declined;
        saga.CompensationReason = paymentResult.FailureReason ?? (paymentResult.IsTransientError ? "Payment timed out" : "Payment declined");
        saga.UpdatedAt = DateTimeOffset.UtcNow;
        await sagaRepository.SaveAsync(saga, ct);

        // Compensation 1: Release stock in Redis
        await stockGateway.ReleaseReservationAsync(command.ProductId, command.Quantity, command.IdempotencyKey);

        // Compensation 2: Cancel order and restore stock in database
        if (saga.OrderId > 0)
        {
            await paymentRepository.CancelAndReleaseStockAsync(saga.OrderId, ct);
        }

        saga.InventoryStatus = InventoryReservationStatus.Released;
        saga.Status = SagaStatus.Compensated;
        saga.UpdatedAt = DateTimeOffset.UtcNow;
        await sagaRepository.SaveAsync(saga, ct);

        await PublishSafeAsync(new CheckoutSagaCompensatedEvent(
            Guid.NewGuid().ToString("N"),
            correlationId,
            "checkout-saga",
            DateTimeOffset.UtcNow,
            saga.SagaId,
            saga.OrderId,
            saga.CompensationReason), ct);

        await PublishSafeAsync(new OrderCancelledEvent(
            Guid.NewGuid().ToString("N"),
            correlationId,
            "checkout-saga",
            DateTimeOffset.UtcNow,
            saga.OrderId,
            saga.CompensationReason), ct);

        return new CheckoutSagaResult(
            Success: false,
            SagaId: saga.SagaId,
            OrderId: saga.OrderId,
            Status: saga.Status,
            Message: $"Checkout compensated: {saga.CompensationReason}");
    }

    private async Task PublishSafeAsync(DomainEvent @event, CancellationToken ct)
    {
        try
        {
            await domainEventPublisher.PublishAsync(@event, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish domain event {EventType}.", @event.EventType);
        }
    }
}
