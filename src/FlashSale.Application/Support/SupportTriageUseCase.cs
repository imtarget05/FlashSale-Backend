using FlashSale.Application.Assistant;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace FlashSale.Application.Support;

/// <summary>
/// Customer support triage (spec §9): classify intent → retrieve the REAL order
/// facts → produce a grounded draft → mark sensitive cases for human review.
///
/// Grounding rule: the authoritative status text is fetched from PostgreSQL and
/// handed to the model inside the prompt; when no facts exist the draft must ask
/// for the order key instead of guessing, and the result is flagged for human
/// review. The model can therefore never invent an order status that reaches a
/// customer unsupervised.
/// </summary>
public sealed class SupportTriageUseCase(
    IPaymentRepository payments,
    IAiChatClient chatClient,
    IAiRateLimiter rateLimiter,
    IAutomationRunRepository runs,
    ILogger<SupportTriageUseCase> logger)
{
    public const int MaxMessageLength = 1000;
    public const int MaxAttempts = 2;

    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public async Task<SupportTriageResult> ExecuteAsync(
        Guid userId, string? message, string? orderKey, CancellationToken ct = default)
    {
        var text = message?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxMessageLength)
            return new SupportTriageResult(SupportTriageOutcome.InvalidMessage,
                SupportCategory.General, null, true, null, "n/a", 0, 0, 0);

        if (!await rateLimiter.TryAcquireAsync(userId, ct))
            return new SupportTriageResult(SupportTriageOutcome.RateLimited,
                SupportCategory.General, null, true, null, "n/a", 0, 0, 0);

        var run = new AutomationRun
        {
            WorkflowName = AutomationWorkflow.SupportTriage.ToString(),
            TriggerType = "api",
            TriggerId = string.IsNullOrWhiteSpace(orderKey) ? "no-order-key" : $"order:{orderKey.Trim()}",
            Status = AutomationRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid()
        };
        await runs.CreateAsync(run, ct);

        // ---- Grounding: authoritative facts straight from the database (spec §9)
        string? facts = null;
        if (!string.IsNullOrWhiteSpace(orderKey))
        {
            var order = await payments.GetByKeyAsync(orderKey.Trim(), ct);
            if (order is not null)
            {
                facts = $"orderKey={order.IdempotencyKey}; orderId={order.OrderId}; " +
                        $"productId={order.ProductId}; quantity={order.Quantity}; " +
                        $"orderStatus={order.Status}";
            }
        }

        var (systemPrompt, userPrompt) = BuildPrompt(text, facts);
        var stopwatch = Stopwatch.StartNew();
        AiChatResponse? response = null;
        SupportCategory category = SupportCategory.General;
        string? draft = null;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(AttemptTimeout);
            try
            {
                response = await chatClient.CompleteAsync(
                    [new ChatTurn("system", systemPrompt), new ChatTurn("user", userPrompt)],
                    timeout.Token);

                if (SupportTriageParser.TryParse(response.Content, out var parsedCategory, out var parsedDraft))
                {
                    category = parsedCategory;
                    draft = parsedDraft;
                    lastError = null;
                    break;
                }

                lastError = new InvalidOperationException("triage output failed validation");
                logger.LogWarning("Support triage attempt {Attempt} produced unparseable output.", attempt);
                response = null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException($"triage attempt {attempt} exceeded {AttemptTimeout}.");
                logger.LogWarning("Support triage attempt {Attempt} timed out.", attempt);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "Support triage attempt {Attempt} failed.", attempt);
            }
        }

        stopwatch.Stop();
        // Probabilistic classification guard: a plain refund/cancel request the
        // model downgraded to GENERAL is escalated so §13 human review applies.
        category = SupportTriageRules.EscalateIfObvious(text, category);
        var requiresHumanReview = draft is null ||
            SupportTriageRules.NeedsHumanReview(category, hasGroundingFacts: facts is not null);

        if (draft is null)
        {
            var msg = lastError?.Message ?? "no valid triage output";
            run.Status = AutomationRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = lastError?.GetType().Name ?? "InvalidModelOutput";
            run.ErrorMessage = msg.Length <= 1000 ? msg : msg[..1000];
            await runs.UpdateAsync(run, ct);

            var outcome = lastError is InvalidOperationException
                ? SupportTriageOutcome.InvalidModelOutput
                : SupportTriageOutcome.UpstreamUnavailable;
            return new SupportTriageResult(outcome, SupportCategory.General, null, true, facts,
                "n/a", stopwatch.ElapsedMilliseconds, 0, 0);
        }

        run.Status = AutomationRunStatus.Success;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.ResultSummary =
            $"category={category} grounded={(facts is not null ? "yes" : "no")} " +
            $"human_review={requiresHumanReview} model={response!.Model} latencyMs={stopwatch.ElapsedMilliseconds}";

        logger.LogInformation(
            "Support triage: category={Category} grounded={Grounded} humanReview={Review} (model {Model}, {LatencyMs}ms).",
            category, facts is not null, requiresHumanReview, response.Model, stopwatch.ElapsedMilliseconds);

        await runs.UpdateAsync(run, ct);
        return new SupportTriageResult(
            SupportTriageOutcome.Ok, category, draft, requiresHumanReview, facts,
            response.Model, stopwatch.ElapsedMilliseconds, response.PromptTokens, response.CompletionTokens);
    }

    private static (string System, string User) BuildPrompt(string message, string? facts)
    {
        var system = new StringBuilder()
            .Append("You are a support triage assistant. Classify the customer message into exactly one category: ")
            .Append("ORDER_STATUS, PAYMENT, CANCELLATION, REFUND, SHIPPING, GENERAL. ")
            .Append("Then write a SHORT draft reply. Use ONLY the authoritative order facts provided; ")
            .Append("if no facts are provided you MUST NOT state any order status — ask the customer for their order key instead. ")
            .Append("Never promise refunds or cancellations; say a human will review those. ")
            .Append("Reply with ONLY JSON, no prose and no markdown fences: ")
            .Append("{\"category\": string, \"draftResponse\": string}")
            .ToString();

        var user = new StringBuilder()
            .Append("Customer message: ").Append(message).Append('\n');
        if (facts is not null)
            user.Append("Authoritative order facts (from the database): ").Append(facts).Append('\n');
        else
            user.Append("Authoritative order facts: NONE AVAILABLE\n");

        return (system, user.ToString());
    }
}