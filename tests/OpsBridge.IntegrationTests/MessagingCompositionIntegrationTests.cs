using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using OpsBridge.Domain.InboxOutbox;
using OpsBridge.Domain.Ingestion;
using OpsBridge.Infrastructure;
using OpsBridge.Infrastructure.Messaging;
using OpsBridge.Infrastructure.Persistence;

namespace OpsBridge.IntegrationTests;

[Collection("OpsBridgeIntegration")]
public class MessagingCompositionIntegrationTests(OpsBridgeFixture fixture)
{
    [Fact]
    public async Task BrokerDown_DoesNotBlockDurableOutboxWrites()
    {
        await fixture.ResetAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OpsBridgeDb"] = fixture.ConnectionString,
            ["ConnectionStrings:RabbitMQ"] = "amqp://guest:guest@127.0.0.1:1/",
            ["OpsBridge:Messaging:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpsBridgeInfrastructure(config);
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        try
        {
            foreach (var service in hosted) await service.StartAsync(CancellationToken.None);
            Assert.False(provider.GetRequiredService<MessagingRuntimeStatus>().ConsumerConnected);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
            var id = Guid.NewGuid();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                EventType = "GoodsReceiptConfirmed",
                Topic = "opsbridge.events.inbound.goods-receipt",
                Payload = "{}",
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            Assert.True(await db.OutboxMessages.AnyAsync(o => o.Id == id));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            foreach (var service in hosted.AsEnumerable().Reverse())
                await service.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task EnabledMessaging_StartsOutboxAndConsumerAgainstRealBroker()
    {
        await fixture.ResetAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OpsBridgeDb"] = fixture.ConnectionString,
            ["ConnectionStrings:RabbitMQ"] = fixture.RabbitMqConnectionString,
            ["OpsBridge:Messaging:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpsBridgeInfrastructure(config);
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.Equal(2, hosted.Count);

        try
        {
            foreach (var service in hosted) await service.StartAsync(CancellationToken.None);
            var status = provider.GetRequiredService<MessagingRuntimeStatus>();
            for (var i = 0; i < 100 && !status.ConsumerConnected; i++)
                await Task.Delay(50);
            Assert.True(status.ConsumerConnected);

            var eventId = Guid.NewGuid();
            var payload = File.ReadAllText(Path.Combine(
                    "..", "..", "..", "..", "fixtures", "valid-goods-receipt-v1.json"))
                .Replace("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d", eventId.ToString(), StringComparison.Ordinal)
                .Replace("b2c3d4e5-f6a7-8b9c-0d1e-2f3a4b5c6d7e", Guid.NewGuid().ToString(), StringComparison.Ordinal);
            var outboxId = Guid.NewGuid();
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
                db.IngestedEvents.Add(new IngestedEvent
                {
                    EventId = eventId,
                    EventType = "GoodsReceiptConfirmed",
                    SchemaVersion = "1.0",
                    Source = "factoryflow",
                    RawPayload = payload,
                    Status = EventProcessingStatus.Received,
                    OccurredAt = DateTimeOffset.UtcNow,
                    ReceivedAt = DateTimeOffset.UtcNow
                });
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = outboxId,
                    EventType = "GoodsReceiptConfirmed",
                    Topic = "opsbridge.events.inbound.goods-receipt",
                    Payload = payload,
                    Status = "Pending",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var published = false;
            var processed = false;
            for (var i = 0; i < 100 && !(published && processed); i++)
            {
                await Task.Delay(100);
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OpsBridgeDbContext>();
                published = await db.OutboxMessages.AsNoTracking()
                    .AnyAsync(o => o.Id == outboxId && o.Status == "Published");
                processed = await db.GoodsReceipts.AsNoTracking()
                    .AnyAsync(g => g.SourceEventId == eventId);
            }
            Assert.True(published, "The registered publisher must drain the PostgreSQL outbox.");
            Assert.True(processed, "The registered consumer must persist one canonical goods receipt.");
        }
        finally
        {
            foreach (var service in hosted.AsEnumerable().Reverse())
                await service.StopAsync(CancellationToken.None);
        }
    }
}
