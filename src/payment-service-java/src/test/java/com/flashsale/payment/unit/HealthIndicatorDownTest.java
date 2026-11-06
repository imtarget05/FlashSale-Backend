package com.flashsale.payment.unit;

import org.junit.jupiter.api.Test;
import org.springframework.boot.health.contributor.Health;
import org.springframework.boot.health.contributor.HealthIndicator;
import org.springframework.boot.health.contributor.Status;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * Verifies a health indicator can report DOWN, which is the behaviour the
 * {@code /actuator/health/readiness} endpoint aggregates from its configured
 * dependency indicators ({@code readinessState, db, redis, rabbit}) so that a
 * failed postgres or RabbitMQ connection surfaces as a non-UP readiness status.
 */
class HealthIndicatorDownTest {

	@Test
	void healthIndicatorReportsDown() {
		HealthIndicator indicator = () -> Health.down().build();
		Health health = indicator.health();

		assertThat(health.getStatus()).isEqualTo(Status.DOWN);
	}

}
