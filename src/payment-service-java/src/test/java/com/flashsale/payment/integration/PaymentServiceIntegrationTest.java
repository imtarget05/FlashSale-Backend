package com.flashsale.payment.integration;

import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.audit.PaymentAuditRepository;
import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.event.OrderEventDTO;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.security.JwtUtil;
import org.springframework.amqp.core.AmqpAdmin;
import org.springframework.amqp.core.BindingBuilder;
import org.springframework.amqp.core.DirectExchange;
import org.springframework.amqp.core.Queue;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.http.HttpHeaders;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.web.reactive.server.WebTestClient;
import org.springframework.test.web.reactive.server.EntityExchangeResult;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.containers.RabbitMQContainer;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.MongoDBContainer;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;
import org.springframework.test.context.ActiveProfiles;

import java.math.BigDecimal;
import java.time.Duration;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@Testcontainers
@ActiveProfiles({"demo", "mongodb"})
class PaymentServiceIntegrationTest {

	@Container
	static PostgreSQLContainer<?> postgres = new PostgreSQLContainer<>(DockerImageName.parse("postgres:15-alpine"));

	@Container
	static RabbitMQContainer rabbitmq = new RabbitMQContainer(DockerImageName.parse("rabbitmq:3-management-alpine"))
		.withCreateContainerCmdModifier(cmd -> cmd.withUser("100:101"))
		.waitingFor(Wait.forSuccessfulCommand("rabbitmq-diagnostics -q check_running"))
		.withStartupTimeout(Duration.ofMinutes(2));

	@Container
	static GenericContainer<?> redis = new GenericContainer<>(DockerImageName.parse("redis:7-alpine"))
		.withExposedPorts(6379);

	@Container
	static MongoDBContainer mongo = new MongoDBContainer(DockerImageName.parse("mongo:7.0.5"));

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
		registry.add("spring.data.redis.host", redis::getHost);
		registry.add("spring.data.redis.port", () -> redis.getMappedPort(6379));
		registry.add("spring.mongodb.uri", mongo::getReplicaSetUrl);
		registry.add("app.jwt.secret", () -> "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
		registry.add("demo.auth.password", () -> "interview-demo-pass");
	}

	@Autowired
	private PaymentRepository paymentRepository;

	@Autowired
	private PaymentAuditRepository paymentAuditRepository;

	@Autowired
	private org.springframework.jdbc.core.JdbcTemplate jdbcTemplate;

	@Autowired
	private JwtUtil jwtUtil;

	@Autowired
	private UserDetailsService userDetailsService;

	@Autowired
	private RabbitTemplate rabbitTemplate;

	@Autowired
	private AmqpAdmin amqpAdmin;

	@org.springframework.boot.test.web.server.LocalServerPort
	private int port;

	private WebTestClient createClient() {
		String token = jwtUtil.generateToken(userDetailsService.loadUserByUsername("staff@flashsale.local"));
		return WebTestClient.bindToServer().baseUrl("http://localhost:" + port)
			.defaultHeader(HttpHeaders.AUTHORIZATION, "Bearer " + token).build();
	}

	private Queue bindPaymentEventQueue(String routingKey) {
		Queue queue = new Queue(
			"payment.completed.integration." + UUID.randomUUID(), false, false, false);
		amqpAdmin.declareQueue(queue);
		amqpAdmin.declareBinding(BindingBuilder.bind(queue)
			.to(new DirectExchange(RabbitConfig.PAYMENT_EVENTS_EXCHANGE))
			.with(routingKey));
		return queue;
	}

	private List<EntityExchangeResult<byte[]>> submitPaymentRequestsConcurrently(
		WebTestClient client, ProcessPaymentRequest first, ProcessPaymentRequest second) throws Exception {
		ExecutorService executor = Executors.newFixedThreadPool(2);
		CountDownLatch ready = new CountDownLatch(2);
		CountDownLatch start = new CountDownLatch(1);

		try {
			CompletableFuture<EntityExchangeResult<byte[]>> firstResult =
				CompletableFuture.supplyAsync(() -> submitPayment(client, first, ready, start), executor);
			CompletableFuture<EntityExchangeResult<byte[]>> secondResult =
				CompletableFuture.supplyAsync(() -> submitPayment(client, second, ready, start), executor);

			assertThat(ready.await(5, TimeUnit.SECONDS)).isTrue();
			start.countDown();
			return List.of(firstResult.get(15, TimeUnit.SECONDS), secondResult.get(15, TimeUnit.SECONDS));
		} finally {
			executor.shutdownNow();
		}
	}

