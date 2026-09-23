namespace FlashSale.Application.Automation;

/// <summary>
/// Payment automation configuration (spec §5: PAYMENT_TIMEOUT_MINUTES,
/// PAYMENT_GRACE_PERIOD_MINUTES, MAX_PAYMENT_REMINDERS — never hard-coded;
/// bound from the "Automation:Payment" configuration section in every
/// composition root).
/// </summary>
public sealed class PaymentAutomationOptions
{
    public const string SectionName = "Automation:Payment";

    /// <summary>Minutes after creation before the first reminder fires.</summary>
    public int TimeoutMinutes { get; set; } = 15;

    /// <summary>Extra minutes after the timeout before the order is cancelled.</summary>
    public int GracePeriodMinutes { get; set; } = 15;

    /// <summary>Reminder cap per order (spec §5 MAX_PAYMENT_REMINDERS).</summary>
    public int MaxPaymentReminders { get; set; } = 3;

    /// <summary>Background scan cadence in seconds (PaymentTimeoutHostedService).</summary>
    public int ScanIntervalSeconds { get; set; } = 60;
}