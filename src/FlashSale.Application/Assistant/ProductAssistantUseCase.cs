using FlashSale.Application.Persistence;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace FlashSale.Application.Assistant;

/// <summary>
/// Use case: grounded product assistant (spec §10).
///
/// Flow: validate question → per-user rate limit → load REAL product candidates
/// → grounded prompt → local LLM (bounded retry + per-attempt timeout) →
/// validate structured output (never invent a productId) → log model, latency
/// and token usage. Depends only on Application ports.
/// </summary>
public sealed class ProductAssistantUseCase(
    IOrderReadModel readModel,
    IAiChatClient chatClient,
    IAiRateLimiter rateLimiter,
    ILogger<ProductAssistantUseCase> logger)
{
    public const int MaxQuestionLength = 500;
    public const int CandidateLimit = 20;

    /// <summary>Bounded retry (spec §10: "retry có giới hạn").</summary>
    public const int MaxAttempts = 2;

    /// <summary>
    /// Per-attempt ceiling. Local qwen3:4b REASONS before answering (measured
    /// 16–50s per completion via /v1 — think/reasoning_effort params are not
    /// honored by Ollama's OpenAI endpoint), so 30s and 60s both produced false
    /// UpstreamUnavailable. 120s keeps a hard timeout with real headroom.
    /// <c>init</c> so unit tests can shrink it.
    /// </summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public async Task<AssistantResult> ExecuteAsync(Guid userId, string? question, CancellationToken ct = default)
    {
        var trimmed = question?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxQuestionLength)
            return new AssistantResult(AssistantOutcome.InvalidQuestion, null, [], "n/a", 0, 0, 0);

        // Rate limit BEFORE touching the database or the model (cheapest gate first).
        if (!await rateLimiter.TryAcquireAsync(userId, ct))
            return new AssistantResult(AssistantOutcome.RateLimited, null, [], "n/a", 0, 0, 0);

        var candidates = await readModel.GetProductCandidatesAsync(CandidateLimit, ct);
        if (candidates.Count == 0)
        {
            // Nothing to ground on: answer from the application, never the model.
            return new AssistantResult(
                AssistantOutcome.Ok, "No products are currently on sale.", [], "none", 0, 0, 0);
        }

        var validIds = candidates.Select(c => c.Id).ToHashSet();
        var (systemPrompt, userPrompt) = BuildPrompts(trimmed, candidates);

        var stopwatch = Stopwatch.StartNew();
        AiChatResponse? lastResponse = null;
        Exception? lastError = null;
        string? answer = null;
        IReadOnlyList<RecommendedProduct>? products = null;
        var droppedTotal = 0;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(AttemptTimeout);
            try
            {
                lastResponse = await chatClient.CompleteAsync(
                    [new ChatTurn("system", systemPrompt), new ChatTurn("user", userPrompt)],
                    timeout.Token);

                // Validation is part of the bounded retry: a response the guard
                // rejects is transient misbehaviour too (spec §10 retry rule).
                if (AssistantOutputParser.TryParse(
                        lastResponse.Content, validIds,
                        out var attemptAnswer, out var attemptProducts, out var attemptDropped))
                {
                    answer = attemptAnswer;
                    products = attemptProducts;
                    droppedTotal = attemptDropped;
                    break;
                }

                lastError = new InvalidOperationException("model output failed grounding validation");
                logger.LogWarning(
                    "AI assistant attempt {Attempt} produced unparseable output.", attempt);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-attempt timeout — retry within the bounded budget.
                lastError = new TimeoutException($"LLM attempt {attempt} exceeded {AttemptTimeout}.");
                logger.LogWarning(
                    "AI assistant attempt {Attempt} timed out after {Timeout}.", attempt, AttemptTimeout);
            }
            catch (OperationCanceledException)
            {
                throw; // the CALLER cancelled — surface it, do not mask as unavailability
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "AI assistant attempt {Attempt} failed.", attempt);
            }
        }

        stopwatch.Stop();

        if (lastResponse is null)
        {
            logger.LogWarning(
                "AI assistant unavailable after {Attempts} attempt(s): {Error}.",
                MaxAttempts, lastError?.Message);
            return new AssistantResult(
                AssistantOutcome.UpstreamUnavailable, null, [], "n/a", stopwatch.ElapsedMilliseconds, 0, 0);
        }

        if (answer is null || products is null)
        {
            // The model ANSWERED on every attempt but never in a validatable shape.
            logger.LogWarning(
                "AI assistant returned unparseable output (model {Model}, latency {LatencyMs}ms).",
                lastResponse.Model, stopwatch.ElapsedMilliseconds);
            return new AssistantResult(
                AssistantOutcome.InvalidModelOutput, null, [], lastResponse.Model,
                stopwatch.ElapsedMilliseconds, lastResponse.PromptTokens, lastResponse.CompletionTokens);
        }

        // Spec §10: log model, latency, token usage — on every successful call.
        logger.LogInformation(
            "AI assistant ok model={Model} latency={LatencyMs}ms tokens(prompt={PromptTokens}, completion={CompletionTokens}) droppedRecommendations={Dropped}.",
            lastResponse.Model, stopwatch.ElapsedMilliseconds,
            lastResponse.PromptTokens, lastResponse.CompletionTokens, droppedTotal);

        if (droppedTotal > 0)
            logger.LogWarning(
                "Grounding guard dropped {Dropped} recommendation(s) not present in the candidate set.",
                droppedTotal);

        return new AssistantResult(
            AssistantOutcome.Ok, answer, products, lastResponse.Model,
            stopwatch.ElapsedMilliseconds, lastResponse.PromptTokens, lastResponse.CompletionTokens);
    }

    /// <summary>
    /// System prompt pins the JSON schema and the grounding rule; the user turn
    /// carries ONLY real product rows (id/name/category/description/price/stock).
    /// </summary>
    private static (string System, string User) BuildPrompts(
        string question, IReadOnlyList<ProductCandidate> candidates)
    {
        var system = new StringBuilder()
            .Append("You are a product assistant for a flash-sale store. ")
            .Append("Recommend ONLY from the candidate list below; NEVER invent a product id. ")
            .Append("Reply with ONLY a JSON object, no prose and no markdown fences, in this exact shape: ")
            .Append("{\"answer\": string, \"recommendedProducts\": [{\"productId\": int, \"reason\": string}]}. ")
            .Append("recommendedProducts must be an ARRAY OF OBJECTS with integer productId, never strings.")
            .ToString();

        var sb = new StringBuilder("Candidates:\n");
        foreach (var c in candidates)
        {
            sb.Append("- id=").Append(c.Id)
              .Append(" | ").Append(c.Name)
              .Append(" | ").Append(c.Category)
              .Append(" | ").Append(c.Description)
              .Append(" | price=").Append(c.FlashSalePrice)
              .Append(" | stock=").Append(c.AvailableStock)
              .Append('\n');
        }
        sb.Append('\n').Append("Question: ").Append(question);

        return (system, sb.ToString());
    }
}
