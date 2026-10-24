using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Outbox;
using FlashSale.Application.Persistence;
using FlashSale.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

public class OrderProcessorHostTests
{
    [Fact]
    public async Task FailedRetryPublish_DoesNotAcknowledgeOriginalDelivery()
    {
        var repository = new FakeOrderRepository { ThrowOnPersist = true };
        var services = new ServiceCollection();
        services.AddSingleton<IOrderRepository>(repository);
        services.AddScoped<IWriteTransaction, FakeWriteTransaction>();
        services.AddScoped<IOutboxRepository, FakeOutboxRepository>();
        services.AddScoped<OutboxEventWriter>();
        services.AddScoped<OrderProcessor>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<OrderProcessor>>(
            NullLogger<OrderProcessor>.Instance);
        await using var provider = services.BuildServiceProvider();

        var producer = new RejectingProducer();
        var host = new OrderProcessorHost(
            new EmptyConsumer(), producer, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderProcessorHost>.Instance);
        var acknowledged = false;
        var entry = new QueueEntry
        {
            Message = new OrderMessage(1, 1, "retry-rejected", DateTimeOffset.UtcNow),
            Complete = () => { acknowledged = true; return Task.CompletedTask; }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.ProcessEntryAsync(entry, CancellationToken.None));

        Assert.False(acknowledged);
        Assert.Equal(1, producer.Attempts);
    }

    private sealed class RejectingProducer : IOrderQueueProducer
    {
        public int Attempts { get; private set; }
        public ValueTask<bool> EnqueueAsync(OrderMessage message, CancellationToken ct = default)
        {
            Attempts++;
            return ValueTask.FromResult(false);
        }
    }

    private sealed class EmptyConsumer : IOrderQueueConsumer
    {
        public ValueTask<QueueEntry?> DequeueAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<QueueEntry?>(null);
    }
}
