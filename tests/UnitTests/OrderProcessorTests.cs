using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

public class OrderProcessorTests
{
    private static OrderMessage Message(string key = "key-1") =>
        new(ProductId: 1, Quantity: 1, IdempotencyKey: key, CreatedAt: DateTimeOffset.UtcNow);

    private static OrderProcessor CreateProcessor(FakeOrderRepository repo) =>
        new(repo, NullLogger<OrderProcessor>.Instance);

    [Fact]
    public async Task ProcessAsync_PersistsOnce_ForNewOrder()
    {
        var repo = new FakeOrderRepository();

        await CreateProcessor(repo).ProcessAsync(Message(), CancellationToken.None);

        Assert.Equal(1, repo.PersistCalls);
        Assert.True(await repo.ExistsAsync("key-1"));
    }

    [Fact]
    public async Task ProcessAsync_IsIdempotent_ForDuplicateDelivery()
    {
        var repo = new FakeOrderRepository();
        repo.SeedExisting("key-1");

        await CreateProcessor(repo).ProcessAsync(Message(), CancellationToken.None);

        // At-least-once delivery must not produce a second order.
        Assert.Equal(0, repo.PersistCalls);
    }

    [Fact]
    public async Task ProcessAsync_ThrowsStockDrift_WhenRepositoryRejects()
    {
        var repo = new FakeOrderRepository { NextPersistSucceeds = false };

        await Assert.ThrowsAsync<StockDriftException>(
            () => CreateProcessor(repo).ProcessAsync(Message(), CancellationToken.None));

        Assert.Equal(1, repo.PersistCalls);
    }

    [Fact]
    public async Task ProcessAsync_SecondCall_IsNoOp_AfterSuccessfulPersist()
    {
        var repo = new FakeOrderRepository();
        var processor = CreateProcessor(repo);

        await processor.ProcessAsync(Message(), CancellationToken.None);
        await processor.ProcessAsync(Message(), CancellationToken.None); // redelivery

        Assert.Equal(1, repo.PersistCalls);
    }
}
