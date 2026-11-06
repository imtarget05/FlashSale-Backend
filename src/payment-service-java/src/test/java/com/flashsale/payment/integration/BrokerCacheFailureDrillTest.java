package com.flashsale.payment.integration;

import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.outbox.PaymentOutbox;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import com.flashsale.payment.outbox.PaymentOutboxRelay;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.service.PaymentService;
import org.springframework.amqp.AmqpException;
import org.springframework.amqp.core.BindingBuilder;
import org.springframework.amqp.core.DirectExchange;
import org.springframework.amqp.core.Queue;
import org.springframework.amqp.core.AmqpAdmin;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;
import org.junit.jupiter.api.Test;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.transaction.annotation.Transactional;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.containers.RabbitMQContainer;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;

import java.math.BigDecimal;
import java.time.Duration;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.MOCK)
@Testcontainers
@ActiveProfiles("demo")
class BrokerCacheFailureDrillTest {

	private static final String JWT_SECRET =
		"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

	@Container
	static PostgreSQLContainer<?> postgres =
		new PostgreSQLContainer<>(DockerImageName.parse("postgres:15-alpine"));

	@Container
	static RabbitMQContainer rabbitmq = new RabbitMQContainer(
		DockerImageName.parse("rabbitmq:3-management-alpine"))
		.withCreateContainerCmdModifier(cmd -> cmd.withUser("100:101"))
		.waitingFor(Wait.forSuccessfulCommand("rabbitmq-diagnostics -q check_running"))
		.withStartupTimeout(Duration.ofMinutes(2));

