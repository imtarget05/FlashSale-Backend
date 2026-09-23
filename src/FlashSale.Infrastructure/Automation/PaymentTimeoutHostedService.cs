using FlashSale.Application.Automation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Automation;

/// <summary>
/// Periodic payment-timeout scan (spec §5). Runs the SAME
/// <see cref="PaymentAutomationUseCase"/> the API's manual trigger uses, so
/// timer and manual runs are one code path with different trigger_type in the
/// audit record. The host never dies: a failed scan is recorded by the use
/// case (spec §11) and the loop just waits for the next interval.
/// </summary>
public sealed class PaymentTimeoutHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PaymentTimeoutHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue<int?>(
            $"{PaymentAutomationOptions.SectionName}:ScanIntervalSeconds") ?? 60;
        var interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds));

        logger.LogInformation(
            "PaymentTimeoutHostedService started — scanning every {Interval}s (spec §5).",
            interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var useCase = scope.ServiceProvider.GetRequiredService<PaymentAutomationUseCase>();
                var result = await useCase.ExecuteAsync("timer", stoppingToken);
                if (result.Scanned > 0)
                    logger.LogInformation(
                        "Payment timeout scan: scanned={Scanned} reminded={Reminded} cancelled={Cancelled}.",
                        result.Scanned, result.Reminded, result.Cancelled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Defensive: the use case already records failures; this keeps
                // the loop alive for truly unexpected infrastructure errors.
                logger.LogError(ex, "Payment timeout scan iteration failed — retrying next interval.");
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

        logger.LogInformation("PaymentTimeoutHostedService stopping.");
    }
}