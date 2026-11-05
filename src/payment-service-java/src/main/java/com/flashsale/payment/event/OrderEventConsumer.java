package com.flashsale.payment.event;

import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.service.PaymentService;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.rabbitmq.client.Channel;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.amqp.support.AmqpHeaders;
import org.springframework.messaging.handler.annotation.Header;
import org.springframework.stereotype.Component;

import java.io.IOException;
import java.util.UUID;

@Component
public class OrderEventConsumer {

	private static final Logger log = LoggerFactory.getLogger(OrderEventConsumer.class);

	private final PaymentService paymentService;
	private final ObjectMapper objectMapper;

	public OrderEventConsumer(PaymentService paymentService, ObjectMapper objectMapper) {
		this.paymentService = paymentService;
		this.objectMapper = objectMapper;
	}

	@RabbitListener(queues = RabbitConfig.PAYMENT_QUEUE, ackMode = "MANUAL")
	public void consumeOrderCreated(Message orderEvent, Channel channel,
		@Header(AmqpHeaders.DELIVERY_TAG) long deliveryTag) {
		OrderEventDTO dto;
		try {
			dto = objectMapper.readValue(orderEvent.getBody(), OrderEventDTO.class);
		} catch (IOException ex) {
			log.error("[payment-service] failed to parse order event: {}", orderEvent, ex);
			nack(channel, deliveryTag);
			return;
		}

		String idempotencyKey = "order-" + dto.orderId();
		log.info("[payment-service] received order event orderId={} idempotencyKey={} eventType={}",
			dto.orderId(), idempotencyKey, dto.eventType());

		try {
			ProcessPaymentRequest request = new ProcessPaymentRequest(
				UUID.randomUUID(), Integer.valueOf(dto.orderId()), idempotencyKey,
				dto.amount(), dto.currency());

			PaymentRecordDto result = paymentService.processPayment(request);

			ack(channel, deliveryTag);
			log.info("[payment-service] order processed orderId={} status={}", dto.orderId(), result.status());
		} catch (Exception ex) {
			log.error("[payment-service] failed to process order orderId={} idempotencyKey={}", dto.orderId(),
				idempotencyKey, ex);
			nack(channel, deliveryTag);
		}
	}

	private void ack(Channel channel, long deliveryTag) {
		try {
			channel.basicAck(deliveryTag, false);
		} catch (IOException ex) {
			log.warn("[payment-service] failed to ack delivery tag {}", deliveryTag, ex);
		}
	}

	private void nack(Channel channel, long deliveryTag) {
		try {
			channel.basicNack(deliveryTag, false, false);
		} catch (IOException ex) {
			log.warn("[payment-service] failed to nack delivery tag {}", deliveryTag, ex);
		}
	}

}
