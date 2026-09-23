using FlashSale.Domain.Automation;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

/// <summary>Phase 6 dashboard roll-up (spec §14): reads only what AutomationRuns recorded.</summary>
public sealed class AutomationDashboardTests
{
    private static AppDbContext CreateDb(params AutomationRun[] runs)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options);
        db.AutomationRuns.AddRange(runs);
        db.SaveChangesAsync().GetAwaiter().GetResult();
        return db;
    }

    [Fact]
    public async Task GetSummaryAsync_CountsRunsAndStatuses_TodayOnly()
    {
        var db = CreateDb(
            new AutomationRun { WorkflowName = "OrderProcessing", Status = AutomationRunStatus.Success, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30), FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-20) },
            new AutomationRun { WorkflowName = "PaymentTimeout", Status = AutomationRunStatus.Failed, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10), FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new AutomationRun { WorkflowName = "PaymentTimeout", Status = AutomationRunStatus.Failed, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-8) },
            new AutomationRun { WorkflowName = "InventoryAutomation", Status = AutomationRunStatus.Retrying, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2) });

        var repo = new AutomationRunRepository(db);
        var summary = await repo.GetSummaryAsync();

        Assert.Equal(4, summary.RunsToday);
        Assert.Equal(1, summary.Successful);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(1, summary.Retrying);
        Assert.Equal(0, summary.ManualReview);
        Assert.Equal("PaymentTimeout", summary.TopFailingWorkflow);
        Assert.True(summary.AverageDurationSeconds > 0);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyDb_ReturnsZeros()
    {
        var db = CreateDb();
        var repo = new AutomationRunRepository(db);
        var summary = await repo.GetSummaryAsync();

        Assert.Equal(0, summary.RunsToday);
        Assert.Equal(0, summary.Successful);
        Assert.Null(summary.TopFailingWorkflow);
    }
}
