using FlashSale.Application.Assistant;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests;

/// <summary>
/// Spec §10 rules as executable checks: grounding validation, bounded retry,
/// per-attempt timeout, rate limit before any heavy work, and the
/// "log model, latency, token usage" figures carried in the result.
/// </summary>
public class ProductAssistantUseCaseTests
{
    private static readonly Guid User = Guid.NewGuid();

    private const string ValidJson = """
        {"answer":"The Phone is the best fit.","recommendedProducts":[{"productId":1,"reason":"flagship"}]}
        """;

    [Fact]
    public async Task EmptyQuestion_IsRejectedBeforeRateLimitOrModel()
    {
        var (useCase, rl, chat) = Create();

        var result = await useCase.ExecuteAsync(User, "   ");

        Assert.Equal(AssistantOutcome.InvalidQuestion, result.Outcome);
        Assert.Equal(0, rl.Calls);   // cheapest gate first — no store touch
        Assert.Equal(0, chat.Calls); // no model call
    }

    [Fact]
    public async Task QuestionLongerThanLimit_IsRejected()
    {
        var (useCase, _, chat) = Create();

        var result = await useCase.ExecuteAsync(User, new string('q', 501));

        Assert.Equal(AssistantOutcome.InvalidQuestion, result.Outcome);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task RateLimited_ShortCircuitsBeforeModel()
    {
        var (useCase, rl, chat) = Create(allow: false);

        var result = await useCase.ExecuteAsync(User, "which phone?");

        Assert.Equal(AssistantOutcome.RateLimited, result.Outcome);
        Assert.Equal(1, rl.Calls);
        Assert.Equal(0, chat.Calls);
    }

    [Fact]
    public async Task Ok_FencesStripped_InventedProductIdDropped()
    {
        var (useCase, _, chat) = Create(
            content: "```json\n{\"answer\":\"Phone recommended.\",\"recommendedProducts\":[{\"productId\":1,\"reason\":\"best\"},{\"productId\":999,\"reason\":\"hallucinated\"}]}\n```");

        var result = await useCase.ExecuteAsync(User, "recommend a phone");

        Assert.Equal(AssistantOutcome.Ok, result.Outcome);
        Assert.Equal("Phone recommended.", result.Answer);
        Assert.Single(result.RecommendedProducts); // id 999 was dropped by the grounding guard
        Assert.Equal(1, result.RecommendedProducts[0].ProductId);
        Assert.Equal("qwen3:4b", result.Model);
        Assert.Equal(10, result.PromptTokens);
        Assert.Equal(5, result.CompletionTokens);
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task UnparseableModelOutput_IsInvalidModelOutput_AfterBoundedRetry()
    {
        var (useCase, _, chat) = Create(content: "I cannot answer as JSON, sorry!");

        var result = await useCase.ExecuteAsync(User, "recommend something");

        Assert.Equal(AssistantOutcome.InvalidModelOutput, result.Outcome);
        Assert.Equal(ProductAssistantUseCase.MaxAttempts, chat.Calls); // bounded, not infinite
    }

    [Fact]
    public async Task UpstreamError_RetriesThenReportsUnavailable()
    {
        var (useCase, _, chat) = Create(failWith: new HttpRequestException("connection refused"));

        var result = await useCase.ExecuteAsync(User, "recommend something");

        Assert.Equal(AssistantOutcome.UpstreamUnavailable, result.Outcome);
        Assert.Equal(ProductAssistantUseCase.MaxAttempts, chat.Calls);
    }

    [Fact]
    public async Task PerAttemptTimeout_RetriesThenReportsUnavailable()
    {
        // The fake sleeps 10s unless the use case's per-attempt CTS cancels it
        // (50ms here) — proving the timeout path without a slow test suite.
        var (useCase, _, chat) = Create(delay: TimeSpan.FromSeconds(10), timeout: TimeSpan.FromMilliseconds(50));

        var result = await useCase.ExecuteAsync(User, "recommend something");

        Assert.Equal(AssistantOutcome.UpstreamUnavailable, result.Outcome);
        Assert.Equal(ProductAssistantUseCase.MaxAttempts, chat.Calls);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesInsteadOfBecomingUnavailable()
    {
        var (useCase, _, _) = Create(delay: TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(User, "recommend something", cts.Token));
    }

    [Fact]
    public async Task NoCandidates_AnsweredFromApplication_WithoutModel()
    {
        var chat = new FakeAiChatClient();
        var useCase = new ProductAssistantUseCase(
            new FakeAssistantReadModel { Candidates = [] }, chat, new FakeAiRateLimiter(),
            NullLogger<ProductAssistantUseCase>.Instance);

        var result = await useCase.ExecuteAsync(User, "recommend something");

        Assert.Equal(AssistantOutcome.Ok, result.Outcome);
        Assert.Empty(result.RecommendedProducts);
        Assert.Equal(0, chat.Calls);
    }

    private static (ProductAssistantUseCase UseCase, FakeAiRateLimiter RateLimiter, FakeAiChatClient Chat) Create(
        string content = ValidJson,
        bool allow = true,
        Exception? failWith = null,
        TimeSpan? delay = null,
        TimeSpan? timeout = null)
    {
        var chat = new FakeAiChatClient { Content = content, Throw = failWith, Delay = delay ?? TimeSpan.Zero };
        var rl = new FakeAiRateLimiter(allow);
        var useCase = new ProductAssistantUseCase(
            new FakeAssistantReadModel(), chat, rl, NullLogger<ProductAssistantUseCase>.Instance)
        {
            AttemptTimeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        return (useCase, rl, chat);
    }
}
