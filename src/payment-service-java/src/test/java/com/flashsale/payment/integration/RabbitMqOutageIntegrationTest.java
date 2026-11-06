package com.flashsale.payment.integration;

import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.outbox.PaymentOutbox;
import com.flashsale.payment.outbox.PaymentOutboxRelay;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import org.junit.jupiter.api.Test;
import org.springframework.amqp.AmqpException;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.containers.RabbitMQContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.utility.DockerImageName;

import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.Duration;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.MOCK)
@Testcontainers
@ActiveProfiles("demo")
class RabbitMqOutageIntegrationTest {

	private static final String JWT_SECRET =
		"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	@Container
	static PostgreSQLContainer<?> postgres =
		new PostgreSQLContainer<>(DockerImageName.parse("postgres:15-alpine"));

	@Container
	static RabbitMQContainer rabbitmq = new RabbitMQContainer(
		DockerImageName.parse("rabbitmq:3-management-alpine"))
		.withCreateContainerCmdModifier(cmd -> cmd.withUser("100:101"))
		.waitingFor(Wait.forSuccessfulCommand("rabbitmq-diagnostics -q check_running"));

	@DynamicPropertySource
	static void properties(DynamicPropertyRegistry registry) {
		registry.add("spring.datasource.url", postgres::getJdbcUrl);
		registry.add("spring.datasource.username", postgres::getUsername);
		registry.add("spring.datasource.password", postgres::getPassword);
		registry.add("spring.sql.init.mode", () -> "always");
		registry.add("spring.sql.init.schema-locations", () -> "file:docker/init-schema.sql");
		registry.add("spring.rabbitmq.host", rabbitmq::getHost);
		registry.add("spring.rabbitmq.port", rabbitmq::getAmqpPort);
		registry.add("spring.rabbitmq.username", () -> "guest");
		registry.add("spring.rabbitmq.password", () -> "guest");
		registry.add("spring.rabbitmq.listener.simple.auto-startup", () -> "false");
		registry.add("spring.rabbitmq.connection-timeout", () -> "2s");
		registry.add("payment.outbox.poll-interval-ms", () -> "60000");
		registry.add("app.jwt.secret", () -> JWT_SECRET);
		registry.add("demo.auth.password", () -> "integration-demo-password");
	}

	@Autowired
	private PaymentOutboxRepository outboxRepository;

	@Autowired
	private PaymentOutboxRelay outboxRelay;

	@Autowired
	private RabbitTemplate rabbitTemplate;

	@Test
	void pendingOutboxEventRetriesAndPublishesAfterRabbitMqRecovers() throws Exception {
		String idempotencyKey = "rabbit-outage-" + UUID.randomUUID();
		OffsetDateTime createdAt = OffsetDateTime.now(ZoneOffset.UTC).minusMinutes(1);
		PaymentOutbox event = outboxRepository.saveAndFlush(new PaymentOutbox(
			idempotencyKey,
			RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED,
			"""
				{"idempotencyKey":"%s","success":true,"status":"Succeeded","transactionId":"tx-1",
				"failureReason":null,"isTransientError":false,"processedAt":"%s"}
				""".formatted(idempotencyKey, createdAt),
			createdAt));

		rabbitTemplate.execute(channel -> Boolean.TRUE);
		rabbitmq.getDockerClient().pauseContainerCmd(rabbitmq.getContainerId()).exec();
		try {
			outboxRelay.publishPendingEvents();

			PaymentOutbox pending = outboxRepository.findById(event.getId()).orElseThrow();
			assertThat(pending.getPublishedAt()).isNull();
			assertThat(pending.getAttempts()).isEqualTo(1);
			assertThat(pending.getLastError()).isNotBlank();
		} finally {
			rabbitmq.getDockerClient().unpauseContainerCmd(rabbitmq.getContainerId()).exec();
		}
		awaitRabbitMqConnection();

		PaymentOutbox pending = outboxRepository.findById(event.getId()).orElseThrow();
		pending.setNextAttemptAt(OffsetDateTime.now(ZoneOffset.UTC).minusSeconds(1));
		outboxRepository.saveAndFlush(pending);

		outboxRelay.publishPendingEvents();

		PaymentOutbox published = outboxRepository.findById(event.getId()).orElseThrow();
		assertThat(published.getPublishedAt()).isNotNull();
		assertThat(published.getAttempts()).isEqualTo(1);
	}

	private void awaitRabbitMqConnection() throws Exception {
		long deadline = System.nanoTime() + Duration.ofSeconds(15).toNanos();
		AmqpException lastFailure = null;
		while (System.nanoTime() < deadline) {
			try {
				rabbitTemplate.execute(channel -> Boolean.TRUE);
				return;
			} catch (AmqpException ex) {
				lastFailure = ex;
				Thread.sleep(250);
			}
		}
		throw new IllegalStateException("RabbitMQ did not recover within 15 seconds", lastFailure);
	}
}
