using FlashSale.Application.Automation;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Outbox;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlashSale.UnitTests;

public class SubmitOrderCompensationFailureTests
{
    private sealed class RejectingQueueProducer : IOrderQueueProducer
    {
        public ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default) =>
            ValueTask.FromResult(false); // always reject to trigger backpressure/compensation
    }

    private sealed class FakeReadModel : IOrderReadModel
    {
        public Task<int?> GetStockAsync(int productId, CancellationToken ct = default) => Task.FromResult<int?>(10);
        public Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default) =>
            Task.FromResult<ProductView?>(new ProductView(productId, "Test", 10, 9.99m, string.Empty));
        public Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult<OrderStatusView?>(null);
        public Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderSummaryView>>([]);
        public Task<IReadOnlyList<PendingPaymentView>> GetPendingPaymentOrdersAsync(int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingPaymentView>>([]);
    }

    [Fact]
    public async Task WhenQueueRejectsAndCompensationOutboxFails_RedisIsReleased_AndReconcilerCanRepair()
    {
        var gateway = new FakeStockReservationGateway();
        var outboxRepo = new FakeOutboxRepository();
        var outboxWriter = new OutboxEventWriter(outboxRepo);
        var transaction = new FakeWriteTransaction();
        var readModel = new FakeReadModel();
        var queue = new RejectingQueueProducer();
        var processor = new OrderProcessor(
            new FakeOrderRepository(),
            transaction,
            outboxWriter,
            NullLogger<OrderProcessor>.Instance);

        var useCase = new SubmitOrderUseCase(
            gateway,
            queue,
            readModel,
            processor,
            transaction,
            outboxWriter,
            new PaymentAutomationOptions(),
            NullLogger<SubmitOrderUseCase>.Instance);

        const int productId = 101;
        const int quantity = 2;
        const string key = "test-compensation-failure-key";

        // 1. Simulate failure specifically on inventory.released write
        outboxRepo.EnqueueFailureFactory = msg => msg.EventType == "inventory.released"
            ? new InvalidOperationException("Simulated transient database failure during inventory.released outbox write.")
            : null;

        var result = await useCase.ExecuteAsync(productId, quantity, key, null, CancellationToken.None);

        // Caller must receive SystemBusy (503) without unhandled crash
        Assert.Equal(SubmitOrderOutcome.SystemBusy, result.Outcome);

        // Redis reservation was promptly released
        Assert.Equal(1, gateway.ReleaseCalls);
        Assert.Contains(gateway.ReleasedReservations, r => r.ProductId == productId && r.Quantity == quantity && r.Key == key);

        // Outbox contains inventory.reserved, but NO inventory.released yet (failure window reproduced)
        Assert.Contains(outboxRepo.Enqueued, m => m.EventType == "inventory.reserved");
        Assert.DoesNotContain(outboxRepo.Enqueued, m => m.EventType == "inventory.released");
        var correlationId = outboxRepo.Enqueued.Single(m => m.EventType == "inventory.reserved").CorrelationId!.Value;

        // 2. Reconciler runs when database is recovered:
        outboxRepo.EnqueueFailureFactory = null;

        bool repaired = await useCase.ReconcileOrphanReservationAsync(productId, quantity, key, correlationId, CancellationToken.None);

        Assert.True(repaired, "Reconciler must successfully record compensation outbox event once database is healthy");
        Assert.Contains(outboxRepo.Enqueued, m => m.EventType == "inventory.released");
    }

    [Fact]
    public async Task WhenQueueRejectsAndCompensationOutboxFails_FailedMarkerIsWrittenToOutbox_ThenReconcilerRepairs()
    {
        var gateway = new FakeStockReservationGateway();
        var outboxRepo = new FakeOutboxRepository();
        var outboxWriter = new OutboxEventWriter(outboxRepo);
        var transaction = new FakeWriteTransaction();
        var readModel = new FakeReadModel();
        var queue = new RejectingQueueProducer();
        var processor = new OrderProcessor(
            new FakeOrderRepository(),
            transaction,
            outboxWriter,
            NullLogger<OrderProcessor>.Instance);
        var useCase = new SubmitOrderUseCase(
            gateway,
            queue,
            readModel,
            processor,
            transaction,
            outboxWriter,
            new PaymentAutomationOptions(),
            NullLogger<SubmitOrderUseCase>.Instance);

        const int productId = 101;
        const int quantity = 2;
        const string key = "remediation-marker-test-key";

        // 1. Simulate transient DB outage on the inventory.released compensation write.
        outboxRepo.EnqueueFailureFactory = msg => msg.EventType == "inventory.released"
            ? new InvalidOperationException("Simulated transient database failure during inventory.released outbox write.")
            : null;

        var result = await useCase.ExecuteAsync(productId, quantity, key, null, CancellationToken.None);

        // 2. Caller receives SystemBusy (HTTP 503) — no crash, no retry of the outbox write.
        Assert.Equal(SubmitOrderOutcome.SystemBusy, result.Outcome);

        // 3. Redis reservation is released promptly (backpressure shed load).
        Assert.Equal(1, gateway.ReleaseCalls);
        Assert.Contains(gateway.ReleasedReservations,
            r => r.ProductId == productId && r.Quantity == quantity && r.Key == key);

        // 4. Outbox data plane (reproduced failure window):
        //    - inventory.reserved is committed (order accepted)
        //    - inventory.released is NOT written (compensation failed)
        //    - inventory.compensation_failed IS written (durable marker for reconciler)
        Assert.Contains(outboxRepo.Enqueued, m => m.EventType == "inventory.reserved");
        Assert.DoesNotContain(outboxRepo.Enqueued, m => m.EventType == "inventory.released");
        Assert.Contains(outboxRepo.Enqueued, m => m.EventType == "inventory.compensation_failed");
        var correlationId = outboxRepo.Enqueued.Single(m => m.EventType == "inventory.reserved").CorrelationId!.Value;

        // 5. Database is recovered; reconciler re-runs.
        outboxRepo.EnqueueFailureFactory = null;
        bool repaired = await useCase.ReconcileOrphanReservationAsync(productId, quantity, key, correlationId, CancellationToken.None);

        Assert.True(repaired, "Reconciler must repair the orphan reservation once the database is healthy");
        Assert.Contains(outboxRepo.Enqueued, m => m.EventType == "inventory.released");
    }

    [Fact]
    public async Task RepeatedCompensationRepair_EmitsOneReleaseForTheSameRequest()
    {
        var gateway = new FakeStockReservationGateway();
        var outboxRepo = new FakeOutboxRepository();
        var writer = new OutboxEventWriter(outboxRepo);
        var transaction = new FakeWriteTransaction();
        var useCase = new SubmitOrderUseCase(
            gateway, new RejectingQueueProducer(), new FakeReadModel(),
            new OrderProcessor(new FakeOrderRepository(), transaction, writer, NullLogger<OrderProcessor>.Instance),
            transaction, writer, new PaymentAutomationOptions(), NullLogger<SubmitOrderUseCase>.Instance);

        const string key = "repair-once-key";
        outboxRepo.EnqueueFailureFactory = message => message.EventType == "inventory.released"
            ? new InvalidOperationException("Transient write failure") : null;

        Assert.Equal(SubmitOrderOutcome.SystemBusy,
            (await useCase.ExecuteAsync(101, 1, key, null, CancellationToken.None)).Outcome);
        outboxRepo.EnqueueFailureFactory = null;
        var correlationId = outboxRepo.Enqueued.Single(m => m.EventType == "inventory.reserved").CorrelationId!.Value;

        Assert.True(await useCase.ReconcileOrphanReservationAsync(101, 1, key, correlationId, CancellationToken.None));
        Assert.True(await useCase.ReconcileOrphanReservationAsync(101, 1, key, correlationId, CancellationToken.None));
        Assert.Single(outboxRepo.Enqueued, m => m.EventType == "inventory.released");
    }

    [Fact]
    public async Task LegacyReleaseWithRandomEventId_IsNotReleasedAgainDuringRepair()
    {
        var repo = new FakeOutboxRepository();
        const string key = "legacy-repair-key";
        var correlationId = Guid.NewGuid();
        repo.Enqueued.Add(new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            EventType = "inventory.released",
            AggregateType = OutboxEventWriter.OrderAggregate,
            AggregateId = key,
            CorrelationId = correlationId,
            Payload = "{}"
        });
        var transaction = new FakeWriteTransaction();
        var writer = new OutboxEventWriter(repo);
        var useCase = new SubmitOrderUseCase(
            new FakeStockReservationGateway(), new RejectingQueueProducer(), new FakeReadModel(),
            new OrderProcessor(new FakeOrderRepository(), transaction, writer, NullLogger<OrderProcessor>.Instance),
            transaction, writer, new PaymentAutomationOptions(), NullLogger<SubmitOrderUseCase>.Instance);

        Assert.True(await useCase.ReconcileOrphanReservationAsync(101, 1, key, correlationId, CancellationToken.None));
        Assert.Single(repo.Enqueued, m => m.EventType == "inventory.released" && m.AggregateId == key);
    }

    [Fact]
    public async Task SameHttpKeyAfterTwoRejectedQueueAttempts_HasOneReleasePerReservationAttempt()
    {
        var repo = new FakeOutboxRepository();
        var writer = new OutboxEventWriter(repo);
        var transaction = new FakeWriteTransaction();
        var useCase = new SubmitOrderUseCase(
            new FakeStockReservationGateway(), new RejectingQueueProducer(), new FakeReadModel(),
            new OrderProcessor(new FakeOrderRepository(), transaction, writer, NullLogger<OrderProcessor>.Instance),
            transaction, writer, new PaymentAutomationOptions(), NullLogger<SubmitOrderUseCase>.Instance);

        const string key = "rejected-then-retried-key";
        Assert.Equal(SubmitOrderOutcome.SystemBusy,
            (await useCase.ExecuteAsync(101, 1, key, null, CancellationToken.None)).Outcome);
        Assert.Equal(SubmitOrderOutcome.SystemBusy,
            (await useCase.ExecuteAsync(101, 1, key, null, CancellationToken.None)).Outcome);

        var reserved = repo.Enqueued.Where(m => m.EventType == "inventory.reserved" && m.AggregateId == key).ToList();
        var released = repo.Enqueued.Where(m => m.EventType == "inventory.released" && m.AggregateId == key).ToList();
        Assert.Equal(2, reserved.Count);
        Assert.Equal(2, released.Count);
        Assert.Equal(2, released.Select(m => m.MessageId).Distinct().Count());
        Assert.Equal(reserved.Select(m => m.CorrelationId).Order(), released.Select(m => m.CorrelationId).Order());
    }

    [Fact]
    public async Task LookupFailsAfterRedisRelease_ReturnsSystemBusyAndRecordsFailureMarker()
    {
        var repo = new FakeOutboxRepository();
        repo.LookupFailureFactory = () => new InvalidOperationException("Simulated PostgreSQL read outage");
        var gateway = new FakeStockReservationGateway();
        var writer = new OutboxEventWriter(repo);
        var transaction = new FakeWriteTransaction();
        var useCase = new SubmitOrderUseCase(
            gateway, new RejectingQueueProducer(), new FakeReadModel(),
            new OrderProcessor(new FakeOrderRepository(), transaction, writer, NullLogger<OrderProcessor>.Instance),
            transaction, writer, new PaymentAutomationOptions(), NullLogger<SubmitOrderUseCase>.Instance);

        Assert.Equal(SubmitOrderOutcome.SystemBusy,
            (await useCase.ExecuteAsync(101, 1, "lookup-outage", null, CancellationToken.None)).Outcome);
        Assert.Equal(1, gateway.ReleaseCalls);
        Assert.Single(repo.Enqueued, m => m.EventType == "inventory.reserved");
        Assert.DoesNotContain(repo.Enqueued, m => m.EventType == "inventory.released");
        Assert.Single(repo.Enqueued, m => m.EventType == "inventory.compensation_failed");
    }

    [Fact]
    public async Task DuplicateErrorWithoutCommittedRelease_DoesNotClaimCompensationSucceeded()
    {
        var repo = new FakeOutboxRepository();
        repo.EnqueueFailureFactory = message => message.EventType == "inventory.released"
            ? new DuplicateOutboxMessageException(message.MessageId) : null;
        var writer = new OutboxEventWriter(repo);
        var transaction = new FakeWriteTransaction();
        var useCase = new SubmitOrderUseCase(
            new FakeStockReservationGateway(), new RejectingQueueProducer(), new FakeReadModel(),
            new OrderProcessor(new FakeOrderRepository(), transaction, writer, NullLogger<OrderProcessor>.Instance),
            transaction, writer, new PaymentAutomationOptions(), NullLogger<SubmitOrderUseCase>.Instance);

        Assert.Equal(SubmitOrderOutcome.SystemBusy,
            (await useCase.ExecuteAsync(101, 1, "false-duplicate", null, CancellationToken.None)).Outcome);
        Assert.DoesNotContain(repo.Enqueued, m => m.EventType == "inventory.released");
        Assert.Single(repo.Enqueued, m => m.EventType == "inventory.compensation_failed");
    }
}
