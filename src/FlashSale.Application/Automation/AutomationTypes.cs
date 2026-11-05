namespace FlashSale.Application.Automation;

using FlashSale.Application.Inventory;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using Microsoft.Extensions.Logging;

public sealed class StockResyncResult
{
    public int ProductId { get; init; }
    public int Redis { get; init; }
    public int Postgres { get; init; }
    public int Drift => Postgres - Redis;
    public bool Fixed { get; init; }
}

public interface IAutomationRunRepository
{
    Task RecordAsync(string runId, string outcome, CancellationToken ct = default);
    Task<AutomationRun> CreateAsync(AutomationRun run, CancellationToken ct = default);
    Task UpdateAsync(AutomationRun run, CancellationToken ct = default);
    Task<AutomationRun?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRun>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<AutomationSummary> GetSummaryAsync(CancellationToken ct = default);
}

public sealed class PaymentAutomationOptions
{
    public int MaxReminders { get; set; } = 3;
    public int GracePeriodMinutes { get; set; } = 15;
}

public sealed class PaymentTimeoutRule
{
    public enum Decision { None, SendReminder, Cancel }

    public static Decision Evaluate(
        DateTimeOffset? dueAt,
        int reminderCount,
        int maxReminders,
        int gracePeriodMinutes,
        DateTimeOffset now)
    {
        if (dueAt is null) return Decision.None;
        if (dueAt.Value > now) return Decision.None;
        var overdueMinutes = (now - dueAt.Value).TotalMinutes;
        if (overdueMinutes < gracePeriodMinutes)
        {
            return reminderCount < maxReminders ? Decision.SendReminder : Decision.None;
        }
        return Decision.Cancel;
    }
}

public sealed class InventoryAutomationOptions
{
    public int ReorderThreshold { get; set; } = 5;
}
