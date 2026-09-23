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
}