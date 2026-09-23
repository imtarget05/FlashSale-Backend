using FlashSale.Application.Automation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Automation;

/// <summary>
/// Periodic low-stock scan (spec §6). Same use case as the manual endpoint, so
/// timer and manual runs differ only by TriggerType in the audit record.
/// </summary>
public sealed class LowStockScanHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LowStockScanHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue<int?>(
            $"{InventoryAutomationOptions.SectionName}:ScanIntervalSeconds") ?? 60;
        var interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds));

        logger.LogInformation(
            "LowStockScanHostedService started — scanning every {Interval}s (spec §6).",
            interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var useCase = scope.ServiceProvider.GetRequiredService<LowStockAlertUseCase>();
                var result = await useCase.ExecuteAsync("timer", stoppingToken);
                if (result.AlertsCreated > 0)
                    logger.LogInformation(
                        "Low-stock scan: scanned={Scanned} alerts_created={Created} products=[{Products}].",
                        result.Scanned, result.AlertsCreated, string.Join(",", result.ProductIds));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Low-stock scan iteration failed — retrying next interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("LowStockScanHostedService stopping.");
    }
}