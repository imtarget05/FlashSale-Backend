using FlashSale.Domain.Automation;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the automation run audit repository (spec §11).</summary>
public sealed class AutomationRunRepository(AppDbContext db) : IAutomationRunRepository
{
    public async Task<AutomationRun> CreateAsync(AutomationRun run, CancellationToken ct = default)
    {
        db.AutomationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task UpdateAsync(AutomationRun run, CancellationToken ct = default)
    {
        db.AutomationRuns.Update(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AutomationRun?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await db.AutomationRuns.FindAsync([id], ct);

    public async Task<IReadOnlyList<AutomationRun>> GetRecentAsync(int count, CancellationToken ct = default) =>
        await db.AutomationRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .ToListAsync(ct);

    public async Task<AutomationSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        // DateTimeOffset.Date has Kind=Unspecified → implicit conversion picks
        // the machine's local offset (e.g. +07:00); Npgsql requires offset=0
        // (UTC) for 'timestamp with time zone'. Pin the boundary to UTC midnight.
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var runs = await db.AutomationRuns
            .Where(r => r.StartedAt >= today)
            .ToListAsync(ct);

        var successful = runs.Count(r => r.Status == AutomationRunStatus.Success);
        var failed = runs.Count(r => r.Status == AutomationRunStatus.Failed);
        var retrying = runs.Count(r => r.Status == AutomationRunStatus.Retrying);
        var manualReview = runs.Count(r => r.Status == AutomationRunStatus.ManualReview);

        var durations = runs
            .Where(r => r.FinishedAt.HasValue)
            .Select(r => (r.FinishedAt.Value - r.StartedAt).TotalSeconds)
            .ToList();
        var avgDuration = durations.Count > 0 ? durations.Average() : 0;

        var topFailing = runs
            .Where(r => r.Status == AutomationRunStatus.Failed)
            .GroupBy(r => r.WorkflowName)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        return new AutomationSummary(
            runs.Count,
            successful,
            failed,
            retrying,
            manualReview,
            avgDuration,
            topFailing);
    }
}