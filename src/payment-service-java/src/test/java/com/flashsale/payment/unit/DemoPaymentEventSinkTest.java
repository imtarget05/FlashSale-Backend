package com.flashsale.payment.unit;

import com.flashsale.payment.config.RabbitConfig;
import org.junit.jupiter.api.Test;
import org.springframework.amqp.core.Binding;
import org.springframework.amqp.core.Queue;

import static org.assertj.core.api.Assertions.assertThat;

class DemoPaymentEventSinkTest {

	private final RabbitConfig config = new RabbitConfig();

	@Test
	void demoQueueIsBoundToBothPaymentOutcomesAndExpiresOldMessages() {
		Queue queue = config.demoPaymentEventQueue();
		Binding completed = config.demoPaymentCompletedBinding(queue, config.paymentEventsExchange());
		Binding failed = config.demoPaymentFailedBinding(queue, config.paymentEventsExchange());

		assertThat(queue.getName()).isEqualTo("payment.demo.events");
		assertThat(queue.isDurable()).isTrue();
		assertThat(queue.getArguments()).containsEntry("x-message-ttl", 86_400_000)
			.containsEntry("x-max-length", 1_000);
		assertThat(completed.getExchange()).isEqualTo(RabbitConfig.PAYMENT_EVENTS_EXCHANGE);
		assertThat(completed.getRoutingKey()).isEqualTo(RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED);
		assertThat(failed.getRoutingKey()).isEqualTo(RabbitConfig.ROUTING_KEY_PAYMENT_FAILED);
	}
}
