package com.flashsale.payment.integration;

import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.context.ActiveProfiles;
import org.testcontainers.containers.PostgreSQLContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;

@Testcontainers
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.NONE)
@ActiveProfiles("dev")
class ContextLoadsIntegrationTest {

	@Container
	static PostgreSQLContainer<?> postgres = new PostgreSQLContainer<>("postgres:15-alpine");

	@DynamicPropertySource
	static void properties(DynamicPropertyRegistry registry) {
		registry.add("spring.datasource.url", postgres::getJdbcUrl);
		registry.add("spring.datasource.username", postgres::getUsername);
		registry.add("spring.datasource.password", postgres::getPassword);
		registry.add("spring.sql.init.mode", () -> "always");
		registry.add("spring.sql.init.schema-locations", () -> "file:docker/init-schema.sql");
		registry.add("app.jwt.secret", () -> "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
		registry.add("app.kafka.listener.auto-startup", () -> "false");
	}

	@Test
	void contextLoads() {
		// Just verifies the application context starts with Testcontainers
	}
}
