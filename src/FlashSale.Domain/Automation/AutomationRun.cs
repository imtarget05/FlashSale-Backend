namespace FlashSale.Domain.Automation;

/// <summary>Automation workflow names (spec §4-§10).</summary>
public enum AutomationWorkflow
{
    OrderProcessing,
    PaymentTimeout,
    InventoryAutomation,
    DailyReport,
    AiContentGeneration,
    SupportTriage,
    RiskFraudRuleEngine
}

/// <summary>Automation run status (spec §11).</summary>
public enum AutomationRunStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Retrying,
    ManualReview
}

/// <summary>Audit record for each automation execution (spec §11).</summary>
public sealed class AutomationRun
{
    public int Id { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string TriggerId { get; set; } = string.Empty;
    public AutomationRunStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RetryCount { get; set; }
    public Guid CorrelationId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultSummary { get; set; }
}

/// <summary>Port for persisting automation run audit records (spec §11).</summary>
public interface IAutomationRunRepository
{
    Task<AutomationRun> CreateAsync(AutomationRun run, CancellationToken ct = default);
    Task UpdateAsync(AutomationRun run, CancellationToken ct = default);
    Task<AutomationRun?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationRun>> GetRecentAsync(int count, CancellationToken ct = default);
}