	@Container
	static GenericContainer<?> redis = new GenericContainer<>(DockerImageName.parse("redis:7-alpine"))
		.withExposedPorts(6379);

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
		registry.add("spring.data.redis.host", redis::getHost);
		registry.add("spring.data.redis.port", () -> redis.getMappedPort(6379));
		registry.add("spring.data.redis.timeout", () -> "1s");
		registry.add("app.jwt.secret", () -> JWT_SECRET);
		registry.add("demo.auth.password", () -> "drill-demo-password-16");
	}

	@Autowired
	private PaymentRepository paymentRepository;

	@Autowired
	private PaymentOutboxRepository outboxRepository;

	@Autowired
	private PaymentOutboxRelay outboxRelay;

	@Autowired
	private PaymentService paymentService;

	@Autowired
	private RabbitTemplate rabbitTemplate;

	@Autowired
	private AmqpAdmin amqpAdmin;

	@Autowired
	private StringRedisTemplate redisTemplate;

	@Autowired
	private JdbcTemplate jdbcTemplate;

	private long countPayments(String idempotencyKey) {
		return jdbcTemplate.queryForObject(
			"SELECT COUNT(*) FROM payment_records WHERE idempotency_key = ?", Long.class, idempotencyKey);
	}

	@Test
	@Transactional
	void insertIfAbsent_preventsDoubledPaymentUnderRedelivery() {
		String idempotencyKey = "drill-idem-" + UUID.randomUUID();
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			UUID.randomUUID(), 101, idempotencyKey, new BigDecimal("10.01"), "USD");

		PaymentRecordDto first = paymentService.processPayment(request);

		// Redelivery of the same idempotency key must replay the winner, not double the payment.
		PaymentRecordDto replay = paymentService.processPayment(request);
		assertThat(replay.transactionId()).isEqualTo(first.transactionId());
		assertThat(replay.status()).isEqualTo(first.status());
		assertThat(countPayments(idempotencyKey)).isEqualTo(1L);

		// Direct insertIfAbsent (INSERT ... ON CONFLICT DO NOTHING) must not insert a duplicate row.
		Payment duplicate = new Payment(idempotencyKey, UUID.randomUUID(), 102, new BigDecimal("99.99"),
			"USD", true, "Succeeded", "tx-dup", null, false, OffsetDateTime.now());
		int inserted = paymentRepository.insertIfAbsent(duplicate);
		assertThat(inserted).isZero();
		assertThat(countPayments(idempotencyKey)).isEqualTo(1L);
	}

	@Test
	void rabbitMqStopStart_outboxReplayPublishesAfterRecovery() throws Exception {
		String idempotencyKey = "drill-outbox-" + UUID.randomUUID();
		OffsetDateTime createdAt = OffsetDateTime.now(ZoneOffset.UTC).minusMinutes(1);
		PaymentOutbox event = outboxRepository.saveAndFlush(new PaymentOutbox(
			idempotencyKey, RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED,
			"{\"idempotencyKey\":\"" + idempotencyKey + "\",\"success\":true,\"status\":\"Succeeded\"}", createdAt));

		// Simulate broker outage.
		execRabbitMqCtl("stop_app");
		try {
			outboxRelay.publishPendingEvents();

			PaymentOutbox pending = outboxRepository.findById(event.getId()).orElseThrow();
			assertThat(pending.getPublishedAt()).isNull();
			assertThat(pending.getAttempts()).isGreaterThan(0);
			assertThat(pending.getLastError()).isNotBlank();
		} finally {
			// Broker recovery.
			execRabbitMqCtl("start_app");
		}

		awaitRabbitMqConnection();

		// Make the failed event due again so the relay can replay it.
		PaymentOutbox pending = outboxRepository.findById(event.getId()).orElseThrow();
		pending.setNextAttemptAt(OffsetDateTime.now(ZoneOffset.UTC).minusSeconds(1));
		outboxRepository.saveAndFlush(pending);

		// FOR UPDATE SKIP LOCKED claim of due events.
		List<PaymentOutbox> due = outboxRepository.lockDueEvents(
			OffsetDateTime.now(ZoneOffset.UTC), 25);
		assertThat(due).isNotEmpty();

		// Replay after recovery must publish the event. A bound queue is required so the
		// mandatory publisher confirm routes successfully.
		Queue replayQueue = new Queue("payment.drill.replay." + UUID.randomUUID(), false, false, true);
		amqpAdmin.declareQueue(replayQueue);
		amqpAdmin.declareBinding(BindingBuilder.bind(replayQueue)
			.to(new DirectExchange(RabbitConfig.PAYMENT_EVENTS_EXCHANGE))
			.with(RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED));
		outboxRelay.publishPendingEvents();
		PaymentOutbox published = outboxRepository.findById(event.getId()).orElseThrow();
		assertThat(published.getPublishedAt()).isNotNull();
	}

	@Test
	void duplicateDelivery_doesNotDoublePayment() {
		String idempotencyKey = "drill-dup-" + UUID.randomUUID();
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			UUID.randomUUID(), 103, idempotencyKey, new BigDecimal("10.01"), "USD");

		paymentService.processPayment(request);

		// Simulate redelivery of the same order/payment event.
		paymentService.processPayment(request);

		assertThat(countPayments(idempotencyKey)).isEqualTo(1L);
		assertThat(jdbcTemplate.queryForObject(
            "SELECT COUNT(*) FROM payment_outbox WHERE idempotency_key = ?", Long.class, idempotencyKey))
        .isEqualTo(1L);
	}

	@Test
	void dlqQueueExistsAndCanHoldMessage() {
		assertThat(amqpAdmin.getQueueInfo(RabbitConfig.PAYMENT_DLQ)).isNotNull();

		rabbitTemplate.convertAndSend("", RabbitConfig.PAYMENT_DLQ, "dlq-drill-message");
		Object received = rabbitTemplate.receiveAndConvert(RabbitConfig.PAYMENT_DLQ, 5_000);
		assertThat(received).isEqualTo("dlq-drill-message");
	}

	@Test
	void redisOutage_getPaymentFallsBackToPostgres() throws Exception {
		String idempotencyKey = "drill-redis-" + UUID.randomUUID();
		Payment persisted = paymentRepository.save(new Payment(
			idempotencyKey, UUID.randomUUID(), 1, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx-redis-drill", null, false, OffsetDateTime.now()));
		paymentRepository.flush();

		assertThat(paymentService.getPayment(idempotencyKey).idempotencyKey()).isEqualTo(idempotencyKey);

		redis.getDockerClient().pauseContainerCmd(redis.getContainerId()).exec();
		try {
			assertThat(paymentService.getPayment(idempotencyKey).idempotencyKey())
				.isEqualTo(persisted.getIdempotencyKey());
		} finally {
			redis.getDockerClient().unpauseContainerCmd(redis.getContainerId()).exec();
		}

		assertThat(paymentService.getPayment(idempotencyKey).idempotencyKey()).isEqualTo(idempotencyKey);
	}

	private void execRabbitMqCtl(String command) throws Exception {
		String[] cmd = new String[] { "sh", "-c", "rabbitmqctl " + command };
		var execCreate = rabbitmq.getDockerClient().execCreateCmd(rabbitmq.getContainerId())
			.withCmd(cmd)
			.withAttachStdout(true)
			.withAttachStderr(true);
		String execId = execCreate.exec().getId();
		rabbitmq.getDockerClient().execStartCmd(execId);
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
