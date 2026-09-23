using FlashSale.Application.Assistant;
using FlashSale.Application.Content;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Content;
using FlashSale.Infrastructure.Events;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.IntegrationTests;

/// <summary>Spec §8/§13/§17-D: draft → review → publish through guarded transitions.</summary>
[Collection("flashsale")]
public sealed class ContentFlowTests(FlashSaleFixture fx)
{
    private const string ContentJson = """
        {"shortDescription":"Great phone for daily use.","seoDescription":"Flagship smartphone with advanced camera.","keywords":["phone","smartphone","flagship"],"socialCaption":"Get the new Phone X!","faq":[{"question":"Warranty?","answer":"1 year."},{"question":"Screen size?","answer":"6.7 inch."}]}
        """;

    private static readonly Guid Staff = Guid.NewGuid();

    private static (ProductContentUseCase Gen, ContentReviewUseCase Review, FakeAiChatClient Ai, AppDbContext Db)
        Create(FlashSaleFixture fx, string modelJson = ContentJson)
    {
        var db = fx.CreateDbContext();
        var ai = new FakeAiChatClient { Content = modelJson };
        var readModel = new OrderReadModel(db);
        var limiter = new RedisNoOpRateLimiter();
        var runs = new AutomationRunRepository(db);
        var drafts = new ContentRepository(db);
        var gen = new ProductContentUseCase(readModel, ai, limiter, drafts, runs,
            NullLogger<ProductContentUseCase>.Instance);
        var review = new ContentReviewUseCase(drafts, runs, NullLogger<ContentReviewUseCase>.Instance);
        return (gen, review, ai, db);
    }

    private sealed class RedisNoOpRateLimiter : IAiRateLimiter
    {
        public Task<bool> TryAcquireAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    [Fact]
    public async Task Generate_CreatesDraftInReviewRequired()
    {
        var productId = await fx.ResetDatabaseAsync(stock: 10);
        var (gen, _, ai, db) = Create(fx);
        await using var _ = db;

        var result = await gen.GenerateAsync(Staff, productId, CancellationToken.None);

        Assert.Equal(ContentGenerationOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Draft);
        Assert.Equal(ProductContentStatus.ReviewRequired, result.Draft!.Status);
        Assert.Equal(1, ai.Calls);                          // single model call
        Assert.Equal("Great phone for daily use.", result.Draft.ShortDescription);

        var run = await db.AutomationRuns
            .Where(r => r.WorkflowName == AutomationWorkflow.AiContentGeneration.ToString()
                     && r.TriggerType == "api")
            .OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(AutomationRunStatus.Success, run.Status);
        Assert.Contains("status=ReviewRequired", run.ResultSummary);
    }

    [Fact]
    public async Task Publish_BeforeApproval_IsRefused()
    {
        var productId = await fx.ResetDatabaseAsync(stock: 10);
        var (gen, review, _, db) = Create(fx);
        await using var _ = db;

        var generated = await gen.GenerateAsync(Staff, productId, CancellationToken.None);
        var draft = generated.Draft!;

        var publish = await review.PublishAsync(draft.Id, CancellationToken.None);

        Assert.False(publish.Transitioned);                 // guard holds: not approved
        Assert.Equal(ProductContentStatus.ReviewRequired, publish.Status);

        var row = await db.ProductContentDrafts.SingleAsync(d => d.Id == draft.Id);
        Assert.Equal(ProductContentStatus.ReviewRequired, row.Status);
    }

    [Fact]
    public async Task FullLifecycle_Generate_Approve_Publish_AppliesToProduct()
    {
        var productId = await fx.ResetDatabaseAsync(stock: 10);
        var (gen, review, _, db) = Create(fx);
        await using var _ = db;

        var generated = await gen.GenerateAsync(Staff, productId, CancellationToken.None);
        var draft = generated.Draft!;

        var approve = await review.ApproveAsync(Staff, draft.Id, CancellationToken.None);
        Assert.True(approve.Transitioned);
        Assert.Equal(ProductContentStatus.Approved, approve.Status);

        // Double approve is a no-op (exactly-once review):
        var reapprove = await review.ApproveAsync(Staff, draft.Id, CancellationToken.None);
        Assert.False(reapprove.Transitioned);

        var publish = await review.PublishAsync(draft.Id, CancellationToken.None);
        Assert.True(publish.Transitioned);
        Assert.Equal(ProductContentStatus.Published, publish.Status);

        // The approved copy is now the product description:
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        Assert.Equal("Great phone for daily use.", product.Description);

        // Review queue is empty afterwards:
        var queue = await review.ReviewQueueAsync(CancellationToken.None);
        Assert.Empty(queue);
    }

    [Fact]
    public async Task Reject_WithReason_ThenRegenerate()
    {
        var productId = await fx.ResetDatabaseAsync(stock: 10);
        var (gen, review, _, db) = Create(fx);
        await using var _ = db;

        var generated = await gen.GenerateAsync(Staff, productId, CancellationToken.None);
        var rejected = await review.RejectAsync(Staff, generated.Draft!.Id, "tone is off", CancellationToken.None);

        Assert.True(rejected.Transitioned);
        Assert.Equal(ProductContentStatus.Rejected, rejected.Status);

        // Verify with a FRESH context: the raw-SQL guarded update bypasses EF's
        // change tracker, so the shared test context still holds the stale row.
        // (Production is unaffected — every request gets its own DbContext.)
        await using var verify = fx.CreateDbContext();
        var row = await verify.ProductContentDrafts.SingleAsync(d => d.Id == generated.Draft.Id);
        Assert.Equal("tone is off", row.RejectionReason);
    }

    [Fact]
    public async Task Generate_UnknownProduct_ReturnsNotFound_WithoutModelCall()
    {
        await fx.ResetDatabaseAsync(stock: 10);
        var (gen, _, ai, db) = Create(fx);
        await using var _ = db;

        var result = await gen.GenerateAsync(Staff, productId: 999_999, CancellationToken.None);

        Assert.Equal(ContentGenerationOutcome.ProductNotFound, result.Outcome);
        Assert.Equal(0, ai.Calls);
    }
}