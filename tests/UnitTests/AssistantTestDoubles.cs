using FlashSale.Application.Assistant;
using FlashSale.Application.Persistence;

namespace FlashSale.UnitTests;

/// <summary>
/// In-memory doubles for the assistant ports — the use case and the grounding
/// guard are testable without Ollama, Redis or PostgreSQL (Clean Architecture
/// payoff, same as <see cref="FakeOrderRepository"/>).
/// </summary>
internal sealed class FakeAiChatClient : IAiChatClient
{
    public string Content { get; set; } = "{}";
    public Exception? Throw { get; set; }
    public TimeSpan Delay { get; set; }
    public int Calls { get; private set; }

    public async Task<AiChatResponse> CompleteAsync(IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        Calls++;
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Throw is not null) throw Throw;
        return new AiChatResponse(Content, "qwen3:4b", 10, 5);
    }
}

internal sealed class FakeAiRateLimiter(bool allow = true) : IAiRateLimiter
{
    public int Calls { get; private set; }

    public Task<bool> TryAcquireAsync(Guid userId, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(allow);
    }
}

internal sealed class FakeAssistantReadModel : IOrderReadModel
{
    public IReadOnlyList<ProductCandidate> Candidates { get; set; } =
    [
        new(1, "Phone X", "Electronics", "Flagship smartphone", 199m, 5),
        new(2, "Watch Y", "Wearables", "Fitness smartwatch", 99m, 3)
    ];

    public Task<int?> GetStockAsync(int productId, CancellationToken ct = default) => Task.FromResult<int?>(5);

    public Task<ProductView?> GetProductAsync(int productId, CancellationToken ct = default) =>
        Task.FromResult<ProductView?>(new ProductView(1, "Phone X", 5, 199m, "Flagship smartphone"));

    public Task<OrderStatusView?> GetOrderStatusAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult<OrderStatusView?>(null);

    public Task<IReadOnlyList<OrderSummaryView>> GetOrdersByUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderSummaryView>>([]);

    public Task<IReadOnlyList<ProductCandidate>> GetProductCandidatesAsync(int max, CancellationToken ct = default) =>
        Task.FromResult(Candidates);

    public Task<IReadOnlyList<PendingPaymentView>> GetPendingPaymentOrdersAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PendingPaymentView>>(PendingPayments ?? []);

    /// <summary>Test hook: rows the payment-timeout scan will see.</summary>
    public IReadOnlyList<PendingPaymentView>? PendingPayments { get; set; }

    public Task<ProductFactsView?> GetProductFactsAsync(int productId, CancellationToken ct = default) =>
        Task.FromResult<ProductFactsView?>(
            new ProductFactsView(1, "Phone X", "Electronics", "Flagship smartphone", 199m, 5));
}
