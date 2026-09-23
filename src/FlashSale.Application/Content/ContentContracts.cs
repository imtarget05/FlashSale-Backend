using FlashSale.Domain.Content;
using System.Text.Json;

namespace FlashSale.Application.Content;

/// <summary>Validated AI content payload (spec §8 output fields).</summary>
public sealed record GeneratedContent(
    string ShortDescription,
    string SeoDescription,
    IReadOnlyList<string> Keywords,
    string SocialCaption,
    IReadOnlyList<GeneratedFaq> Faq);

public sealed record GeneratedFaq(string Question, string Answer);

public enum ContentGenerationOutcome
{
    Ok,
    ProductNotFound,
    RateLimited,
    UpstreamUnavailable,
    InvalidModelOutput
}

/// <summary>
/// Approval state machine (spec §8 + §13 human approval). Pure and unit-tested
/// because these are the rules that make "AI must never auto-publish" true:
/// Published is reachable ONLY from Approved, and Approved ONLY via a human
/// review call.
/// </summary>
public static class ContentApprovalStateMachine
{
    /// <summary>A human reviewer can approve only what is awaiting review.</summary>
    public static bool CanApprove(ProductContentStatus status) => status == ProductContentStatus.ReviewRequired;

    /// <summary>Rejection is possible while awaiting review or after approval.</summary>
    public static bool CanReject(ProductContentStatus status) =>
        status is ProductContentStatus.ReviewRequired or ProductContentStatus.Approved;

    /// <summary>Publishing requires a prior human approval — never the AI alone.</summary>
    public static bool CanPublish(ProductContentStatus status) => status == ProductContentStatus.Approved;

    /// <summary>Regeneration is allowed until content is published.</summary>
    public static bool CanRegenerate(ProductContentStatus status) =>
        status is ProductContentStatus.Draft or ProductContentStatus.AiGenerated
               or ProductContentStatus.ReviewRequired or ProductContentStatus.Rejected;

    /// <summary>True once the AI draft exists but no human has decided yet.</summary>
    public static bool RequiresHumanReview(ProductContentStatus status) =>
        status is ProductContentStatus.AiGenerated or ProductContentStatus.ReviewRequired;
}

/// <summary>
/// Parses + VALIDATES the model's content JSON (spec §8). One object with five
/// required fields; anything missing/empty means the draft is unusable and the
/// caller reports InvalidModelOutput instead of persisting half-content.
/// </summary>
public static class ProductContentParser
{
    public const int MinKeywords = 3;
    public const int MinFaqEntries = 2;

    public static bool TryParse(
        string raw,
        out GeneratedContent content,
        out string? reason)
    {
        content = null!;
        reason = null;

        if (string.IsNullOrWhiteSpace(raw)) { reason = "empty response"; return false; }

        var json = StripCodeFence(raw.Trim());
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { reason = "not JSON"; return false; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { reason = "root not object"; return false; }

            if (!TryString(doc.RootElement, "shortDescription", out var shortDescription)) { reason = "shortDescription missing"; return false; }
            if (!TryString(doc.RootElement, "seoDescription", out var seoDescription)) { reason = "seoDescription missing"; return false; }
            if (!TryString(doc.RootElement, "socialCaption", out var socialCaption)) { reason = "socialCaption missing"; return false; }

            if (!TryArray(doc.RootElement, "keywords", out var keywordsEl)) { reason = "keywords missing"; return false; }
            var keywords = new List<string>();
            foreach (var k in keywordsEl.EnumerateArray())
                if (k.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(k.GetString()))
                    keywords.Add(k.GetString()!.Trim());
            if (keywords.Count < MinKeywords) { reason = $"needs >= {MinKeywords} keywords"; return false; }

            if (!TryArray(doc.RootElement, "faq", out var faqEl)) { reason = "faq missing"; return false; }
            var faq = new List<GeneratedFaq>();
            foreach (var item in faqEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!TryString(item, "question", out var q) || !TryString(item, "answer", out var a)) continue;
                faq.Add(new GeneratedFaq(q, a));
            }
            if (faq.Count < MinFaqEntries) { reason = $"needs >= {MinFaqEntries} faq entries"; return false; }

            content = new GeneratedContent(shortDescription, seoDescription, keywords, socialCaption, faq);
            return true;
        }
    }

    private static bool TryString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryArray(JsonElement obj, string name, out JsonElement array)
    {
        array = default;
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return false;
        array = el;
        return true;
    }

    private static string StripCodeFence(string raw)
    {
        if (!raw.StartsWith("```", StringComparison.Ordinal)) return raw;
        var firstBreak = raw.IndexOf('\n');
        if (firstBreak < 0) return raw;
        var end = raw.LastIndexOf("```", StringComparison.Ordinal);
        return (end > firstBreak ? raw[(firstBreak + 1)..end] : raw[(firstBreak + 1)..]).Trim();
    }
}