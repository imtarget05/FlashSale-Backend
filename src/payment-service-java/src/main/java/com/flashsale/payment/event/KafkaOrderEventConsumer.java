package com.flashsale.payment.event;

import com.flashsale.payment.config.KafkaConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.service.PaymentService;
import com.fasterxml.jackson.databind.ObjectMapper;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.kafka.core.KafkaTemplate;
import org.springframework.kafka.support.Acknowledgment;
import org.springframework.stereotype.Component;

import java.util.UUID;

@Component
public class KafkaOrderEventConsumer {

	private static final Logger log = LoggerFactory.getLogger(KafkaOrderEventConsumer.class);

	private final PaymentService paymentService;
	private final PaymentEventPublisher paymentEventPublisher;
	private final PaymentRepository paymentRepository;
	private final KafkaTemplate<String, Object> kafkaTemplate;
	private final ObjectMapper objectMapper;

	public KafkaOrderEventConsumer(PaymentService paymentService,
		PaymentEventPublisher paymentEventPublisher,
		PaymentRepository paymentRepository,
		KafkaTemplate<String, Object> kafkaTemplate,
		ObjectMapper objectMapper) {
		this.paymentService = paymentService;
		this.paymentEventPublisher = paymentEventPublisher;
		this.paymentRepository = paymentRepository;
		this.kafkaTemplate = kafkaTemplate;
		this.objectMapper = objectMapper;
	}

	@KafkaListener(topics = KafkaConfig.ORDER_EVENTS_TOPIC, groupId = KafkaConfig.CONSUMER_GROUP,
		containerFactory = "paymentKafkaListenerFactory")
	public void consumeOrderCreated(String message, Acknowledgment acknowledgment) {
		OrderEventDTO dto;
		try {
			dto = objectMapper.readValue(message, OrderEventDTO.class);
		} catch (Exception ex) {
			log.error("[payment-service] failed to parse kafka order message: {}", message, ex);
			acknowledgment.acknowledge();
			sendToDlq(message, "parse_error");
			return;
		}

		String idempotencyKey = "order-" + dto.orderId();
		log.info("[payment-service] received kafka order event orderId={} idempotencyKey={} eventType={}",
			dto.orderId(), idempotencyKey, dto.eventType());

		try {
			if (paymentRepository.existsByIdempotencyKey(idempotencyKey)) {
				log.info("[payment-service] order already processed, skipping orderId={}", dto.orderId());
				acknowledgment.acknowledge();
				return;
			}

			ProcessPaymentRequest request = new ProcessPaymentRequest(
				UUID.randomUUID(), Integer.valueOf(dto.orderId()), idempotencyKey,
				dto.amount(), dto.currency());

			PaymentRecordDto result = paymentService.processPayment(request);

			if (result.success()) {
				paymentEventPublisher.publishPaymentCompleted(result);
			} else {
				paymentEventPublisher.publishPaymentFailed(result);
			}

			acknowledgment.acknowledge();
			log.info("[payment-service] kafka order processed orderId={} status={}", dto.orderId(), result.status());
		} catch (Exception ex) {
			log.error("[payment-service] failed to process kafka order orderId={} idempotencyKey={}", dto.orderId(),
				idempotencyKey, ex);
			sendToDlq(message, ex.getMessage());
			acknowledgment.acknowledge();
		}
	}

	private void sendToDlq(String message, String reason) {
		try {
			kafkaTemplate.execute((producer) -> {
				org.apache.kafka.clients.producer.ProducerRecord<String, Object> record =
					new org.apache.kafka.clients.producer.ProducerRecord<>(
						KafkaConfig.PAYMENT_DLQ_TOPIC, reason, message);
				producer.send(record);
				return null;
			});
		} catch (Exception ex) {
			log.warn("[payment-service] failed to send to dlq", ex);
		}
	}

}
