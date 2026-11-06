package com.flashsale.payment.security;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

/**
 * In-memory token-bucket rate limiter keyed by client identifier (e.g. IP address).
 * <p>
 * Each client starts with a full bucket of {@code capacity} tokens. Every allowed
 * request consumes one token; tokens refill continuously at
 * {@code refillTokensPerMinute / 60} tokens per second, capped at capacity. When
 * the bucket is empty the request is rejected. All state is heap memory, so the
 * limit applies per service instance (sufficient for a single replica; scale out
 * would require a shared store such as Redis).
 * <p>
 * Default limit: 100 requests per minute per client/IP.
 */
@Component
public class InMemoryTokenBucketRateLimiter implements RateLimiter {

	private final int capacity;
	private final double refillTokensPerSecond;
	private final Map<String, Bucket> buckets = new ConcurrentHashMap<>();

	public InMemoryTokenBucketRateLimiter(
			@Value("${payment.rate-limit.capacity:100}") int capacity,
			@Value("${payment.rate-limit.refill-per-minute:100}") int refillTokensPerMinute) {
		this.capacity = capacity;
		this.refillTokensPerSecond = refillTokensPerMinute / 60.0;
	}

	@Override
	public boolean allowRequest(String clientId) {
		if (clientId == null || clientId.isBlank()) {
			clientId = "anonymous";
		}
		Bucket bucket = buckets.computeIfAbsent(clientId, k -> new Bucket(capacity, nowMillis()));
		synchronized (bucket) {
			refill(bucket);
			if (bucket.tokens >= 1.0) {
				bucket.tokens -= 1.0;
				return true;
			}
			return false;
		}
	}

	private void refill(Bucket bucket) {
		long now = nowMillis();
		long elapsedMs = now - bucket.lastRefill;
		if (elapsedMs <= 0) {
			return;
		}
		bucket.tokens = Math.min(capacity, bucket.tokens + (elapsedMs / 1000.0) * refillTokensPerSecond);
		bucket.lastRefill = now;
	}

	private static long nowMillis() {
		return System.currentTimeMillis();
	}

	private static class Bucket {
		double tokens;
		long lastRefill;

		Bucket(double tokens, long lastRefill) {
			this.tokens = tokens;
			this.lastRefill = lastRefill;
		}
	}
}

