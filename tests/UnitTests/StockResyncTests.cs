using FlashSale.Application.Inventory;
using FlashSale.Application.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

/// <summary>
/// Stock reconciliation (ADR-003 runbook): the Redis mirror vs PostgreSQL truth.
/// The existing <see cref="FakeStockReservationGateway"/> is stateless (always
/// reports 100, <c>SetStockAsync</c> is a no-op), so drift assertions need
/// stateful doubles — defined here, next to the tests, following the same
/// fake-the-port pattern as <c>SagaTestDoubles.cs</c>.
/// </summary>
public sealed class StockResyncTests
{
    private sealed class FakeReadModel(Dictionary<int, int> stock) : IOrderReadModel
    {
        public Task<int?> GetStockAsync(int productId, CancellationToken ct = default) =>
            Task.FromResult(stock.TryGetValue(productId, out var qty) ? (int?)qty : null);

        public Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default) =>
            Task.FromResult(stock.TryGetValue(productId, out var qty)
                ? new ProductView(productId, "Test", qty, 9.99m, string.Empty)
                : null);

        public Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult<OrderStatusView?>(null);

        public Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderSummaryView>>([]);

        public Task<IReadOnlyList<PendingPaymentView>> GetPendingPaymentOrdersAsync(int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingPaymentView>>([]);
    }

    private sealed class FakeStockGateway(Dictionary<int, int> mirror) : IStockReservationGateway
    {
        public int SetStockCalls { get; private set; }
        public int? LastSetValue { get; private set; }

        public Task<ReservationResult> TryReserveAsync(int productId, int quantity, string idempotencyKey) =>
            Task.FromResult(ReservationResult.Reserved);

        public Task ReleaseReservationAsync(int productId, int quantity, string idempotencyKey) =>
            Task.CompletedTask;

        public Task SetStockAsync(int productId, int stock)
        {
            SetStockCalls++;
            LastSetValue = stock;
            mirror[productId] = stock;
            return Task.CompletedTask;
        }

        public Task<int?> GetCachedStockAsync(int productId) =>
            Task.FromResult(mirror.TryGetValue(productId, out var qty) ? (int?)qty : null);
    }

    private static StockResyncUseCase Create(Dictionary<int, int> postgres, Dictionary<int, int> redis, out FakeStockGateway gateway)
    {
        gateway = new FakeStockGateway(redis);
        return new StockResyncUseCase(
            new FakeReadModel(postgres),
            gateway,
            NullLogger<StockResyncUseCase>.Instance);
    }

    [Fact]
    public async Task DriftDetected_ReportsRedisPostgresAndDrift()
    {
        var useCase = Create(new() { [7] = 100 }, new() { [7] = 70 }, out _);

        var result = await useCase.ExecuteAsync(7, dryRun: true);

        Assert.NotNull(result);
        Assert.Equal(7, result.ProductId);
        Assert.Equal(70, result.Redis);
        Assert.Equal(100, result.Postgres);
        Assert.Equal(-30, result.Drift);
        Assert.False(result.Fixed);
    }

    [Fact]
    public async Task DryRun_WritesNothing()
    {
        var redis = new Dictionary<int, int> { [7] = 70 };
        var useCase = Create(new() { [7] = 100 }, redis, out var gateway);

        var result = await useCase.ExecuteAsync(7, dryRun: true);

        Assert.NotNull(result);
        Assert.False(result.Fixed);
        Assert.Equal(0, gateway.SetStockCalls);
        Assert.Equal(70, redis[7]);
    }

    [Fact]
    public async Task Apply_FixesExactlyTheDrift()
    {
        var redis = new Dictionary<int, int> { [7] = 70 };
        var useCase = Create(new() { [7] = 100 }, redis, out var gateway);

        var result = await useCase.ExecuteAsync(7, dryRun: false);

        Assert.NotNull(result);
        Assert.Equal(-30, result.Drift);
        Assert.True(result.Fixed);
        Assert.Equal(1, gateway.SetStockCalls);
        Assert.Equal(100, gateway.LastSetValue);
        Assert.Equal(100, redis[7]);
    }

    [Fact]
    public async Task NoDrift_ReturnsCleanWithoutWriting()
    {
        var useCase = Create(new() { [7] = 100 }, new() { [7] = 100 }, out var gateway);

        var result = await useCase.ExecuteAsync(7, dryRun: false);

        Assert.NotNull(result);
        Assert.Equal(100, result.Redis);
        Assert.Equal(100, result.Postgres);
        Assert.Equal(0, result.Drift);
        Assert.False(result.Fixed);
        Assert.Equal(0, gateway.SetStockCalls);
    }

    [Fact]
    public async Task UnknownProduct_ReturnsNull()
    {
        var useCase = Create(new() { [7] = 100 }, new() { [7] = 70 }, out var gateway);

        var result = await useCase.ExecuteAsync(999, dryRun: false);

        Assert.Null(result);
        Assert.Equal(0, gateway.SetStockCalls);
    }
}