	private EntityExchangeResult<byte[]> submitPayment(WebTestClient client, ProcessPaymentRequest request,
		CountDownLatch ready, CountDownLatch start) {
		ready.countDown();
		try {
			if (!start.await(5, TimeUnit.SECONDS)) {
				throw new IllegalStateException("Timed out waiting to start concurrent payment request");
			}
		} catch (InterruptedException ex) {
			Thread.currentThread().interrupt();
			throw new IllegalStateException("Interrupted while waiting to start concurrent payment request", ex);
		}

		return client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(request)
			.exchange()
			.expectBody(byte[].class)
			.returnResult();
	}

	private long countPayments(String idempotencyKey) {
		return jdbcTemplate.queryForObject(
			"SELECT COUNT(*) FROM payment_records WHERE idempotency_key = ?", Long.class, idempotencyKey);
	}

	private long countOutboxEvents(String idempotencyKey) {
		return jdbcTemplate.queryForObject(
			"SELECT COUNT(*) FROM payment_outbox WHERE idempotency_key = ?", Long.class, idempotencyKey);
	}

	@Test
	void readiness_isHealthy_whenDependenciesAvailable() {
		WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build()
			.get().uri("/actuator/health/readiness")
			.exchange()
			.expectStatus().isOk()
			.expectBody().jsonPath("$.status").isEqualTo("UP");
	}

	@Test
	void validOrderEvent_persistsPaymentAndPublishesCompletion() {
		Queue outputQueue = bindPaymentEventQueue(RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED);
		OrderEventDTO orderEvent = new OrderEventDTO(
			"90301", "customer-1", new BigDecimal("10.01"), "USD", "OrderCreated",
			java.time.OffsetDateTime.now());

		rabbitTemplate.convertAndSend(
			RabbitConfig.ORDER_EVENTS_EXCHANGE, RabbitConfig.ROUTING_KEY_ORDER_CREATED, orderEvent);

		PaymentRecordDto publishedEvent = receivePaymentEvent(outputQueue, "order-90301", 10_000);

		assertThat(publishedEvent).isNotNull();
		assertThat(publishedEvent.idempotencyKey()).isEqualTo("order-90301");
		assertThat(paymentRepository.findByIdempotencyKey("order-90301")).isPresent();
		assertThat(countOutboxEvents("order-90301")).isEqualTo(1);
	}

	@Test
	void paymentOutboxEvent_isProjectedToMongoAndReadableByStaff() throws Exception {
		WebTestClient client = createClient();
		String idempotencyKey = "audit-" + UUID.randomUUID();
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			UUID.randomUUID(), 90310, idempotencyKey, new BigDecimal("10.01"), "USD");

		client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(request)
			.exchange()
			.expectStatus().isOk();

		long deadline = System.nanoTime() + Duration.ofSeconds(15).toNanos();
		while (paymentAuditRepository.findById(idempotencyKey).isEmpty()
			&& System.nanoTime() < deadline) {
			Thread.sleep(100);
		}

