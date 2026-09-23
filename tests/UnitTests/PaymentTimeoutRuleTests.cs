using FlashSale.Application.Automation;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

/// <summary>
/// Spec §16 "timeout rule" tests: pure decisions for abandoned-payment
/// recovery (§5), to-the-minute, no clock mocking needed.
/// </summary>
public class PaymentTimeoutRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoPaymentWindow_NeverActs()
    {
        var decision = PaymentTimeoutRule.Evaluate(null, 0, maxReminders: 3, gracePeriodMinutes: 15, Now);
        Assert.Equal(PaymentTimeoutRule.Decision.None, decision);
    }

    [Fact]
    public void BeforeDue_IsSilent()
    {
        var due = Now.AddMinutes(5);
        var decision = PaymentTimeoutRule.Evaluate(due, 0, 3, 15, Now);
        Assert.Equal(PaymentTimeoutRule.Decision.None, decision);
    }

    [Fact]
    public void AtDue_SendsReminder_UntilCap()
    {
        var due = Now.AddMinutes(-1);

        Assert.Equal(PaymentTimeoutRule.Decision.SendReminder,
            PaymentTimeoutRule.Evaluate(due, 0, 3, 15, Now));
        Assert.Equal(PaymentTimeoutRule.Decision.SendReminder,
            PaymentTimeoutRule.Evaluate(due, 2, 3, 15, Now));

        // Cap reached (MAX_PAYMENT_REMINDERS): no more reminders, but no
        // cancellation either — still inside the grace window.
        Assert.Equal(PaymentTimeoutRule.Decision.None,
            PaymentTimeoutRule.Evaluate(due, 3, 3, 15, Now));
    }

    [Fact]
    public void PastGrace_Cancels_EvenWithRemindersLeft()
    {
        var due = Now.AddMinutes(-16); // -1 timeout, -15 grace => past grace
        var decision = PaymentTimeoutRule.Evaluate(due, 0, 3, 15, Now);
        Assert.Equal(PaymentTimeoutRule.Decision.Cancel, decision);
    }

    [Fact]
    public void GraceBoundary_IsInclusive()
    {
        // exactly due+grace: cancel fires at the boundary, not a reminder
        var due = Now.AddMinutes(-15);
        var decision = PaymentTimeoutRule.Evaluate(due, 0, 3, 15, Now);
        Assert.Equal(PaymentTimeoutRule.Decision.Cancel, decision);
    }

    [Fact]
    public void ZeroGrace_CancelsImmediately_WhenDue()
    {
        var due = Now;
        var decision = PaymentTimeoutRule.Evaluate(due, 0, 3, 0, Now);
        Assert.Equal(PaymentTimeoutRule.Decision.Cancel, decision);
    }
}

/// <summary>Guarded payment transitions must be exactly-once (spec §12 idempotency).</summary>
public class RecordPaymentGuardLogicTests
{
    [Fact]
    public void StatusGuardSemantics_DocumentedByContract()
    {
        // The guard lives in PaymentRepository SQL (WHERE Status='PendingPayment');
        // this test pins the enum contract the SQL string-compares against —
        // renaming the enum member without migration would silently break guards.
        Assert.Equal("PendingPayment", OrderStatus.PendingPayment.ToString());
        Assert.Equal("Cancelled", OrderStatus.Cancelled.ToString());
        Assert.Equal("Confirmed", OrderStatus.Confirmed.ToString());
    }
}