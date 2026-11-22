package com.flashsale.payment.integration;

import org.junit.jupiter.api.Test;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.event.OrderEventDTO;
import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.outbox.PaymentOutboxRelay;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.security.JwtUtil;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.context.TestPropertySource;
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
import java.time.Duration;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.http.HttpStatus.GATEWAY_TIMEOUT;
import static org.springframework.http.HttpStatus.OK;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@Testcontainers
@ActiveProfiles({"demo", "mongodb"})
@TestPropertySource(properties = "management.endpoint.health.show-details=always")
class IdempotencyReliabilityIntegrationTest {

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
        registry.add("app.jwt.secret", () -> "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        registry.add("demo.auth.password", () -> "interview-demo-pass");
    }
    @Autowired
    private PaymentRepository paymentRepository;

    @Autowired
    private JdbcTemplate jdbcTemplate;

    @Autowired
    private RabbitTemplate rabbitTemplate;

    @Autowired
    private PaymentOutboxRelay outboxRelay;

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

    private ProcessPaymentRequest timeoutRequest(String idempotencyKey) {
        // cents .03 => SimulatedPaymentProvider ghi trạng thái TimedOut (transient)
        return new ProcessPaymentRequest(UUID.randomUUID(), 71, idempotencyKey, new BigDecimal("10.03"), "USD");
    }

    private ProcessPaymentRequest successRequest(String idempotencyKey) {
        return new ProcessPaymentRequest(UUID.randomUUID(), 72, idempotencyKey, new BigDecimal("10.01"), "USD");
    }

    private int postStatus(WebTestClient client, ProcessPaymentRequest request) {
        return client.post().uri("/api/payments")
                .contentType(MediaType.APPLICATION_JSON)
                .bodyValue(request)
                .exchange()
                .expectBody(byte[].class)
                .returnResult()
                .getStatus()
                .value();
    }

    private long countPayments(String idempotencyKey) {
        return jdbcTemplate.queryForObject(
                "SELECT COUNT(*) FROM payment_records WHERE idempotency_key = ?", Long.class, idempotencyKey);
    }

    private long countOutboxEvents(String idempotencyKey) {
        return jdbcTemplate.queryForObject(
                "SELECT COUNT(*) FROM payment_outbox WHERE idempotency_key = ?", Long.class, idempotencyKey);
    }

    private OffsetDateTime outboxPublishedAt(String idempotencyKey) {
        return jdbcTemplate.queryForObject(
                "SELECT published_at FROM payment_outbox WHERE idempotency_key = ?",
                (rs, rowNum) -> rs.getObject("published_at", OffsetDateTime.class), idempotencyKey);
    }


    private void waitForPaymentCount(String idempotencyKey, long expected) throws InterruptedException {
        long deadline = System.currentTimeMillis() + TimeUnit.SECONDS.toMillis(15);
        while (System.currentTimeMillis() < deadline) {
            if (countPayments(idempotencyKey) == expected) {
                return;
            }
            Thread.sleep(300);
        }
        throw new AssertionError("Timed out waiting for payment count " + expected + " for key " + idempotencyKey);
    }

    @Test
    void retrySameKeyAfterTransientTimeout_returnsRecordedResultWithoutDuplicate() throws Exception {
        WebTestClient client = createClient();
        String idempotencyKey = "idem-retry-" + UUID.randomUUID();
        ProcessPaymentRequest request = timeoutRequest(idempotencyKey);

        // Lần gọi đầu: gateway timeout -> HTTP 504, nhưng DB vẫn commit payment record + outbox event
        // (@Transactional(noRollbackFor = PaymentTransientException.class)).
        assertThat(postStatus(client, request)).isEqualTo(GATEWAY_TIMEOUT.value());
        assertThat(paymentRepository.findByIdempotencyKey(idempotencyKey)).isPresent();
        assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
        assertThat(outboxPublishedAt(idempotencyKey)).isNull();

        // Retry CÙNG KEY + CÙNG payload: service đọc bản ghi đã commit, trả về kết quả đã lưu
        // (giai đoạn timeout vẫn là 504 vì kết quả đã ghi là TimedOut), KHÔNG tạo thêm payment/outbox.
        PaymentRecordDto replay = client.post().uri("/api/payments")
                .contentType(MediaType.APPLICATION_JSON)
                .bodyValue(request)
                .exchange()
                .expectStatus().isEqualTo(GATEWAY_TIMEOUT)
                .expectBody(PaymentRecordDto.class)
                .returnResult()
                .getResponseBody();
        assertThat(replay).isNotNull();
        assertThat(replay.status()).isEqualTo("TimedOut");
        assertThat(countPayments(idempotencyKey)).isEqualTo(1);
        assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
    }