		assertThat(paymentAuditRepository.findById(idempotencyKey)).isPresent();
		client.get().uri("/api/audit/payments/{idempotencyKey}", idempotencyKey)
			.exchange()
			.expectStatus().isOk()
			.expectBody()
			.jsonPath("$.idempotencyKey").isEqualTo(idempotencyKey)
			.jsonPath("$.status").isEqualTo("Succeeded");
	}

	@Test
	void duplicateOrderEvent_persistsAndPublishesOnlyOnce() {
		Queue outputQueue = bindPaymentEventQueue(RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED);
		OrderEventDTO orderEvent = new OrderEventDTO(
			"90302", "customer-1", new BigDecimal("10.01"), "USD", "OrderCreated",
			java.time.OffsetDateTime.now());

		rabbitTemplate.convertAndSend(
			RabbitConfig.ORDER_EVENTS_EXCHANGE, RabbitConfig.ROUTING_KEY_ORDER_CREATED, orderEvent);
		rabbitTemplate.convertAndSend(
			RabbitConfig.ORDER_EVENTS_EXCHANGE, RabbitConfig.ROUTING_KEY_ORDER_CREATED, orderEvent);

		PaymentRecordDto firstEvent = receivePaymentEvent(outputQueue, "order-90302", 10_000);
		PaymentRecordDto duplicateEvent = receivePaymentEvent(outputQueue, "order-90302", 500);

		assertThat(firstEvent).isNotNull();
		assertThat(duplicateEvent).isNull();
		assertThat(paymentRepository.findByIdempotencyKey("order-90302")).isPresent();
		assertThat(countOutboxEvents("order-90302")).isEqualTo(1);
	}

	private PaymentRecordDto receivePaymentEvent(Queue queue, String idempotencyKey, long timeoutMillis) {
		long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMillis);
		while (System.nanoTime() < deadline) {
			long remainingMillis = TimeUnit.NANOSECONDS.toMillis(deadline - System.nanoTime());
			Object event = rabbitTemplate.receiveAndConvert(
				queue.getName(), (int) Math.max(1, Math.min(remainingMillis, 250)));
			if (event instanceof PaymentRecordDto payment && idempotencyKey.equals(payment.idempotencyKey())) {
				return payment;
			}
		}
		return null;
	}

	@Test
	void malformedOrderEvent_isDeadLettered() {
		rabbitTemplate.convertAndSend(
			RabbitConfig.ORDER_EVENTS_EXCHANGE, RabbitConfig.ROUTING_KEY_ORDER_CREATED, "not-an-order-event");

		org.springframework.amqp.core.Message deadLetter =
			rabbitTemplate.receive(RabbitConfig.PAYMENT_DLQ, 10_000);

		assertThat(deadLetter).isNotNull();
	}

	@Test
	void jwtForUnknownUser_isUnauthorized() {
		String token = jwtUtil.generateToken(
			User.withUsername("unknown@flashsale.local").password("not-used").roles("STAFF").build());
		WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build()
			.get().uri("/api/payments")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + token)
			.exchange()
			.expectStatus().isUnauthorized();
	}

	@Test
	void login_returnsJwt_forValidStaffUser() {
		WebTestClient client = WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build();
		LoginRequest request = new LoginRequest("staff@flashsale.local", "interview-demo-pass");

		AuthResponse auth = client.post().uri("/api/auth/login")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(request)
			.exchange()
			.expectStatus().isOk()
			.expectBody(AuthResponse.class)
			.returnResult()
			.getResponseBody();

		assertThat(auth).isNotNull();
		assertThat(auth.token()).isNotBlank();
		assertThat(auth.type()).isEqualTo("Bearer");
	}

	@Test
	void paymentApi_requiresJwt_and_processesPaidRequest() {
		WebTestClient client = WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build();
		AuthResponse auth = client.post().uri("/api/auth/login")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(new LoginRequest("staff@flashsale.local", "interview-demo-pass"))
			.exchange()
			.expectStatus().isOk()
			.expectBody(AuthResponse.class)
			.returnResult()
			.getResponseBody();

		ProcessPaymentRequest req = new ProcessPaymentRequest(
			UUID.randomUUID(), 1, "idem-integ-1", new BigDecimal("10.01"), "USD");

		PaymentRecordDto dto = client.post().uri("/api/payments")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + auth.token())
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(req)
			.exchange()
			.expectStatus().isOk()
			.expectBody(PaymentRecordDto.class)
			.returnResult()
			.getResponseBody();

		assertThat(dto).isNotNull();
		assertThat(dto.success()).isTrue();
		assertThat(dto.status()).isEqualTo("Succeeded");
		Payment payment = paymentRepository.findByIdempotencyKey("idem-integ-1").orElseThrow();
		assertThat(payment.getSuccess()).isTrue();
	}

	@Test
	void processPayment_succeeds_whenCentsIsOne() {
		WebTestClient client = createClient();
		ProcessPaymentRequest req = new ProcessPaymentRequest(
			UUID.randomUUID(), 1, "idem-integ-1b", new BigDecimal("10.01"), "USD");

		PaymentRecordDto dto = client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(req)
			.exchange()
			.expectStatus().isOk()
			.expectBody(PaymentRecordDto.class)
			.returnResult()
			.getResponseBody();

		assertThat(dto).isNotNull();
		assertThat(dto.success()).isTrue();
		assertThat(dto.status()).isEqualTo("Succeeded");

		Payment payment = paymentRepository.findByIdempotencyKey("idem-integ-1b").orElseThrow();
		assertThat(payment.getSuccess()).isTrue();
	}

	@Test
	void login_rejectsBadCredentials() {
		WebTestClient client = WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build();
		client.post().uri("/api/auth/login")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(new LoginRequest("staff@flashsale.local", "wrong-password"))
			.exchange()
			.expectStatus().isUnauthorized();
	}

	@Test
	void processPayment_declined_whenCentsIsTwo() {
		WebTestClient client = createClient();
		ProcessPaymentRequest req = new ProcessPaymentRequest(
			UUID.randomUUID(), 2, "idem-integ-2", new BigDecimal("10.02"), "USD");

		PaymentRecordDto dto = client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(req)
			.exchange()
			.expectStatus().isOk()
			.expectBody(PaymentRecordDto.class)
			.returnResult()
			.getResponseBody();

		assertThat(dto).isNotNull();
		assertThat(dto.success()).isFalse();
		assertThat(dto.status()).isEqualTo("Declined");
	}

	@Test
	void processPayment_transientTimeout_whenCentsIsThree() {
		WebTestClient client = createClient();
		ProcessPaymentRequest req = new ProcessPaymentRequest(
			UUID.randomUUID(), 3, "idem-integ-3", new BigDecimal("10.03"), "USD");

		client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(req)
			.exchange()
			.expectStatus().isEqualTo(504);
		assertThat(paymentRepository.findByIdempotencyKey("idem-integ-3")).isPresent();
	}

	@Test
	void processPayment_idempotentReplay_returnsSameResult() {
		WebTestClient client = createClient();
		ProcessPaymentRequest req = new ProcessPaymentRequest(
			UUID.randomUUID(), 4, "idem-replay", new BigDecimal("10.01"), "USD");

		PaymentRecordDto first = client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(req)
			.exchange()
			.expectStatus().isOk()
			.expectBody(PaymentRecordDto.class)
			.returnResult()
			.getResponseBody();

		ProcessPaymentRequest replay = new ProcessPaymentRequest(
			req.paymentId(), 4, "idem-replay", new BigDecimal("10.01"), "USD");

		PaymentRecordDto second = client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(replay)
			.exchange()
			.expectStatus().isOk()
			.expectBody(PaymentRecordDto.class)
			.returnResult()
			.getResponseBody();

		assertThat(second.transactionId()).isEqualTo(first.transactionId());
		assertThat(second.status()).isEqualTo(first.status());
		assertThat(countOutboxEvents("idem-replay")).isEqualTo(1);

		ProcessPaymentRequest changed = new ProcessPaymentRequest(
			req.paymentId(), 4, "idem-replay", new BigDecimal("99.99"), "USD");
		client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(changed)
			.exchange()
			.expectStatus().isEqualTo(409);
	}

	@Test
	void concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed() throws Exception {
		WebTestClient client = createClient();
		String idempotencyKey = "idem-concurrent-same-" + UUID.randomUUID();
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			UUID.randomUUID(), 91, idempotencyKey, new BigDecimal("10.01"), "USD");

		List<EntityExchangeResult<byte[]>> results =
			submitPaymentRequestsConcurrently(client, request, request);

		assertThat(results).extracting(EntityExchangeResult::getStatus)
			.containsExactlyInAnyOrder(org.springframework.http.HttpStatus.OK, org.springframework.http.HttpStatus.OK);
		assertThat(countPayments(idempotencyKey)).isEqualTo(1);
		assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
	}

	@Test
	void concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment() throws Exception {
		WebTestClient client = createClient();
		String idempotencyKey = "idem-concurrent-diff-" + UUID.randomUUID();
		UUID paymentId = UUID.randomUUID();
		ProcessPaymentRequest first = new ProcessPaymentRequest(
			paymentId, 92, idempotencyKey, new BigDecimal("10.01"), "USD");
		ProcessPaymentRequest changed = new ProcessPaymentRequest(
			paymentId, 92, idempotencyKey, new BigDecimal("99.99"), "USD");

		List<EntityExchangeResult<byte[]>> results =
			submitPaymentRequestsConcurrently(client, first, changed);

		assertThat(results).extracting(EntityExchangeResult::getStatus)
			.containsExactlyInAnyOrder(org.springframework.http.HttpStatus.OK, org.springframework.http.HttpStatus.CONFLICT);
		assertThat(countPayments(idempotencyKey)).isEqualTo(1);
	}

	@Test
	void healthz_returnsHealthy() {
		WebTestClient client = createClient();
		client.get().uri("/healthz")
			.exchange()
			.expectStatus().isOk()
			.expectBody().jsonPath("$.status").isEqualTo("healthy");
	}
}
