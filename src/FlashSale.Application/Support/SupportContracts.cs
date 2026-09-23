namespace FlashSale.Application.Support;

/// <summary>Support triage categories (spec §9).</summary>
public enum SupportCategory
{
    OrderStatus,
    Payment,
    Cancellation,
    Refund,
    Shipping,
    General
}

public enum SupportTriageOutcome
{
    Ok,
    InvalidMessage,
    RateLimited,
    UpstreamUnavailable,
    InvalidModelOutput
}

public sealed record SupportTriageResult(
    SupportTriageOutcome Outcome,
    SupportCategory Category,
    string? DraftResponse,
    bool RequiresHumanReview,
    string? GroundedFacts,
    string Model,
    long LatencyMs,
    long PromptTokens,
    long CompletionTokens);

/// <summary>
/// Deterministic triage rules (spec §9 + §13 human approval). Category parsing
/// is defensive: an unknown category from the model becomes GENERAL rather than
/// guessing, and sensitive categories always demand a human.
/// </summary>
public static class SupportTriageRules
{
    public static SupportCategory ParseCategory(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().Replace("_", string.Empty).Replace(" ", string.Empty);
        return normalized.ToLowerInvariant() switch
        {
            "orderstatus" => SupportCategory.OrderStatus,
            "payment" => SupportCategory.Payment,
            "cancellation" => SupportCategory.Cancellation,
            "refund" => SupportCategory.Refund,
            "shipping" => SupportCategory.Shipping,
            "general" => SupportCategory.General,
            _ => SupportCategory.General // never invent a category
        };
    }

    /// <summary>
    /// Model classification is probabilistic (qwen3:4b sometimes answers
    /// GENERAL for a clear refund request). Transparent keyword escalation for
    /// sensitive intents (spec §10 philosophy: deterministic rules, not model
    /// confidence): when the model said GENERAL but the message plainly asks
    /// about money/commitments, force the sensitive category so human review
    /// can never be skipped. A specific model classification is never
    /// downgraded — only the uncertain GENERAL bucket is escalated.
    /// </summary>
    public static SupportCategory EscalateIfObvious(string message, SupportCategory modelCategory)
    {
        if (modelCategory != SupportCategory.General)
            return modelCategory;

        var text = message ?? string.Empty;
        if (text.Contains("refund", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("money back", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("chargeback", StringComparison.OrdinalIgnoreCase))
            return SupportCategory.Refund;

        if (text.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            return SupportCategory.Cancellation;

        return modelCategory;
    }

    /// <summary>
    /// Refund and cancellation touch money/commitments, so a draft is never
    /// sent autonomously — spec §13 requires human approval for risky actions.
    /// </summary>
    public static bool IsSensitive(SupportCategory category) =>
        category is SupportCategory.Refund or SupportCategory.Cancellation;

    /// <summary>
    /// Order-related questions are only answered from real order data: when the
    /// caller gave no order key (or the key is unknown) the draft cannot be
    /// grounded, so it needs human review regardless of the model's confidence.
    /// </summary>
    public static bool NeedsHumanReview(SupportCategory category, bool hasGroundingFacts) =>
        IsSensitive(category) || (!hasGroundingFacts && category != SupportCategory.General);
}

/// <summary>Parses the triage JSON: {"category": string, "draftResponse": string}.</summary>
public static class SupportTriageParser
{
    public static bool TryParse(string raw, out SupportCategory category, out string draft)
    {
        category = SupportCategory.General;
        draft = string.Empty;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        var json = raw.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = json.IndexOf('\n');
            if (firstBreak >= 0)
            {
                var end = json.LastIndexOf("```", StringComparison.Ordinal);
                json = (end > firstBreak ? json[(firstBreak + 1)..end] : json[(firstBreak + 1)..]).Trim();
            }
        }

        System.Text.Json.JsonDocument doc;
        try { doc = System.Text.Json.JsonDocument.Parse(json); }
        catch (System.Text.Json.JsonException) { return false; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;

            string? categoryRaw = null;
            if (doc.RootElement.TryGetProperty("category", out var c) &&
                c.ValueKind == System.Text.Json.JsonValueKind.String)
                categoryRaw = c.GetString();

            if (!doc.RootElement.TryGetProperty("draftResponse", out var d) ||
                d.ValueKind != System.Text.Json.JsonValueKind.String ||
                string.IsNullOrWhiteSpace(d.GetString()))
                return false;

            category = SupportTriageRules.ParseCategory(categoryRaw);
            draft = d.GetString()!.Trim();
            return true;
        }
    }
}