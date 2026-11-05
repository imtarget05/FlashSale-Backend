package com.flashsale.payment.event;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.outbox.PaymentOutbox;
import org.springframework.amqp.AmqpException;
import org.springframework.amqp.core.ReturnedMessage;
import org.springframework.amqp.rabbit.connection.CorrelationData;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.stereotype.Component;

import java.io.IOException;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

@Component
public class PaymentEventPublisher {

	private static final long CONFIRM_TIMEOUT_SECONDS = 5;

	private final RabbitTemplate rabbitTemplate;
	private final ObjectMapper objectMapper;

	public PaymentEventPublisher(RabbitTemplate rabbitTemplate, ObjectMapper objectMapper) {
		this.rabbitTemplate = rabbitTemplate;
		this.objectMapper = objectMapper;
	}

	public void publish(PaymentOutbox event) {
		PaymentRecordDto payload;
		try {
			payload = objectMapper.readValue(event.getPayload(), PaymentRecordDto.class);
		} catch (IOException ex) {
			throw new AmqpException("Invalid payment outbox payload for key " + event.getIdempotencyKey(), ex);
		}

		CorrelationData correlation = new CorrelationData(event.getIdempotencyKey());
		rabbitTemplate.convertAndSend(
			RabbitConfig.PAYMENT_EVENTS_EXCHANGE, event.getRoutingKey(), payload, correlation);

		try {
			CorrelationData.Confirm confirm =
				correlation.getFuture().get(CONFIRM_TIMEOUT_SECONDS, TimeUnit.SECONDS);
			if (!confirm.ack()) {
				throw new AmqpException("RabbitMQ rejected payment event: " + confirm.reason());
			}

			ReturnedMessage returned = correlation.getReturned();
			if (returned != null) {
				throw new AmqpException("RabbitMQ could not route payment event: " + returned.getReplyText());
			}
		} catch (InterruptedException ex) {
			Thread.currentThread().interrupt();
			throw new AmqpException("Interrupted while waiting for RabbitMQ publisher confirm", ex);
		} catch (ExecutionException | TimeoutException ex) {
			throw new AmqpException("Failed to confirm payment event publish", ex);
		}
	}
}
