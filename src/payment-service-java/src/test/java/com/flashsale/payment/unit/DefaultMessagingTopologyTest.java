package com.flashsale.payment.unit;

import org.junit.jupiter.api.Test;
import org.springframework.util.ClassUtils;

import static org.assertj.core.api.Assertions.assertThat;

class DefaultMessagingTopologyTest {

	private final ClassLoader classLoader = getClass().getClassLoader();

	@Test
	void defaultRuntimeContainsRabbitMqButNoKafkaPath() {
		assertThat(ClassUtils.isPresent("org.springframework.amqp.rabbit.core.RabbitTemplate", classLoader)).isTrue();
		assertThat(ClassUtils.isPresent("com.flashsale.payment.event.OrderEventConsumer", classLoader)).isTrue();
		assertThat(ClassUtils.isPresent("org.springframework.kafka.core.KafkaTemplate", classLoader)).isFalse();
		assertThat(ClassUtils.isPresent("com.flashsale.payment.event.KafkaOrderEventConsumer", classLoader)).isFalse();
	}
}
