package com.flashsale.payment.unit;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.event.OrderEventConsumer;
import com.flashsale.payment.event.OrderEventDTO;
import com.flashsale.payment.service.PaymentService;
import com.rabbitmq.client.Channel;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.core.MessageProperties;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import java.math.BigDecimal;
import java.time.OffsetDateTime;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

@ExtendWith(MockitoExtension.class)
class OrderEventConsumerTest {

	@Mock
	private PaymentService paymentService;

	@Mock
	private Channel channel;

	@Test
	void paymentPersistenceFailure_nacksDeliveryInsteadOfAcknowledgingIt() throws Exception {
		ObjectMapper objectMapper = new ObjectMapper().findAndRegisterModules();
		OrderEventConsumer consumer = new OrderEventConsumer(paymentService, objectMapper);
		OrderEventDTO orderEvent = new OrderEventDTO(
			"90303", "customer-1", new BigDecimal("10.01"), "USD", "OrderCreated",
			OffsetDateTime.now());
		Message message = new Message(objectMapper.writeValueAsBytes(orderEvent), new MessageProperties());
		when(paymentService.processPayment(any(ProcessPaymentRequest.class)))
			.thenThrow(new IllegalStateException("database unavailable"));

		consumer.consumeOrderCreated(message, channel, 77L);

		verify(channel).basicNack(77L, false, false);
		verify(channel, never()).basicAck(77L, false);
	}
}
