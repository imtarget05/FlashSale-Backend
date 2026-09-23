using FlashSale.Application.Support;

namespace FlashSale.UnitTests;

/// <summary>
/// Spec §9 + §13: unknown categories must never be invented (GENERAL fallback);
/// sensitive categories and ungrounded order questions always demand a human.
/// </summary>
public class SupportTriageRulesTests
{
    [Theory]
    [InlineData("ORDER_STATUS", SupportCategory.OrderStatus)]
    [InlineData("order_status", SupportCategory.OrderStatus)]
    [InlineData("Payment", SupportCategory.Payment)]
    [InlineData("CANCELLATION", SupportCategory.Cancellation)]
    [InlineData("refund", SupportCategory.Refund)]
    [InlineData("SHIPPING", SupportCategory.Shipping)]
    [InlineData("general", SupportCategory.General)]
    public void ParseCategory_MapsKnownValues(string raw, SupportCategory expected)
    {
        Assert.Equal(expected, SupportTriageRules.ParseCategory(raw));
    }

    [Theory]
    [InlineData("BANKRUPTCY")]
    [InlineData("give_me_money")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseCategory_FallsBackToGeneral_InsteadOfInventing(string? raw)
    {
        Assert.Equal(SupportCategory.General, SupportTriageRules.ParseCategory(raw));
    }

    [Theory]
    [InlineData(SupportCategory.Refund, true)]
    [InlineData(SupportCategory.Cancellation, true)]
    [InlineData(SupportCategory.Payment, false)]
    [InlineData(SupportCategory.OrderStatus, false)]
    [InlineData(SupportCategory.Shipping, false)]
    [InlineData(SupportCategory.General, false)]
    public void IsSensitive_OnlyRefundAndCancellation(SupportCategory category, bool expected)
    {
        Assert.Equal(expected, SupportTriageRules.IsSensitive(category));
    }

    [Fact]
    public void NeedsHumanReview_UngroundedOrderQuestion_AlwaysHuman()
    {
        // Order-related question, no facts from the database: the draft cannot
        // be grounded, so it needs a human no matter how confident the model is.
        Assert.True(SupportTriageRules.NeedsHumanReview(SupportCategory.OrderStatus, hasGroundingFacts: false));
        Assert.False(SupportTriageRules.NeedsHumanReview(SupportCategory.OrderStatus, hasGroundingFacts: true));
    }

    [Fact]
    public void NeedsHumanReview_Sensitive_AlwaysHuman_EvenWhenGrounded()
    {
        Assert.True(SupportTriageRules.NeedsHumanReview(SupportCategory.Refund, hasGroundingFacts: true));
    }

    [Fact]
    public void NeedsHumanReview_GeneralWithoutFacts_LocalHandling()
    {
        Assert.False(SupportTriageRules.NeedsHumanReview(SupportCategory.General, hasGroundingFacts: false));
    }

    [Fact]
    public void TriageParser_ExtractsCategoryAndDraft()
    {
        var ok = SupportTriageParser.TryParse(
            """{"category":"REFUND","draftResponse":"A human will review your refund request."}""",
            out var category, out var draft);

        Assert.True(ok);
        Assert.Equal(SupportCategory.Refund, category);
        Assert.NotEmpty(draft);
    }

    [Fact]
    public void TriageParser_RejectsMissingDraft()
    {
        var ok = SupportTriageParser.TryParse("""{"category":"ORDER_STATUS"}""", out _, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("I want a refund right now")]
    [InlineData("Please give me my MONEY BACK")]
    [InlineData("This was a chargeback situation")]
    public void EscalateIfObvious_GeneralPlusSensitiveKeyword_ForcesSensitive(string message)
    {
        // qwen3:4b sometimes classifies clear refund requests as GENERAL —
        // the deterministic guard must force the sensitive category so §13
        // human review can never be skipped.
        var category = SupportTriageRules.EscalateIfObvious(message, SupportCategory.General);
        Assert.Equal(SupportCategory.Refund, category);
        Assert.True(SupportTriageRules.NeedsHumanReview(category, hasGroundingFacts: false));
    }

    [Fact]
    public void EscalateIfObvious_GeneralPlusCancelKeyword_ForcesCancellation()
    {
        Assert.Equal(SupportCategory.Cancellation,
            SupportTriageRules.EscalateIfObvious("please cancel my order", SupportCategory.General));
    }

    [Fact]
    public void EscalateIfObvious_SpecificModelCategory_IsNeverOverridden()
    {
        // The model already said ORDER_STATUS (e.g. "has my order been
        // cancelled?") — the keyword guard must not second-guess a specific,
        // more precise classification.
        Assert.Equal(SupportCategory.OrderStatus,
            SupportTriageRules.EscalateIfObvious("has my order been cancelled?", SupportCategory.OrderStatus));
    }

    [Fact]
    public void EscalateIfObvious_GeneralWithoutKeywords_StaysGeneral()
    {
        Assert.Equal(SupportCategory.General,
            SupportTriageRules.EscalateIfObvious("what are your opening hours?", SupportCategory.General));
    }
}