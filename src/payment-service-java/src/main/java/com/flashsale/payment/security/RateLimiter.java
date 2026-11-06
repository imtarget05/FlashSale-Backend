package com.flashsale.payment.security;

/**
 * Rate limiter for protecting write-heavy endpoints from traffic spikes.
 * <p>
 * Implementations decide whether a request from a given client identifier
 * should be allowed or rejected (HTTP 429 when rejected by the caller).
 */
public interface RateLimiter {

	/**
	 * Returns {@code true} if the request from {@code clientId} may proceed,
	 * {@code false} if it should be rate-limited.
	 */
	boolean allowRequest(String clientId);
}

