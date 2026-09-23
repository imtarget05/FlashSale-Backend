using FlashSale.Application.Assistant;
using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Content;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FlashSale.Application.Content;

public sealed record GenerateContentResult(
    ContentGenerationOutcome Outcome,
    ProductContentDraft? Draft,
    string Model,
    long LatencyMs,
    long PromptTokens,
    long CompletionTokens);

public sealed record ContentReviewResult(bool Found, bool Transitioned, ProductContentStatus Status);

/// <summary>
/// AI product content generation (spec §8): ground the prompt in REAL product
/// facts, validate the structured output, persist a draft — and ALWAYS stop at
/// ReviewRequired. Publishing is a separate human action (spec §13), so this
/// class has no path to Published at all.
/// </summary>
public sealed class ProductContentUseCase(
    IOrderReadModel readModel,
    IAiChatClient chatClient,
    IAiRateLimiter rateLimiter,
    IContentRepository content,
    IAutomationRunRepository runs,
    ILogger<ProductContentUseCase> logger)
{
    public const int MaxAttempts = 2;
    public const int MaxReviewQueue = 100;

    /// <summary>Per-attempt ceiling (local reasoning model — same as assistant).</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public async Task<GenerateContentResult> GenerateAsync(
        Guid userId, int productId, CancellationToken ct = default)
    {
        var product = await readModel.GetProductFactsAsync(productId, ct);
        if (product is null)
            return new GenerateContentResult(ContentGenerationOutcome.ProductNotFound, null, "n/a", 0, 0, 0);

        if (!await rateLimiter.TryAcquireAsync(userId, ct))
            return new GenerateContentResult(ContentGenerationOutcome.RateLimited, null, "n/a", 0, 0, 0);

        var run = new AutomationRun
        {
            WorkflowName = AutomationWorkflow.AiContentGeneration.ToString(),
            TriggerType = "api",
            TriggerId = $"product:{productId}",
            Status = AutomationRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid()
        };
        await runs.CreateAsync(run, ct);

        var (systemPrompt, userPrompt) = BuildPrompt(product);
        var stopwatch = Stopwatch.StartNew();
        AiChatResponse? response = null;
        GeneratedContent? generated = null;
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

                if (ProductContentParser.TryParse(response.Content, out var parsed, out var reason))
                {
                    generated = parsed;
                    lastError = null;
                    break;
                }

                lastError = new InvalidOperationException($"model output failed validation: {reason}");
                logger.LogWarning(
                    "AI content attempt {Attempt} rejected for product {ProductId}: {Reason}.",
                    attempt, productId, reason);
                response = null; // no usable payload from this attempt
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException($"content attempt {attempt} exceeded {AttemptTimeout}.");
                logger.LogWarning("AI content attempt {Attempt} timed out.", attempt);
            }
            catch (OperationCanceledException)
            {
                throw; // caller cancelled
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "AI content attempt {Attempt} failed.", attempt);
            }
        }

        stopwatch.Stop();

        if (generated is null)
        {
            var message = lastError?.Message ?? "no valid content";
            run.Status = AutomationRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorCode = lastError?.GetType().Name ?? "InvalidModelOutput";
            run.ErrorMessage = message.Length <= 1000 ? message : message[..1000];
            await runs.UpdateAsync(run, ct);

            var outcome = lastError is InvalidOperationException
                ? ContentGenerationOutcome.InvalidModelOutput
                : ContentGenerationOutcome.UpstreamUnavailable;
            logger.LogWarning(
                "Content generation for product {ProductId} failed: {Error}.", productId, run.ErrorMessage);
            return new GenerateContentResult(outcome, null, "n/a", stopwatch.ElapsedMilliseconds, 0, 0);
        }

        // Persist the AI result, then move it through the mandatory review gate.
        var draft = await content.CreateAsync(new ProductContentDraft
        {
            ProductId = productId,
            Status = ProductContentStatus.AiGenerated,
            ShortDescription = generated.ShortDescription,
            SeoDescription = generated.SeoDescription,
            KeywordsJson = JsonSerializer.Serialize(generated.Keywords),
            SocialCaption = generated.SocialCaption,
            FaqJson = JsonSerializer.Serialize(generated.Faq),
            Model = response!.Model,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens,
            CreatedAt = DateTimeOffset.UtcNow,
            CorrelationId = run.CorrelationId
        }, ct);

        await content.MarkReviewRequiredAsync(draft.Id, ct);
        draft.Status = ProductContentStatus.ReviewRequired;

        run.Status = AutomationRunStatus.Success;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.ResultSummary =
            $"draft={draft.Id} product={productId} model={response.Model} latencyMs={stopwatch.ElapsedMilliseconds} tokens={response.PromptTokens}/{response.CompletionTokens} status=ReviewRequired";

        logger.LogInformation(
            "AI content draft {DraftId} for product {ProductId} generated (model {Model}, latency {LatencyMs}ms) — awaiting human review.",
            draft.Id, productId, response.Model, stopwatch.ElapsedMilliseconds);

        await runs.UpdateAsync(run, ct);
        return new GenerateContentResult(
            ContentGenerationOutcome.Ok, draft, response.Model,
            stopwatch.ElapsedMilliseconds, response.PromptTokens, response.CompletionTokens);
    }

    private static (string System, string User) BuildPrompt(ProductFactsView product)
    {
        var system = new StringBuilder()
            .Append("You write product content for a flash-sale store. ")
            .Append("Use ONLY the product facts provided — never invent specifications. ")
            .Append("Reply with ONLY a JSON object, no prose and no markdown fences: ")
            .Append("{\"shortDescription\": string, \"seoDescription\": string, ")
            .Append("\"keywords\": [string], \"socialCaption\": string, ")
            .Append("\"faq\": [{\"question\": string, \"answer\": string}]}. ")
            .Append($"Provide at least {ProductContentParser.MinKeywords} keywords and {ProductContentParser.MinFaqEntries} FAQ entries.")
            .ToString();

        var user = new StringBuilder()
            .Append("Product facts:\n")
            .Append("- name: ").Append(product.Name).Append('\n')
            .Append("- category: ").Append(product.Category).Append('\n')
            .Append("- description: ").Append(product.Description).Append('\n')
            .Append("- flash sale price: ").Append(product.FlashSalePrice).Append('\n')
            .Append("- available stock: ").Append(product.AvailableStock).Append('\n')
            .ToString();

        return (system, user);
    }
}