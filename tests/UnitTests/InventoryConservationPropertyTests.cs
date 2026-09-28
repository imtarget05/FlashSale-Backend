using System.Collections.Concurrent;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using Xunit;

namespace FlashSale.UnitTests;

/// <summary>
/// Phase-2 property tests: inventory conservation under seeded-randomized
/// concurrency. Uses a thread-safe in-memory stock gateway (same
/// <see cref="IStockReservationGateway"/> port the Redis adapter implements)
/// plus the existing <see cref="FakeOrderRepository"/> for idempotency —
/// no Testcontainers, no Docker.
/// Invariants per case: sold &lt;= initial, finalStock &gt;= 0,
/// initial == final + sold.
/// </summary>
public class InventoryConservationPropertyTests
{
    /// <summary>
    /// Thread-safe counting fake: real stock accounting (decrement on reserve,
    /// <see cref="ReservationResult.Duplicate"/> for replayed keys,
    /// <see cref="ReservationResult.SoldOut"/> when empty).
    /// </summary>
    private sealed class CountingStockGateway : IStockReservationGateway
    {
        private readonly object _lock = new();
        private int _stock;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);

        public CountingStockGateway(int initialStock) => _stock = initialStock;

        public Task<ReservationResult> TryReserveAsync(int productId, int quantity, string idempotencyKey)
        {
            lock (_lock)
            {
                if (!_seen.Add(idempotencyKey))
                {
                    return Task.FromResult(ReservationResult.Duplicate);
                }
                if (_stock >= quantity)
                {
                    _stock -= quantity;
                    _reserved.Add(idempotencyKey);
                    return Task.FromResult(ReservationResult.Reserved);
                }
                return Task.FromResult(ReservationResult.SoldOut);
            }
        }

        public Task ReleaseReservationAsync(int productId, int quantity, string idempotencyKey)
        {
            lock (_lock)
            {
                if (_reserved.Remove(idempotencyKey))
                {
                    _stock += quantity;
                }
                return Task.CompletedTask;
            }
        }

        public Task SetStockAsync(int productId, int stock)
        {
            lock (_lock)
            {
                _stock = stock;
                return Task.CompletedTask;
            }
        }

        public Task<int?> GetCachedStockAsync(int productId)
        {
            lock (_lock)
            {
                return Task.FromResult<int?>(_stock);
            }
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        var rng = new Random(20260927);
        for (int i = 0; i < 12; i++)
        {
            int initialStock = rng.Next(1, 101);
            int attempts = rng.Next(1, 201);
            int parallelism = rng.Next(1, 17);
            int duplicateKeys = rng.Next(0, Math.Min(attempts, 51));
            yield return new object[] { initialStock, attempts, parallelism, duplicateKeys };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Conservation_Holds_UnderConcurrentAttempts(
        int initialStock, int attempts, int parallelism, int duplicateKeys)
    {
        var gateway = new CountingStockGateway(initialStock);
        var repo = new FakeOrderRepository(); // existing fake: idempotency ledger
        await gateway.SetStockAsync(1, initialStock);

        // Build the attempt key list: unique keys + duplicate replays of random ones.
        var rng = new Random(initialStock * 7919 + attempts * 131 + parallelism * 17 + duplicateKeys);
        var uniqueKeys = Enumerable.Range(0, attempts - duplicateKeys)
            .Select(i => $"prop-{initialStock}-{attempts}-{i}")
            .ToList();
        var keys = new List<string>(uniqueKeys);
        for (int i = 0; i < duplicateKeys; i++)
        {
            keys.Add(uniqueKeys[rng.Next(uniqueKeys.Count)]);
        }
        // Seeded shuffle so duplicates interleave.
        for (int i = keys.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (keys[i], keys[j]) = (keys[j], keys[i]);
        }

        var outcomes = new ConcurrentBag<ReservationResult>();
        var reservedKeys = new ConcurrentBag<string>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelism };
        await Parallel.ForEachAsync(keys, options, async (key, ct) =>
        {
            var result = await gateway.TryReserveAsync(1, 1, key);
            outcomes.Add(result);
            if (result == ReservationResult.Reserved)
            {
                reservedKeys.Add(key);
            }
        });

        // Persist each successful reservation through the existing fake repository.
        foreach (var key in reservedKeys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            await repo.PersistAsync(
                new OrderMessage(ProductId: 1, Quantity: 1, IdempotencyKey: key, CreatedAt: DateTimeOffset.UtcNow));
        }

        int sold = outcomes.Count(r => r == ReservationResult.Reserved);
        int? finalStock = await gateway.GetCachedStockAsync(1);

        Assert.NotNull(finalStock);
        Assert.True(sold <= initialStock); // never oversell
        Assert.True(finalStock.Value >= 0); // stock never negative
        Assert.Equal(initialStock, finalStock.Value + sold); // conservation
        Assert.Equal(sold + outcomes.Count(r => r == ReservationResult.SoldOut)
            + outcomes.Count(r => r == ReservationResult.Duplicate), attempts);
        Assert.Equal(sold, repo.PersistCalls); // one persisted order per unit sold
    }
}
