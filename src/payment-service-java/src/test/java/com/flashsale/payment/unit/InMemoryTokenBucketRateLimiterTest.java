package com.flashsale.payment.unit;

import com.flashsale.payment.security.InMemoryTokenBucketRateLimiter;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

class InMemoryTokenBucketRateLimiterTest {

	@Test
	void allowsRequestsUpToCapacityThenRejects() {
		RateLimiterTestHarness limiter = new RateLimiterTestHarness(2, 0);

		assertThat(limiter.allow("client-a")).isTrue();
		assertThat(limiter.allow("client-a")).isTrue();
		assertThat(limiter.allow("client-a")).isFalse();
	}

	@Test
	void tracksClientsIndependently() {
		RateLimiterTestHarness limiter = new RateLimiterTestHarness(1, 0);

		assertThat(limiter.allow("client-a")).isTrue();
		assertThat(limiter.allow("client-b")).isTrue();
		assertThat(limiter.allow("client-a")).isFalse();
		assertThat(limiter.allow("client-b")).isFalse();
	}

	@Test
	void nullOrBlankClientIdIsTreatedAsAnonymous() {
		RateLimiterTestHarness limiter = new RateLimiterTestHarness(1, 0);

		assertThat(limiter.allow(null)).isTrue();
		assertThat(limiter.allow("")).isFalse();
		assertThat(limiter.allow("   ")).isFalse();
	}

	@Test
	void refillsTokensOverTime() throws InterruptedException {
		RateLimiterTestHarness limiter = new RateLimiterTestHarness(1, 6000);

		assertThat(limiter.allow("client-a")).isTrue();
		assertThat(limiter.allow("client-a")).isFalse();

		Thread.sleep(100);

		assertThat(limiter.allow("client-a")).isTrue();
	}

	private static class RateLimiterTestHarness {
		private final InMemoryTokenBucketRateLimiter limiter;

		RateLimiterTestHarness(int capacity, int refillPerMinute) {
			this.limiter = new InMemoryTokenBucketRateLimiter(capacity, refillPerMinute);
		}

		boolean allow(String clientId) {
			return limiter.allowRequest(clientId);
		}
	}

}

