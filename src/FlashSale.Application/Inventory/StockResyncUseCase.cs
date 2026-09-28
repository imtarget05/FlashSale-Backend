using FlashSale.Application.Persistence;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Inventory;

/// <summary>
/// Outcome of a stock reconciliation check for one product: the Redis mirror
/// (<c>Redis</c>) against the PostgreSQL source of truth (<c>Postgres</c>).
/// <c>Drift</c> is <c>Redis − Postgres</c> (a missing Redis key counts as 0,
/// i.e. a fully-drifted mirror); <c>Fixed</c> is true only when this run
/// actually overwrote Redis.
/// </summary>
public sealed record StockResyncResult(
    int ProductId,
    int? Redis,
    int Postgres,
    int Drift,
    bool Fixed);

/// <summary>
/// Use case: reconcile the Redis reservation mirror with PostgreSQL truth
/// (ADR-003: Redis is a fast-fail filter, never the source of truth).
///
/// Drift happens when a crash lands between the Redis decrement (T1) and the
/// DB persist — the worker's authoritative atomic UPDATE then disagrees with
/// Redis, the message is dead-lettered with <c>StockDriftException</c>, and
/// this runbook use case reconciles. With <c>dryRun</c> (the default) it only
/// reports; with <c>dryRun: false</c> it overwrites Redis with the Postgres
/// value. Depends only on Application ports — no Infrastructure references.
/// </summary>
public sealed class StockResyncUseCase(
    IOrderReadModel readModel,
    IStockReservationGateway reservationGateway,
    ILogger<StockResyncUseCase> logger)
{
    public async Task<StockResyncResult?> ExecuteAsync(
        int productId,
        bool dryRun = true,
        CancellationToken ct = default)
    {
        var postgres = await readModel.GetStockAsync(productId, ct);
        if (postgres is null)
        {
            logger.LogWarning("Stock resync: product {ProductId} not found.", productId);
            return null;
        }

        var redis = await reservationGateway.GetCachedStockAsync(productId);
        var drift = (redis ?? 0) - postgres.Value;
        var fixed_ = false;

        if (!dryRun && drift != 0)
        {
            await reservationGateway.SetStockAsync(productId, postgres.Value);
            fixed_ = true;
            logger.LogWarning(
                "Stock resync: product {ProductId} drift {Drift} fixed (redis {Redis} -> postgres {Postgres}).",
                productId, drift, redis, postgres.Value);
        }

        return new StockResyncResult(productId, redis, postgres.Value, drift, fixed_);
    }
}
