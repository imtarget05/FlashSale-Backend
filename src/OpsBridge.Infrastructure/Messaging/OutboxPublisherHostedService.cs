using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpsBridge.Application.InboxOutbox;
using OpsBridge.Application.Messaging;

namespace OpsBridge.Infrastructure.Messaging;

public sealed class OutboxPublisherHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxPublisherHostedService started — draining Outbox with Publisher Confirms.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessPendingBatchAsync(stoppingToken);
                if (processedCount == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in OutboxPublisherHostedService loop. Backing off for 2s.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        logger.LogInformation("OutboxPublisherHostedService stopped.");
    }

    public async Task<int> ProcessPendingBatchAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var publisher = scope.ServiceProvider.GetService<IEventPublisher>();

        if (publisher == null)
        {
            // Publisher not registered or broker not available yet
            return 0;
        }

        var pendingMessages = await outboxRepo.GetPendingMessagesAsync(batchSize: 20, ct);
        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        int successCount = 0;
        foreach (var msg in pendingMessages)
        {
            try
            {
                var published = await publisher.PublishAsync(msg.Topic, msg.Payload, ct: ct);
                if (published)
                {
                    await outboxRepo.MarkProcessedAsync(msg.Id, ct);
                    successCount++;
                }
                else
                {
                    await outboxRepo.RecordFailureAsync(msg.Id, "Broker publisher confirmation rejected.", ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish Outbox message {Id} to topic {Topic}", msg.Id, msg.Topic);
                await outboxRepo.RecordFailureAsync(msg.Id, ex.Message, ct);
            }
        }

        return successCount;
    }
}
