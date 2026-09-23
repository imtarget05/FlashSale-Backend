using FlashSale.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Outbox;

/// <summary>
/// Background worker polling and relaying Transactional Outbox records (Phase 10).
/// Also surfaces stuck rows: a dispatcher that only reports "dispatched N" while
/// rows sit dead-lettered would hide the exact failure this pattern exists to prevent.
/// </summary>
public sealed class OutboxDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcherHostedService started.");

        var lastReportedStuck = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcherUseCase>();
                var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

                var count = await dispatcher.ExecuteBatchAsync(50, stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("Dispatched {Count} outbox messages.", count);
                }

                // Edge-triggered: log when the stuck set appears or changes size,
                // not every 2 s.
                var stuck = await outbox.CountStuckAsync(stoppingToken);
                if (stuck != lastReportedStuck)
                {
                    if (stuck > 0)
                    {
                        logger.LogWarning(
                            "Outbox has {StuckCount} stuck message(s) — dead-lettered or backing off. " +
                            "Inspect GET /api/outbox/stuck; recover with POST /api/outbox/requeue.",
                            stuck);
                    }
                    else
                    {
                        logger.LogInformation("Outbox stuck set cleared.");
                    }

                    lastReportedStuck = stuck;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing transactional outbox messages.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        logger.LogInformation("OutboxDispatcherHostedService stopped.");
    }
}
