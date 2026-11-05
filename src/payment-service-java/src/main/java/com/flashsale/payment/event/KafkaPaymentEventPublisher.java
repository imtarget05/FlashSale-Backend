package com.flashsale.payment.event;

import com.flashsale.payment.config.KafkaConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.kafka.support.KafkaHeaders;
import org.springframework.messaging.Message;
import org.springframework.messaging.support.MessageBuilder;
import org.springframework.stereotype.Component;

@Component
public class KafkaPaymentEventPublisher {

	private final KafkaTemplate<String, Object> kafkaTemplate;

	public KafkaPaymentEventPublisher(KafkaTemplate<String, Object> kafkaTemplate) {
		this.kafkaTemplate = kafkaTemplate;
	}

	public void publishPaymentCompleted(PaymentRecordDto payment) {
		send(KafkaConfig.PAYMENT_EVENTS_TOPIC, payment.idempotencyKey(), payment);
	}

	public void publishPaymentFailed(PaymentRecordDto payment) {
		send(KafkaConfig.PAYMENT_EVENTS_TOPIC, payment.idempotencyKey(), payment);
	}

	private void send(String topic, String key, Object payload) {
		Message<Object> message = MessageBuilder.withPayload(payload)
			.setHeader(KafkaHeaders.TOPIC, topic)
			.setHeader(KafkaHeaders.KEY, key)
			.build();
		kafkaTemplate.send(message);
	}
}
