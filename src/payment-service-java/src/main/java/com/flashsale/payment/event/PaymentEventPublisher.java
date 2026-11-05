package com.flashsale.payment.event;

import com.flashsale.payment.dto.PaymentRecordDto;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.stereotype.Component;

@Component
public class PaymentEventPublisher {

	private final RabbitTemplate rabbitTemplate;

	public PaymentEventPublisher(RabbitTemplate rabbitTemplate) {
		this.rabbitTemplate = rabbitTemplate;
	}

	public void publishPaymentCompleted(PaymentRecordDto payment) {
		rabbitTemplate.convertAndSend(
			com.flashsale.payment.config.RabbitConfig.PAYMENT_EVENTS_EXCHANGE,
			com.flashsale.payment.config.RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED,
			payment);
	}

	public void publishPaymentFailed(PaymentRecordDto payment) {
		rabbitTemplate.convertAndSend(
			com.flashsale.payment.config.RabbitConfig.PAYMENT_EVENTS_EXCHANGE,
			com.flashsale.payment.config.RabbitConfig.ROUTING_KEY_PAYMENT_FAILED,
			payment);
	}
}
