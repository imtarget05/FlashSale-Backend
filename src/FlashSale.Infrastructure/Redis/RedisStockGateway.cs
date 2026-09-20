using FlashSale.Application.Messaging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Redis;

/// <summary>
/// Fast, best-effort inventory reservation in Redis (ADR-003).
/// Redis is a *filter and rate-limiter*, never the source of truth.
///   - stock:{productId}   hash: field "qty" (mirrored from PostgreSQL)
///   - reservation:{pid}   set: idempotency keys already seen
///   - reservation is ONE atomic Lua CAS (check + decrement + ledger)
/// If Redis is down, the gateway reports Unavailable and the API falls back
/// to the DB path (slower but correct).
/// </summary>
public sealed class RedisStockGateway(IConnectionMultiplexer multiplexer) : IStockReservationGateway
{
    private readonly IDatabase _db = multiplexer.GetDatabase();

    private static string StockKey(int productId) => $"stock:{productId}";
    private static string LedgerKey(int productId) => $"reservation:{productId}";

    private const string ReserveScript = """
        local qty    = redis.call('HGET', KEYS[1], 'qty')
        if not qty then return -1 end                 -- unknown product: caller seeds
        local want   = tonumber(ARGV[1])
        local key    = ARGV[2]
        if redis.call('SISMEMBER', KEYS[2], key) == 1 then return -3 end  -- duplicate
        if tonumber(qty) < want then return 0 end     -- sold out
        redis.call('HINCRBY', KEYS[1], 'qty', -want)
        redis.call('SADD', KEYS[2], key)
        return 1                                      -- reserved
        """;

    private const string ReleaseScript = """
        redis.call('HINCRBY', KEYS[1], 'qty', tonumber(ARGV[1]))
        redis.call('SREM', KEYS[2], ARGV[2])
        return 1
        """;

    public async Task<ReservationResult> TryReserveAsync(int productId, int quantity, string idempotencyKey)
    {
        try
        {
            var result = (int)await _db.ScriptEvaluateAsync(ReserveScript,
                [(RedisKey)StockKey(productId), (RedisKey)LedgerKey(productId)],
                [(RedisValue)quantity, (RedisValue)idempotencyKey]);
            return result switch
            {
                1 => ReservationResult.Reserved,
                0 => ReservationResult.SoldOut,
                -3 => ReservationResult.Duplicate,
                -1 => ReservationResult.UnknownProduct,
                _ => ReservationResult.Unavailable
            };
        }
        catch (RedisException)
        {
            return ReservationResult.Unavailable;
        }
    }

    public async Task ReleaseReservationAsync(int productId, int quantity, string idempotencyKey)
    {
        try
        {
            await _db.ScriptEvaluateAsync(ReleaseScript,
                [(RedisKey)StockKey(productId), (RedisKey)LedgerKey(productId)],
                [(RedisValue)quantity, (RedisValue)idempotencyKey]);
        }
        catch (RedisException)
        {
            // Best-effort: if Redis is down, the resync endpoint reconciles later.
        }
    }

    public async Task SetStockAsync(int productId, int stock)
    {
        await _db.HashSetAsync(StockKey(productId), "qty", stock);
    }

    public async Task<int?> GetCachedStockAsync(int productId)
    {
        var value = await _db.HashGetAsync(StockKey(productId), "qty");
        return value.IsNull ? null : (int?)value;
    }
}
