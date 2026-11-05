package com.flashsale.payment.integration;

import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.security.JwtUtil;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.MediaType;
import org.springframework.http.HttpHeaders;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.web.reactive.server.WebTestClient;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.containers.RabbitMQContainer;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;
import org.springframework.test.context.ActiveProfiles;

import java.math.BigDecimal;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@Testcontainers
@ActiveProfiles("dev")
class PaymentServiceIntegrationTest {

	@Container
	static PostgreSQLContainer<?> postgres = new PostgreSQLContainer<>(DockerImageName.parse("postgres:15-alpine"));

	@Container
	static RabbitMQContainer rabbitmq = new RabbitMQContainer(DockerImageName.parse("rabbitmq:3-management-alpine"));

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
		registry.add("spring.data.redis.host", redis::getHost);
		registry.add("spring.data.redis.port", () -> redis.getMappedPort(6379));
		registry.add("app.jwt.secret", () -> "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
		registry.add("app.kafka.listener.auto-startup", () -> "false");
	}

	@Autowired
	private PaymentRepository paymentRepository;

	@Autowired
	private JwtUtil jwtUtil;

	@Autowired
	private UserDetailsService userDetailsService;

	@org.springframework.boot.test.web.server.LocalServerPort
	private int port;

	private WebTestClient createClient() {
		String token = jwtUtil.generateToken(userDetailsService.loadUserByUsername("staff@flashsale.local"));
		return WebTestClient.bindToServer().baseUrl("http://localhost:" + port)
			.defaultHeader(HttpHeaders.AUTHORIZATION, "Bearer " + token).build();
	}

	@Test
	void login_returnsJwt_forValidStaffUser() {
		WebTestClient client = WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build();
		LoginRequest request = new LoginRequest("staff@flashsale.local", "password");

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
			.bodyValue(new LoginRequest("staff@flashsale.local", "password"))
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

		ProcessPaymentRequest changed = new ProcessPaymentRequest(
			req.paymentId(), 4, "idem-replay", new BigDecimal("99.99"), "USD");
		client.post().uri("/api/payments")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(changed)
			.exchange()
			.expectStatus().isEqualTo(409);
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
