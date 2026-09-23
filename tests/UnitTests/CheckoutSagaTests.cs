using FlashSale.Application.Inventory;
using FlashSale.Application.Saga;
using FlashSale.Domain.Events;
using FlashSale.Domain.Saga;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

public class CheckoutSagaTests
{
    private readonly FakeCheckoutSagaRepository _sagaRepo = new();
    private readonly FakeStockReservationGateway _stockGateway = new();
    private readonly FakeOrderRepository _orderRepo = new();
    private readonly FakePaymentRepository _paymentRepo = new();
    private readonly FakePaymentClient _paymentClient = new();
    private readonly FakeDomainEventPublisher _publisher = new();

    private CheckoutSagaCoordinator CreateCoordinator() =>
        new(
            _sagaRepo,
            _stockGateway,
            _orderRepo,
            _paymentRepo,
            _paymentClient,
            _publisher,
            NullLogger<CheckoutSagaCoordinator>.Instance);

    [Fact]
    public async Task Scenario1_HappyPath_CompletesSagaAndConfirmsOrder()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var command = new CheckoutSagaCommand(
            IdempotencyKey: "saga-happy-001",
            ProductId: 101,
            Quantity: 1,
            Amount: 49.01m);

        // Act
        var result = await coordinator.ExecuteSagaAsync(command);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(SagaStatus.Completed, result.Status);
        Assert.True(result.OrderId > 0);

        var persistedSaga = await _sagaRepo.GetByIdempotencyKeyAsync(command.IdempotencyKey);
        Assert.NotNull(persistedSaga);
        Assert.Equal(SagaStatus.Completed, persistedSaga.Status);
        Assert.Equal(InventoryReservationStatus.Reserved, persistedSaga.InventoryStatus);
        Assert.Equal(PaymentTransactionStatus.Successful, persistedSaga.PaymentStatus);

        Assert.Equal(1, _paymentRepo.MarkPaidCalls);
        Assert.Equal(0, _paymentRepo.CancelAndReleaseCalls);
        Assert.Equal(0, _stockGateway.ReleaseCalls);

        Assert.Contains(_publisher.PublishedEvents, e => e is CheckoutSagaStartedEvent);
        Assert.Contains(_publisher.PublishedEvents, e => e is CheckoutSagaCompletedEvent);
        Assert.Contains(_publisher.PublishedEvents, e => e is OrderConfirmedEvent);
    }

    [Fact]
    public async Task Scenario2_PaymentDeclined_TriggersCompensationAndReleasesInventory()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var command = new CheckoutSagaCommand(
            IdempotencyKey: "saga-decline-002",
            ProductId: 102,
            Quantity: 2,
            Amount: 99.02m); // .02 triggers payment decline

        // Act
        var result = await coordinator.ExecuteSagaAsync(command);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(SagaStatus.Compensated, result.Status);
        Assert.Contains("Card declined", result.Message);

        var persistedSaga = await _sagaRepo.GetByIdempotencyKeyAsync(command.IdempotencyKey);
        Assert.NotNull(persistedSaga);
        Assert.Equal(SagaStatus.Compensated, persistedSaga.Status);
        Assert.Equal(InventoryReservationStatus.Released, persistedSaga.InventoryStatus);
        Assert.Equal(PaymentTransactionStatus.Declined, persistedSaga.PaymentStatus);

        // Verify compensating transactions executed
        Assert.Equal(1, _stockGateway.ReleaseCalls);
        Assert.Equal(1, _paymentRepo.CancelAndReleaseCalls);
        Assert.Equal(0, _paymentRepo.MarkPaidCalls);

        Assert.Contains(_publisher.PublishedEvents, e => e is CheckoutSagaCompensatedEvent);
        Assert.Contains(_publisher.PublishedEvents, e => e is OrderCancelledEvent);
    }

    [Fact]
    public async Task Scenario3_PaymentTimeout_RetriesExhausted_TriggersCompensation()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var command = new CheckoutSagaCommand(
            IdempotencyKey: "saga-timeout-003",
            ProductId: 103,
            Quantity: 1,
            Amount: 149.03m); // .03 triggers transient timeout

        // Act
        var result = await coordinator.ExecuteSagaAsync(command);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(SagaStatus.Compensated, result.Status);

        // Verify retry occurred (max attempts = 2)
        Assert.Equal(2, _paymentClient.Calls);

        // Verify compensation occurred
        Assert.Equal(1, _stockGateway.ReleaseCalls);
        Assert.Equal(1, _paymentRepo.CancelAndReleaseCalls);

        var persistedSaga = await _sagaRepo.GetByIdempotencyKeyAsync(command.IdempotencyKey);
        Assert.NotNull(persistedSaga);
        Assert.Equal(SagaStatus.Compensated, persistedSaga.Status);
        Assert.Equal(PaymentTransactionStatus.TimedOut, persistedSaga.PaymentStatus);
    }

    [Fact]
    public async Task Scenario4_InventorySoldOut_FailsFastWithoutCallingPayment()
    {
        // Arrange
        _stockGateway.NextResult = ReservationResult.SoldOut;
        var coordinator = CreateCoordinator();
        var command = new CheckoutSagaCommand(
            IdempotencyKey: "saga-soldout-004",
            ProductId: 104,
            Quantity: 1,
            Amount: 49.01m);

        // Act
        var result = await coordinator.ExecuteSagaAsync(command);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(SagaStatus.Failed, result.Status);
        Assert.Equal(0, _paymentClient.Calls);
        Assert.Equal(0, _orderRepo.PersistCalls);
        Assert.Equal(0, _stockGateway.ReleaseCalls);

        var persistedSaga = await _sagaRepo.GetByIdempotencyKeyAsync(command.IdempotencyKey);
        Assert.NotNull(persistedSaga);
        Assert.Equal(SagaStatus.Failed, persistedSaga.Status);
        Assert.Equal(InventoryReservationStatus.Rejected, persistedSaga.InventoryStatus);
    }

    [Fact]
    public async Task Scenario5_DuplicateIdempotencyKey_ReturnsExistingSagaResultSafely()
    {
        // Arrange
        var coordinator = CreateCoordinator();
        var command = new CheckoutSagaCommand(
            IdempotencyKey: "saga-idempotent-005",
            ProductId: 105,
            Quantity: 1,
            Amount: 49.01m);

        // Act 1: Initial successful execution
        var result1 = await coordinator.ExecuteSagaAsync(command);
        Assert.True(result1.Success);

        // Act 2: Duplicate call with identical idempotency key
        var result2 = await coordinator.ExecuteSagaAsync(command);

        // Assert
        Assert.True(result2.Success);
        Assert.Equal(result1.SagaId, result2.SagaId);
        Assert.Equal(result1.OrderId, result2.OrderId);
        Assert.Equal(SagaStatus.Completed, result2.Status);

        // Crucial invariant: Payment provider and order repository were NOT invoked a second time!
        Assert.Equal(1, _paymentClient.Calls);
        Assert.Equal(1, _orderRepo.PersistCalls);
    }
}
