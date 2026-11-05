package com.flashsale.payment.event;

import com.flashsale.payment.config.KafkaConfig;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

@Component
public class KafkaOrderEventConsumer {

	@KafkaListener(topics = KafkaConfig.ORDER_EVENTS_TOPIC, groupId = KafkaConfig.CONSUMER_GROUP)
	public void consumeOrderCreated(String message) {
		// Consume order.created event from Kafka
		// Idempotency is handled at the payment level via idempotency key
		System.out.println("Kafka received order event: " + message);
	}
}
