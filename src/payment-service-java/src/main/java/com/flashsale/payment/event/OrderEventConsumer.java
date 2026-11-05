package com.flashsale.payment.event;

import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.service.PaymentService;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.stereotype.Component;

@Component
public class OrderEventConsumer {

	private final PaymentService paymentService;

	public OrderEventConsumer(PaymentService paymentService) {
		this.paymentService = paymentService;
	}

	@RabbitListener(queues = RabbitConfig.PAYMENT_QUEUE)
	public void consumeOrderCreated(Object orderEvent) {
		// Consume order.created event and process accordingly
		// Idempotency is handled at the payment level via idempotency key
		System.out.println("Received order event: " + orderEvent);
	}
}
