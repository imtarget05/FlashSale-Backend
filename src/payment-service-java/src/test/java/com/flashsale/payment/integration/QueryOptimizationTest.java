package com.flashsale.payment.integration;

import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.repository.PaymentRepository;
import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.UUID;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.utility.DockerImageName;

import static org.assertj.core.api.Assertions.assertThat;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.MOCK)
@Testcontainers
class QueryOptimizationTest {

	private static final int ROW_COUNT = 1_000;
	private static final String TARGET_KEY = "qo-target-key";

	@Container
	static PostgreSQLContainer<?> postgres = new PostgreSQLContainer<>(DockerImageName.parse("postgres:15-alpine"));

	@Autowired
	private JdbcTemplate jdbcTemplate;

	@Autowired
	private PaymentRepository paymentRepository;

	@DynamicPropertySource
	static void properties(DynamicPropertyRegistry registry) {
		registry.add("spring.datasource.url", postgres::getJdbcUrl);
		registry.add("spring.datasource.username", postgres::getUsername);
		registry.add("spring.datasource.password", postgres::getPassword);
		registry.add("spring.rabbitmq.listener.simple.auto-startup", () -> "false");
		registry.add("payment.outbox.poll-interval-ms", () -> "600000");
		registry.add("app.jwt.secret", () -> "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
	}

	@BeforeEach
	void loadDataset() {
		jdbcTemplate.execute("DELETE FROM payment_records");
		OffsetDateTime now = OffsetDateTime.now();
		var payments = new java.util.ArrayList<Payment>(ROW_COUNT);
		for (int i = 0; i < ROW_COUNT; i++) {
			String key = "qo-pay-" + String.format("%05d", i);
			payments.add(new Payment(key, UUID.randomUUID(), i + 1, new BigDecimal("10.01"),
				"USD", true, "Succeeded", "tx_" + key, null, false, now));
		}
		paymentRepository.saveAll(payments);
		paymentRepository.flush();
		Payment target = new Payment(TARGET_KEY, UUID.randomUUID(), 99_999, new BigDecimal("25.00"),
			"USD", true, "Succeeded", "tx-target", null, false, now);
		paymentRepository.saveAndFlush(target);
		assertThat(paymentRepository.count()).isEqualTo(ROW_COUNT + 1);
	}

	@Test
	void findByIdempotencyKey_usesPrimaryKeyIndex() throws Exception {
		String sql = "EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM payment_records WHERE idempotency_key = '" + TARGET_KEY + "'";
		List<String> lines = jdbcTemplate.queryForList(sql, String.class);
		String plan = String.join("\n", lines);
		recordPlan("findByIdempotencyKey", plan);
		assertThat(plan).contains("Index Scan");
		assertThat(plan).contains("payment_records_pkey");
	}

	@Test
	void listPayments_usesIndexScanOrderedByPrimaryKey() throws Exception {
		String sql = "EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM payment_records ORDER BY idempotency_key LIMIT 20 OFFSET 0";
		List<String> lines = jdbcTemplate.queryForList(sql, String.class);
		String plan = String.join("\n", lines);
		recordPlan("listPayments", plan);
		assertThat(plan).contains("Index Scan");
		assertThat(plan).contains("payment_records_pkey");
	}

	private void recordPlan(String query, String plan) throws Exception {
		Path dir = Path.of("target");
		Files.createDirectories(dir);
		Files.writeString(dir.resolve("query-plan-actual.txt"),
			"=== " + query + " ===\n" + plan + "\n\n", StandardOpenOption.CREATE, StandardOpenOption.APPEND);
	}
}
