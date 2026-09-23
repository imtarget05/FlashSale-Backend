using FlashSale.Application.Messaging;
using FlashSale.Application.Persistence;

namespace FlashSale.UnitTests;

/// <summary>
/// In-memory fake implementing the Application port — proves the use case can be
/// tested without PostgreSQL, Redis or Service Bus (Clean Architecture payoff).
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly HashSet<string> _seen = [];
    public int PersistCalls { get; private set; }
    public bool NextPersistSucceeds { get; set; } = true;

    public void SeedExisting(string idempotencyKey) => _seen.Add(idempotencyKey);

    public Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(_seen.Contains(idempotencyKey));

    public Task<bool> PersistAsync(OrderMessage message, CancellationToken ct = default)
    {
        PersistCalls++;
        if (!NextPersistSucceeds) return Task.FromResult(false);
        _seen.Add(message.IdempotencyKey);
        _ids[message.IdempotencyKey] = _nextId++;
        return Task.FromResult(true);
    }

    private readonly Dictionary<string, int> _ids = new();
    private int _nextId = 1;

    public Task<int?> GetOrderIdAsync(string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(_ids.TryGetValue(idempotencyKey, out var id) ? (int?)id : null);
}
