package com.flashsale.payment.integration;

import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.service.PaymentService;
import org.junit.jupiter.api.Assertions;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.context.TestPropertySource;
import org.springframework.test.web.servlet.MockMvc;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;

import java.math.BigDecimal;
import java.time.Duration;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.MOCK)
@AutoConfigureMockMvc
@Testcontainers
@ActiveProfiles("demo")
@TestPropertySource(properties = "management.endpoint.health.show-details=always")
class RedisOutageIntegrationTest {

	private static final String JWT_SECRET =
		"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	@Container
	static PostgreSQLContainer<?> postgres =
		new PostgreSQLContainer<>(DockerImageName.parse("postgres:15-alpine"));

	@Container
	static GenericContainer<?> redis =
		new GenericContainer<>(DockerImageName.parse("redis:7-alpine")).withExposedPorts(6379);

	@DynamicPropertySource
	static void properties(DynamicPropertyRegistry registry) {
		registry.add("spring.datasource.url", postgres::getJdbcUrl);
		registry.add("spring.datasource.username", postgres::getUsername);
		registry.add("spring.datasource.password", postgres::getPassword);
		registry.add("spring.sql.init.mode", () -> "always");
		registry.add("spring.sql.init.schema-locations", () -> "file:docker/init-schema.sql");
		registry.add("spring.rabbitmq.listener.simple.auto-startup", () -> "false");
		registry.add("payment.outbox.poll-interval-ms", () -> "60000");
		registry.add("spring.data.redis.host", redis::getHost);
		registry.add("spring.data.redis.port", () -> redis.getMappedPort(6379));
		registry.add("app.jwt.secret", () -> JWT_SECRET);
		registry.add("demo.auth.password", () -> "integration-demo-password");
	}

	@Autowired
	private MockMvc mockMvc;

	@Autowired
	private PaymentRepository paymentRepository;

	@Autowired
	private PaymentService paymentService;

	@Test
	void paymentLookupFallsBackToPostgresDuringRedisOutageAndRecoversAfterward() {
		String idempotencyKey = "redis-outage-" + UUID.randomUUID();
		Payment persistedPayment = paymentRepository.save(new Payment(
			idempotencyKey,
			UUID.randomUUID(),
			1,
			new BigDecimal("10.01"),
			"USD",
			true,
			"Succeeded",
			"tx-redis-outage",
			null,
			false,
			OffsetDateTime.now(ZoneOffset.UTC)));

		assertThat(paymentService.getPayment(idempotencyKey).idempotencyKey()).isEqualTo(idempotencyKey);

		redis.getDockerClient().pauseContainerCmd(redis.getContainerId()).exec();
		try {
			assertThat(paymentService.getPayment(idempotencyKey).idempotencyKey())
				.isEqualTo(persistedPayment.getIdempotencyKey());
			Assertions.assertTimeoutPreemptively(Duration.ofSeconds(5), () ->
				mockMvc.perform(get("/actuator/health/readiness"))
					.andExpect(status().isServiceUnavailable())
					.andExpect(jsonPath("$.components.redis.status").value("DOWN"))
					.andReturn());
		} finally {
			redis.getDockerClient().unpauseContainerCmd(redis.getContainerId()).exec();
		}

		assertThat(paymentService.getPayment(idempotencyKey).idempotencyKey()).isEqualTo(idempotencyKey);
	}
}