    @Test
    void outboxEventPersistedAfterDbCommitAndPublishedExactlyOnce() throws Exception {
        WebTestClient client = createClient();
        String idempotencyKey = "idem-outbox-" + UUID.randomUUID();
        ProcessPaymentRequest request = timeoutRequest(idempotencyKey);

        assertThat(postStatus(client, request)).isEqualTo(GATEWAY_TIMEOUT.value());
        assertThat(countPayments(idempotencyKey)).isEqualTo(1);
        assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
        assertThat(outboxPublishedAt(idempotencyKey)).isNull(); // chưa publish ngay sau khi commit DB

        outboxRelay.publishPendingEvents();
        OffsetDateTime publishedAt = waitForOutboxPublished(idempotencyKey, 10);
        assertThat(publishedAt).isNotNull(); // relay đã publish và publisher confirm thành công

        // Gọi lại relay: không đánh dấu lại và không tạo bản ghi outbox trùng lặp
        OffsetDateTime firstPublishedAt = outboxPublishedAt(idempotencyKey);
        outboxRelay.publishPendingEvents();
        assertThat(outboxPublishedAt(idempotencyKey)).isEqualTo(firstPublishedAt);
        assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
    }

    /**
     * Đợi cho đến khi outbox event được đánh dấu published_at (relay đã publish
     * xong và nhận publisher confirm từ RabbitMQ), tối đa timeoutSeconds giây.
     */
    private OffsetDateTime waitForOutboxPublished(String idempotencyKey, long timeoutSeconds)
            throws InterruptedException {
        long deadline = System.currentTimeMillis() + TimeUnit.SECONDS.toMillis(timeoutSeconds);
        while (System.currentTimeMillis() < deadline) {
            OffsetDateTime publishedAt = outboxPublishedAt(idempotencyKey);
            if (publishedAt != null) {
                return publishedAt;
            }
            Thread.sleep(300);
        }
        return null;
    }

    @Test
    void consumerReceivesDuplicateOrderEvent_processedOnce() throws Exception {
        WebTestClient client = createClient();
        // orderId phải là số vì OrderEventConsumer chuyển thành Integer.
        String orderId = String.valueOf(50000 + (int) (System.nanoTime() % 90000));
        String idempotencyKey = "order-" + orderId;
        OrderEventDTO event = new OrderEventDTO(
                orderId, "customer-1", new BigDecimal("10.01"), "USD", "ORDER_CREATED",
                java.time.OffsetDateTime.now());

        // Gửi cùng một sự kiện đơn hàng hai lần (consumer nhận sự kiện lặp)
        rabbitTemplate.convertAndSend(RabbitConfig.ORDER_EVENTS_EXCHANGE,
                RabbitConfig.ROUTING_KEY_ORDER_CREATED, event);
        rabbitTemplate.convertAndSend(RabbitConfig.ORDER_EVENTS_EXCHANGE,
                RabbitConfig.ROUTING_KEY_ORDER_CREATED, event);

        waitForPaymentCount(idempotencyKey, 1);
        assertThat(countPayments(idempotencyKey)).isEqualTo(1);
        assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
    }

    @Test
    void tenConcurrentRequestsSameIdempotencyKey_onePaymentAndOneOutboxEvent() throws Exception {
        WebTestClient client = createClient();
        String idempotencyKey = "idem-concurrent-10-" + UUID.randomUUID();
        ProcessPaymentRequest request = successRequest(idempotencyKey);

        int threads = 10;
        ExecutorService executor = Executors.newFixedThreadPool(threads);
        CountDownLatch ready = new CountDownLatch(threads);
        CountDownLatch start = new CountDownLatch(1);
        List<CompletableFuture<Integer>> futures = new ArrayList<>();
        for (int i = 0; i < threads; i++) {
            futures.add(CompletableFuture.supplyAsync(() -> {
                ready.countDown();
                try {
                    start.await(5, TimeUnit.SECONDS);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    return -1;
                }
                return postStatus(client, request);
            }, executor));
        }
        assertThat(ready.await(5, TimeUnit.SECONDS)).isTrue();
        start.countDown();
        List<Integer> statuses = new ArrayList<>();
        for (CompletableFuture<Integer> f : futures) {
            statuses.add(f.get(15, TimeUnit.SECONDS));
        }
        executor.shutdownNow();

        assertThat(statuses).allMatch(s -> s == OK.value());
        assertThat(countPayments(idempotencyKey)).isEqualTo(1);
        assertThat(countOutboxEvents(idempotencyKey)).isEqualTo(1);
    }
}

