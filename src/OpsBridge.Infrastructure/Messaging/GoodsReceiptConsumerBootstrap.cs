using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpsBridge.Infrastructure.Messaging;

/// <summary>
/// Reconnects the consumer without taking the webhook API down. Webhooks
/// remain durable in PostgreSQL outbox while RabbitMQ is unavailable.
/// </summary>
public sealed class GoodsReceiptConsumerBootstrap(
    string connectionString,
    IServiceScopeFactory scopeFactory,
    ILogger<GoodsReceiptConsumerHost> logger,
    MessagingRuntimeStatus status) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            GoodsReceiptConsumerHost? host = null;
            try
            {
                host = await GoodsReceiptConsumerHost.CreateAsync(
                    connectionString, scopeFactory, logger);
                await host.StartAsync(stoppingToken);
                if (host.ExecuteTask is { } execution)
                {
                    while (!stoppingToken.IsCancellationRequested && !execution.IsCompleted)
                    {
                        if (!host.BrokerConnectionOpen)
                            throw new IOException("RabbitMQ consumer channel closed.");
                        status.SetConsumerConnected(true);
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                    await execution.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpsBridge RabbitMQ consumer unavailable; retrying connection.");
            }
            finally
            {
                status.SetConsumerConnected(false);
                if (host is not null)
                {
                    try { await host.StopAsync(CancellationToken.None); }
                    catch (Exception ex) { logger.LogWarning(ex, "Consumer stop failed."); }
                    host.Dispose();
                }
            }

            try { await Task.Delay(RetryDelay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
