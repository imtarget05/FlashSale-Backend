using System.Text.Json;

namespace FlashSale.Application.Assistant;

/// <summary>Product row fed to the model as grounding context (spec §10).</summary>
public sealed record ProductCandidate(
    int Id,
    string Name,
    string Category,
    string Description,
    decimal FlashSalePrice,
    int AvailableStock);

/// <summary>One model recommendation AFTER validation against the real candidate set.</summary>
public sealed record RecommendedProduct(int ProductId, string Reason);

/// <summary>Outcome of an assistant request (spec §10 rules → one value per failure mode).</summary>
public enum AssistantOutcome
{
    /// <summary>Validated grounded answer (possibly with zero recommendations).</summary>
    Ok,
    /// <summary>Question missing or longer than <see cref="ProductAssistantUseCase.MaxQuestionLength"/>.</summary>
    InvalidQuestion,
    /// <summary>The per-user rate limit refused this call before any model work.</summary>
    RateLimited,
    /// <summary>The local LLM was unreachable or timed out on every allowed attempt.</summary>
    UpstreamUnavailable,
    /// <summary>The model answered but not in a shape we can validate.</summary>
    InvalidModelOutput
}

/// <summary>
/// Result of <see cref="ProductAssistantUseCase.ExecuteAsync"/>. Latency/token
/// figures are populated whenever the model was actually invoked (spec §10:
/// "log model, latency, token usage").
/// </summary>
public sealed record AssistantResult(
    AssistantOutcome Outcome,
    string? Answer,
    IReadOnlyList<RecommendedProduct> RecommendedProducts,
    string Model,
    long LatencyMs,
    long PromptTokens,
    long CompletionTokens);

/// <summary>
/// Parses and VALIDATES the model's structured output. Spec §10 rules:
/// the model must never invent a productId — anything not present in the
/// candidate set is dropped (and counted, so the use case can log it).
/// </summary>
public static class AssistantOutputParser
{
    public static bool TryParse(
        string raw,
        ISet<int> validProductIds,
        out string answer,
        out IReadOnlyList<RecommendedProduct> products,
        out int droppedRecommendations)
    {
        answer = string.Empty;
        products = [];
        droppedRecommendations = 0;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        var json = StripCodeFence(raw.Trim());

        // Element-wise tolerant parse: local models sometimes emit sloppy array
        // entries (strings, missing ids). One bad ENTRY must not fail the whole
        // answer — malformed entries are DROPPED and counted, never invented.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false; // not JSON at all → InvalidModelOutput
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("Answer", out var answerEl) &&
                !root.TryGetProperty("answer", out answerEl)) return false;
            if (answerEl.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(answerEl.GetString())) return false;

            answer = answerEl.GetString()!.Trim();

            var seen = new HashSet<int>();
            var kept = new List<RecommendedProduct>();

            if (root.TryGetProperty("recommendedProducts", out var listEl) ||
                root.TryGetProperty("RecommendedProducts", out listEl))
            {
                if (listEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in listEl.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object ||
                            !item.TryGetProperty("productId", out var idEl) &&
                            !item.TryGetProperty("ProductId", out idEl) ||
                            idEl.ValueKind != JsonValueKind.Number ||
                            !idEl.TryGetInt32(out var id) ||
                            !validProductIds.Contains(id))
                        {
                            droppedRecommendations++; // malformed or hallucinated — never invented
                            continue;
                        }
                        if (!seen.Add(id)) continue; // duplicate — keep the first reason

                        var reason = "";
                        if ((item.TryGetProperty("reason", out var rEl) ||
                             item.TryGetProperty("Reason", out rEl)) &&
                            rEl.ValueKind == JsonValueKind.String)
                            reason = rEl.GetString() ?? "";

                        kept.Add(new RecommendedProduct(id, reason.Trim()));
                    }
                }
                else
                {
                    droppedRecommendations++; // wrong type entirely (e.g. a bare string)
                }
            }

            products = kept;
            return true;
        }
    }

    /// <summary>
    /// Local models wrap JSON in ```json fences despite instructions. Strip a
    /// single outer fence; pass plain JSON through untouched.
    /// </summary>
    private static string StripCodeFence(string raw)
    {
        if (!raw.StartsWith("```", StringComparison.Ordinal)) return raw;

        var firstBreak = raw.IndexOf('\n');
        if (firstBreak < 0) return raw;

        var end = raw.LastIndexOf("```", StringComparison.Ordinal);
        var body = end > firstBreak ? raw[(firstBreak + 1)..end] : raw[(firstBreak + 1)..];
        return body.Trim();
    }
}
