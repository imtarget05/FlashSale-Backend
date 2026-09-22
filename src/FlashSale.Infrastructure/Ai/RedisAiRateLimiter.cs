using FlashSale.Application.Assistant;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FlashSale.Infrastructure.Ai;

/// <summary>
/// Fixed one-minute window per user (spec §10 "rate limit"): INCR + EXPIRE on a
/// key that embeds the current unix minute, so windows roll without cleanup
/// jobs. Same fail-open stance as the order path — Redis is runtime-optional,
/// so an outage ALLOWS the request (and logs a warning) instead of taking the
/// assistant down with it.
/// </summary>
public sealed class RedisAiRateLimiter(IConnectionMultiplexer redis, ILogger<RedisAiRateLimiter> logger) : IAiRateLimiter
{
    public const int MaxRequestsPerMinute = 5;

    public async Task<bool> TryAcquireAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var db = redis.GetDatabase();
            // Unix-minute bucket for the fixed window (fully qualified: immune to
            // any using-directive surprises in this multi-namespace project).
            var minute = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            var count = await db.StringIncrementAsync($"flashsale:ai:rl:{userId}:{minute}");
            if (count == 1)
                await db.KeyExpireAsync($"flashsale:ai:rl:{userId}:{minute}", TimeSpan.FromSeconds(70));

            return count <= MaxRequestsPerMinute;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "AI rate limiter unavailable — failing OPEN (request allowed).");
            return true;
        }
    }
}
