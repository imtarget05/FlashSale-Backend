using FlashSale.Application.Persistence;
using FlashSale.Domain.Messaging;

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
        return Task.FromResult(true);
    }
}
