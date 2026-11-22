package com.flashsale.payment.security;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Primary;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Component;

import java.time.Duration;
import java.util.UUID;

/**
 * Enterprise Distributed Sliding-Window Rate Limiter using Redis Sorted Sets (ZSET).
 * <p>
 * Problem Solved in Banking / High-Concurrency FinTech:
 * In a multi-replica Kubernetes deployment, in-memory rate limiters allow each replica
 * its own quota, resulting in an effective rate limit of {@code N * quota} across N pods.
 * <p>
 * This distributed implementation provides:
 * 1. Global cluster-wide rate limiting across all microservice pods.
 * 2. Sliding window precision (eliminating the 2x burst loophole of fixed-window limiters).
 * 3. Resilient Fallback: If Redis suffers network partition or failure, automatically
 *    degrades to local in-memory token bucket without throwing 500 errors to customers.
 */
@Primary
@Component
public class DistributedRedisRateLimiter implements RateLimiter {

    private static final Logger log = LoggerFactory.getLogger(DistributedRedisRateLimiter.class);

    private final StringRedisTemplate redisTemplate;
    private final RateLimiter fallbackLimiter;
    private final int maxRequestsPerWindow;
    private final long windowMillis;

    @Autowired
    public DistributedRedisRateLimiter(
            @Autowired(required = false) StringRedisTemplate redisTemplate,
            InMemoryTokenBucketRateLimiter fallbackLimiter,
            @Value("${payment.rate-limit.capacity:100}") int maxRequestsPerWindow,
            @Value("${payment.rate-limit.window-seconds:60}") int windowSeconds
    ) {
        this.redisTemplate = redisTemplate;
        this.fallbackLimiter = fallbackLimiter;
        this.maxRequestsPerWindow = maxRequestsPerWindow;
        this.windowMillis = windowSeconds * 1000L;
    }

    @Override
    public boolean allowRequest(String clientId) {
        if (clientId == null || clientId.isBlank()) {
            clientId = "anonymous";
        }

        // If Redis is not configured or available, smoothly fall back to local token bucket
        if (redisTemplate == null) {
            return fallbackLimiter.allowRequest(clientId);
        }

        String key = "rate_limit:" + clientId;
        long now = System.currentTimeMillis();
        long windowStart = now - windowMillis;

        try {
            // 1. Remove expired timestamps outside the sliding window
            redisTemplate.opsForZSet().removeRangeByScore(key, 0, windowStart);

            // 2. Count active requests within current sliding window
            Long count = redisTemplate.opsForZSet().zCard(key);
            if (count != null && count >= maxRequestsPerWindow) {
                log.debug("Rate limit exceeded for client: {}, current count: {} >= limit: {}",
                        clientId, count, maxRequestsPerWindow);
                return false;
            }

            // 3. Record new request timestamp with a unique member to avoid collision
            String member = now + ":" + UUID.randomUUID().toString().substring(0, 8);
            redisTemplate.opsForZSet().add(key, member, now);
            redisTemplate.expire(key, Duration.ofMillis(windowMillis * 2));

            return true;
        } catch (Exception ex) {
            // Resilient Fallback Pattern: Redis connection failure must not drop legitimate payments
            log.warn("Redis rate limiter failed for client {}: {}. Falling back to in-memory rate limiter.",
                    clientId, ex.getMessage());
            return fallbackLimiter.allowRequest(clientId);
        }
    }
}
