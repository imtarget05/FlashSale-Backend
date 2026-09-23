using FlashSale.Application.Persistence;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Content;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Content;

/// <summary>
/// The HUMAN side of spec §8/§13: review queue, approve, reject, publish.
/// Every transition is checked against <see cref="ContentApprovalStateMachine"/>
/// and then enforced again by a guarded UPDATE in the repository, so an
/// out-of-order or duplicated request can never publish unreviewed content.
/// </summary>
public sealed class ContentReviewUseCase(
    IContentRepository content,
    IAutomationRunRepository runs,
    ILogger<ContentReviewUseCase> logger)
{
    public Task<IReadOnlyList<ProductContentDraft>> ReviewQueueAsync(CancellationToken ct = default) =>
        content.ListReviewQueueAsync(ProductContentUseCase.MaxReviewQueue, ct);

    public async Task<ContentReviewResult> ApproveAsync(
        Guid reviewerId, int draftId, CancellationToken ct = default)
    {
        var draft = await content.GetAsync(draftId, ct);
        if (draft is null) return new ContentReviewResult(Found: false, Transitioned: false, ProductContentStatus.Draft);
        if (!ContentApprovalStateMachine.CanApprove(draft.Status))
            return new ContentReviewResult(true, false, draft.Status);

        var transitioned = await content.ApproveAsync(draftId, reviewerId, ct);
        await AuditAsync("approve", draftId, transitioned, $"reviewer={reviewerId}", ct);
        if (transitioned)
            logger.LogInformation("Content draft {DraftId} APPROVED by {ReviewerId} — publishing still requires a separate call.", draftId, reviewerId);

        return new ContentReviewResult(true, transitioned,
            transitioned ? ProductContentStatus.Approved : draft.Status);
    }

    public async Task<ContentReviewResult> RejectAsync(
        Guid reviewerId, int draftId, string reason, CancellationToken ct = default)
    {
        var draft = await content.GetAsync(draftId, ct);
        if (draft is null) return new ContentReviewResult(false, false, ProductContentStatus.Draft);
        if (!ContentApprovalStateMachine.CanReject(draft.Status))
            return new ContentReviewResult(true, false, draft.Status);

        var transitioned = await content.RejectAsync(draftId, reviewerId, reason, ct);
        await AuditAsync("reject", draftId, transitioned, $"reviewer={reviewerId} reason={reason}", ct);
        if (transitioned)
            logger.LogInformation("Content draft {DraftId} REJECTED by {ReviewerId}: {Reason}.", draftId, reviewerId, reason);

        return new ContentReviewResult(true, transitioned,
            transitioned ? ProductContentStatus.Rejected : draft.Status);
    }

    public async Task<ContentReviewResult> PublishAsync(int draftId, CancellationToken ct = default)
    {
        var draft = await content.GetAsync(draftId, ct);
        if (draft is null) return new ContentReviewResult(false, false, ProductContentStatus.Draft);
        if (!ContentApprovalStateMachine.CanPublish(draft.Status))
            return new ContentReviewResult(true, false, draft.Status);

        var transitioned = await content.PublishAsync(draftId, ct);
        await AuditAsync("publish", draftId, transitioned, $"product={draft.ProductId}", ct);
        if (transitioned)
            logger.LogInformation(
                "Content draft {DraftId} PUBLISHED to product {ProductId} (human-approved path only).",
                draftId, draft.ProductId);

        return new ContentReviewResult(true, transitioned,
            transitioned ? ProductContentStatus.Published : draft.Status);
    }

    private async Task AuditAsync(string action, int draftId, bool transitioned, string detail, CancellationToken ct)
    {
        var run = new AutomationRun
        {
            WorkflowName = AutomationWorkflow.AiContentGeneration.ToString(),
            TriggerType = "review",
            TriggerId = $"draft:{draftId}",
            Status = transitioned ? AutomationRunStatus.Success : AutomationRunStatus.ManualReview,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid(),
            ResultSummary = transitioned ? $"{action} ok {detail}" : $"{action} refused (state guard) {detail}"
        };
        await runs.CreateAsync(run, ct);
    }
}