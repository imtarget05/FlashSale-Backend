using System.Threading.RateLimiting;
using Xunit;

namespace FlashSale.UnitTests;

/// <summary>
/// Unit tests for Authoritative Server-side Rate Limiting (ADR-025 / Phase 1).
/// Verifies the mathematical guarantees of the token bucket and partitioned rate limiters:
/// 1. Token bucket strictly enforces burst limits on sensitive order endpoints.
/// 2. Partitioned limiter enforces isolation per partition key (client IP / identity).
/// 3. Queuing is disabled (QueueLimit = 0) to guarantee fail-fast behavior and prevent memory saturation.
/// </summary>
public class RateLimitingTests
{
    [Fact]
    public void TokenBucketLimiter_AllowsBurstUpToPermitLimit_ThenRejectsImmediately()
    {
        // Sensitive endpoint policy: burst up to 3 permits
        var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 3,
            TokensPerPeriod = 2,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0 // Fail-fast
        });

        // First 3 requests acquire tokens immediately
        Assert.True(limiter.AttemptAcquire(1).IsAcquired);
        Assert.True(limiter.AttemptAcquire(1).IsAcquired);
        Assert.True(limiter.AttemptAcquire(1).IsAcquired);

        // 4th request must be rejected without queuing
        using var rejectedLease = limiter.AttemptAcquire(1);
        Assert.False(rejectedLease.IsAcquired);
    }

    [Fact]
    public void PartitionedLimiter_EnforcesRateLimitPerClientPartition()
    {
        // Simulates host IP partitioned rate limiter
        var partitionedLimiter = PartitionedRateLimiter.Create<string, string>(clientKey =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        });

        // Client A: 2 requests pass, 3rd fails
        Assert.True(partitionedLimiter.AttemptAcquire("client-ip-1.2.3.4", 1).IsAcquired);
        Assert.True(partitionedLimiter.AttemptAcquire("client-ip-1.2.3.4", 1).IsAcquired);
        Assert.False(partitionedLimiter.AttemptAcquire("client-ip-1.2.3.4", 1).IsAcquired);

        // Client B: independent partition, must NOT be throttled by Client A
        Assert.True(partitionedLimiter.AttemptAcquire("client-ip-5.6.7.8", 1).IsAcquired);
        Assert.True(partitionedLimiter.AttemptAcquire("client-ip-5.6.7.8", 1).IsAcquired);
        Assert.False(partitionedLimiter.AttemptAcquire("client-ip-5.6.7.8", 1).IsAcquired);
    }

    [Fact]
    public void RejectedLease_ProvidesRetryAfterMetadata()
    {
        var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });

        Assert.True(limiter.AttemptAcquire(1).IsAcquired);

        using var rejected = limiter.AttemptAcquire(1);
        Assert.False(rejected.IsAcquired);
        Assert.True(rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }
}
