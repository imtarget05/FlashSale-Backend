package com.flashsale.payment.security;

import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;
import jakarta.servlet.http.HttpServletResponse;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.web.reactive.server.WebTestClient;
import org.testcontainers.containers.GenericContainer;
import org.testcontainers.containers.MongoDBContainer;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.containers.RabbitMQContainer;
import org.testcontainers.containers.wait.strategy.Wait;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;

import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Date;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@Testcontainers
@ActiveProfiles({"demo", "mongodb"})
class AuthSecurityIntegrationTest {

	private static final String JWT_SECRET = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
		registry.add("spring.rabbitmq.host", rabbitmq::getHost);
		registry.add("spring.rabbitmq.port", rabbitmq::getAmqpPort);
		registry.add("spring.rabbitmq.username", () -> "guest");
		registry.add("spring.rabbitmq.password", () -> "guest");
		registry.add("spring.data.redis.host", redis::getHost);
		registry.add("spring.data.redis.port", () -> redis.getMappedPort(6379));
		registry.add("spring.mongodb.uri", mongo::getReplicaSetUrl);
		registry.add("app.jwt.secret", () -> JWT_SECRET);
		registry.add("demo.auth.password", () -> "interview-demo-pass");
	}

	@Autowired
	private JwtUtil jwtUtil;

	@Autowired
	private UserDetailsService userDetailsService;

	@org.springframework.boot.test.web.server.LocalServerPort
	private int port;

	private WebTestClient client() {
		return WebTestClient.bindToServer().baseUrl("http://localhost:" + port).build();
	}

	private String tokenFor(String username) {
		UserDetails user = userDetailsService.loadUserByUsername(username);
		return jwtUtil.generateToken(user);
	}

	@Test
	void login_withValidCustomerCredentials_returnsJwt() {
		client().post().uri("/api/auth/login")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(new LoginRequest("customer@flashsale.local", "interview-demo-pass"))
			.exchange()
			.expectStatus().isOk()
			.expectBody(String.class)
			.value(body -> assertThat(body).contains("token"));
	}

	@Test
	void login_withInvalidCredentials_returns401() {
		client().post().uri("/api/auth/login")
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(new LoginRequest("customer@flashsale.local", "wrong-password"))
			.exchange()
			.expectStatus().isUnauthorized();
	}

	@Test
	void customerRolePostPayment_returns403() {
		String token = tokenFor("customer@flashsale.local");
		assertThat(jwtUtil.isTokenValid(
			token, userDetailsService.loadUserByUsername("customer@flashsale.local"))).isTrue();
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			UUID.randomUUID(), 1, "auth-403-" + UUID.randomUUID(), new BigDecimal("10.01"), "USD");

		client().post().uri("/api/payments")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + token)
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(request)
			.exchange()
			.expectStatus().isEqualTo(HttpServletResponse.SC_FORBIDDEN);
	}

	@Test
	void staffCanPostPayment_returns200() {
		String token = tokenFor("staff@flashsale.local");
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			UUID.randomUUID(), 2, "auth-ok-" + UUID.randomUUID(), new BigDecimal("10.01"), "USD");

		client().post().uri("/api/payments")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + token)
			.contentType(MediaType.APPLICATION_JSON)
			.bodyValue(request)
			.exchange()
			.expectStatus().isOk();
	}

	@Test
	void expiredJwt_returns401() {
		Date now = new Date();
		String expired = Jwts.builder()
			.subject("customer@flashsale.local")
			.issuedAt(new Date(now.getTime() - 200_000))
			.expiration(new Date(now.getTime() - 100_000))
			.signWith(Keys.hmacShaKeyFor(JWT_SECRET.getBytes(StandardCharsets.UTF_8)))
			.compact();

		client().get().uri("/api/payments/any-key")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + expired)
			.exchange()
			.expectStatus().isEqualTo(HttpServletResponse.SC_UNAUTHORIZED);
	}

	@Test
	void tamperedJwt_returns401() {
		String valid = tokenFor("staff@flashsale.local");
		String tampered = valid.substring(0, valid.length() - 6) + "XXXXXX";

		client().get().uri("/api/payments/any-key")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + tampered)
			.exchange()
			.expectStatus().isEqualTo(HttpServletResponse.SC_UNAUTHORIZED);
	}

	@Test
	void missingAuthorization_returns401() {
		client().get().uri("/api/payments/any-key")
			.exchange()
			.expectStatus().isEqualTo(HttpServletResponse.SC_UNAUTHORIZED);
	}

	@Test
	void authenticatedMissingRoute_returns404() {
		client().get().uri("/api/does-not-exist")
			.header(HttpHeaders.AUTHORIZATION, "Bearer " + tokenFor("staff@flashsale.local"))
			.exchange()
			.expectStatus().isNotFound();
	}
}
