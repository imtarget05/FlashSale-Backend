using FlashSale.Application.Events;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
using FlashSale.Application.Support;
using FlashSale.Domain.Automation;
using FlashSale.Infrastructure.Events;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.IntegrationTests;

/// <summary>Spec §9/§17 triage grounding: drafts cite real order facts or demand a human.</summary>
[Collection("flashsale")]
public sealed class SupportTriageTests(FlashSaleFixture fx)
{
    private static readonly Guid Customer = Guid.NewGuid();

    private static SupportTriageUseCase Create(AppDbContext db, string modelJson)
    {
        return new SupportTriageUseCase(
            new PaymentRepository(db),
            new FakeAiChatClient
            {
                Content = modelJson
            },
            new CustomerRateLimiter(),
            new AutomationRunRepository(db),
            NullLogger<SupportTriageUseCase>.Instance)
        {
            AttemptTimeout = TimeSpan.FromSeconds(30)
        };
    }

    private sealed class CustomerRateLimiter : Application.Assistant.IAiRateLimiter
    {
        public Task<bool> TryAcquireAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    [Fact]
    public async Task Triage_WithRealOrderKey_IsGrounded_NotHumanRequired()
    {
        var productId = await fx.ResetDatabaseAsync(stock: 10);
        var key = "triage-1";
        await using (var db = fx.CreateDbContext())
        {
            var repo = new OrderRepository(db);
            await new OrderProcessor(repo, NullLogger<OrderProcessor>.Instance)
                // Anonymous order (ADR-013 §6): any Guid would violate FK_Orders_Users.
                .ProcessAsync(new OrderMessage(productId, 1, key, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        await using var db2 = fx.CreateDbContext();
        var useCase = Create(db2,
            """{"category":"ORDER_STATUS","draftResponse":"Your order is being processed."}""");

        var result = await useCase.ExecuteAsync(
            Customer, "Where is my order?", key, CancellationToken.None);

        Assert.Equal(SupportTriageOutcome.Ok, result.Outcome);
        Assert.Equal(SupportCategory.OrderStatus, result.Category);
        Assert.False(result.RequiresHumanReview);            // grounded in real facts
        Assert.NotNull(result.GroundedFacts);
        Assert.Contains("orderKey=triage-1", result.GroundedFacts);
        Assert.Contains("orderStatus=", result.GroundedFacts);
    }

    [Fact]
    public async Task Triage_RefundCategory_AlwaysNeedsHuman()
    {
        await fx.ResetDatabaseAsync(stock: 10);
        await using var db = fx.CreateDbContext();
        var useCase = Create(db,
            """{"category":"REFUND","draftResponse":"Request noted."}""");

        var result = await useCase.ExecuteAsync(
            Customer, "I want a refund", orderKey: null, CancellationToken.None);

        Assert.Equal(SupportCategory.Refund, result.Category);
        Assert.True(result.RequiresHumanReview);             // money => human
    }

    [Fact]
    public async Task Triage_OrderQuestionWithoutFacts_NeedsHuman()
    {
        await fx.ResetDatabaseAsync(stock: 10);
        await using var db = fx.CreateDbContext();
        var useCase = Create(db,
            """{"category":"ORDER_STATUS","draftResponse":"Sure thing, it is shipped!"}""");

        // No real order was referenced, and the model "confidently" claims a
        // status: system must route to a human rather than show the invented fact.
        var result = await useCase.ExecuteAsync(
            Customer, "Where is my order?", orderKey: null, CancellationToken.None);

        Assert.Equal(SupportTriageOutcome.Ok, result.Outcome);
        Assert.True(result.RequiresHumanReview);
        Assert.Null(result.GroundedFacts);
    }

    [Fact]
    public async Task Triage_AuditTrail_Written()
    {
        await fx.ResetDatabaseAsync(stock: 10);
        await using var db = fx.CreateDbContext();
        var useCase = Create(db,
            """{"category":"GENERAL","draftResponse":"Happy to help."}""");

        var result = await useCase.ExecuteAsync(
            Customer, "Hi there", orderKey: null, CancellationToken.None);

        Assert.Equal(SupportTriageOutcome.Ok, result.Outcome);

        var run = await db.AutomationRuns
            .Where(r => r.WorkflowName == AutomationWorkflow.SupportTriage.ToString())
            .OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(AutomationRunStatus.Success, run.Status);
        Assert.Contains("category=General", run.ResultSummary);
    }
}