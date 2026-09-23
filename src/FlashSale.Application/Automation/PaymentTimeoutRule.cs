namespace FlashSale.Application.Automation;

/// <summary>
/// Pure timeout rule for abandoned-payment recovery (spec §5). No I/O, no
/// clock of its own — the caller passes <c>now</c>, which is what makes the
/// rule unit-testable to the minute (spec §16 "timeout rule" test).
/// </summary>
/// <remarks>
/// Rule (spec §5):
/// <c>IF status = PENDING_PAYMENT AND created_at &gt; timeout → remind</c>;
/// <c>IF still unpaid after grace → cancel + release inventory</c>.
/// The due instant is <c>PaymentDueAt = created_at + TimeoutMinutes</c>, so the
/// grace cancel always wins over an unsent reminder: past grace there is no
/// point reminding a customer about an order that is about to disappear.
/// </remarks>
public static class PaymentTimeoutRule
{
    public enum Decision
    {
        /// <summary>Not due yet, reminders exhausted, or no payment window.</summary>
        None,
        /// <summary>Payment window elapsed — send one reminder (count-limited).</summary>
        SendReminder,
        /// <summary>Grace period elapsed — cancel the order and release stock.</summary>
        Cancel
    };

    public static Decision Evaluate(
        DateTimeOffset? paymentDueAt,
        int reminderCount,
        int maxReminders,
        int gracePeriodMinutes,
        DateTimeOffset now)
    {
        if (paymentDueAt is null)
            return Decision.None; // no payment window (e.g. legacy/test order)

        if (now >= paymentDueAt.Value.AddMinutes(gracePeriodMinutes))
            return Decision.Cancel;

        if (now >= paymentDueAt.Value && reminderCount < maxReminders)
            return Decision.SendReminder;

        return Decision.None;
    }
}