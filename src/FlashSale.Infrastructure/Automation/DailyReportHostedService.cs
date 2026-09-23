using FlashSale.Application.Automation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSale.Infrastructure.Automation;

/// <summary>
/// Daily report scheduler (spec §7 "trigger: daily scheduler"). Fires once per
/// UTC day at the configured hour; the unique report date makes a duplicate run
/// harmless (upsert), and a missed hour window is caught by the next check.
/// </summary>
public sealed class DailyReportHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DailyReportHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue<int?>(
            $"{ReportingAutomationOptions.SectionName}:ScanIntervalSeconds") ?? 300;
        var runAtHour = configuration.GetValue<int?>(
            $"{ReportingAutomationOptions.SectionName}:RunAtHourUtc") ?? 0;
        var interval = TimeSpan.FromSeconds(Math.Max(15, intervalSeconds));

        logger.LogInformation(
            "DailyReportHostedService started — target hour {Hour}:00 UTC, checking every {Interval}s.",
            runAtHour, interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now.Hour == runAtHour)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var reports = scope.ServiceProvider.GetRequiredService<Application.Persistence.IDailyReportRepository>();
                    var (dayStart, _) = ReportWindow.ForDay(now);
                    if (await reports.GetByDateAsync(dayStart, stoppingToken) is null)
                    {
                        var useCase = scope.ServiceProvider.GetRequiredService<DailyReportUseCase>();
                        var report = await useCase.ExecuteAsync(dayStart, "timer", stoppingToken);
                        logger.LogInformation(
                            "Scheduled daily report written for {Date} (orders={Orders}, revenue={Revenue}).",
                            dayStart.ToString("yyyy-MM-dd"), report.TotalOrders, report.Revenue);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily report scheduler iteration failed — retrying next interval.");
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

        logger.LogInformation("DailyReportHostedService stopping.");
    }
}