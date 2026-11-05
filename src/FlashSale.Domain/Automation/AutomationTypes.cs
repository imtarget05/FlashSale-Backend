namespace FlashSale.Domain.Automation;

public enum AutomationRunStatus
{
    Pending,
    Success,
    Failed,
    DeadLettered
}

public enum AutomationWorkflow
{
    InventoryAutomation,
    PaymentAutomation,
    LowStockAlert
}

public enum AutomationEventOutcome
{
    Processed,
    Duplicate,
    Unreadable
}

public sealed class AutomationRun
{
    public int Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public AutomationRunStatus Status { get; set; } = AutomationRunStatus.Pending;
    public string WorkflowName { get; set; } = string.Empty;
    public string ResultSummary { get; set; } = string.Empty;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record AutomationSummary(
    int Processed,
    int Success,
    int Failed,
    int DeadLetter,
    int Duplicate,
    int Unreadable,
    DateTimeOffset? LastRunAt);

public static class ConsumerName
{
    public const string Value = "flashsale.automation.v1";
}
