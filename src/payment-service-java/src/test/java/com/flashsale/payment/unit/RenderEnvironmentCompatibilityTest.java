package com.flashsale.payment.unit;

import static org.assertj.core.api.Assertions.assertThat;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

class RenderEnvironmentCompatibilityTest {

	@Test
	void applicationPropertiesSupportRenderEnvVariableNames() throws IOException {
		String config = Files.readString(Path.of("src/main/resources/application.properties"));

		assertThat(config).contains("spring.datasource.username=${SPRING_DATASOURCE_USERNAME:");
		assertThat(config).contains("spring.datasource.password=${SPRING_DATASOURCE_PASSWORD:");
		assertThat(config).contains("spring.rabbitmq.host=${SPRING_RABBITMQ_HOST:");
		assertThat(config).contains("spring.rabbitmq.username=${SPRING_RABBITMQ_USERNAME:");
		assertThat(config).contains("spring.rabbitmq.password=${SPRING_RABBITMQ_PASSWORD:");
		assertThat(config).contains("spring.data.redis.host=${SPRING_DATA_REDIS_HOST:");
		assertThat(config).contains("spring.data.redis.password=${SPRING_DATA_REDIS_PASSWORD:");
	}
}
