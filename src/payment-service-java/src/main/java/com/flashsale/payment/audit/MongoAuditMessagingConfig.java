package com.flashsale.payment.audit;

import com.flashsale.payment.config.RabbitConfig;
import org.springframework.amqp.core.Binding;
import org.springframework.amqp.core.BindingBuilder;
import org.springframework.amqp.core.DirectExchange;
import org.springframework.amqp.core.Queue;
import org.springframework.amqp.core.QueueBuilder;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.beans.factory.annotation.Qualifier;

@Configuration
@Profile("mongodb")
public class MongoAuditMessagingConfig {

	public static final String QUEUE_NAME = "payment.mongo-audit.queue";
	public static final String DEAD_LETTER_EXCHANGE = "payment.mongo-audit.dlx";
	public static final String DEAD_LETTER_QUEUE = "payment.mongo-audit.dlq";

	@Bean
	Queue mongoAuditQueue() {
		return QueueBuilder.durable(QUEUE_NAME)
			.withArgument("x-dead-letter-exchange", DEAD_LETTER_EXCHANGE)
			.withArgument("x-dead-letter-routing-key", DEAD_LETTER_QUEUE)
			.build();
	}

	@Bean
	Queue mongoAuditDeadLetterQueue() {
		return QueueBuilder.durable(DEAD_LETTER_QUEUE).build();
	}

	@Bean
	DirectExchange mongoAuditDeadLetterExchange() {
		return new DirectExchange(DEAD_LETTER_EXCHANGE);
	}

	@Bean
	Binding mongoAuditCompletedBinding(
		@Qualifier("mongoAuditQueue") Queue mongoAuditQueue,
		@Qualifier("paymentEventsExchange") DirectExchange paymentEventsExchange) {
		return BindingBuilder.bind(mongoAuditQueue)
			.to(paymentEventsExchange)
			.with(RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED);
	}

	@Bean
	Binding mongoAuditFailedBinding(
		@Qualifier("mongoAuditQueue") Queue mongoAuditQueue,
		@Qualifier("paymentEventsExchange") DirectExchange paymentEventsExchange) {
		return BindingBuilder.bind(mongoAuditQueue)
			.to(paymentEventsExchange)
			.with(RabbitConfig.ROUTING_KEY_PAYMENT_FAILED);
	}

	@Bean
	Binding mongoAuditDeadLetterBinding(
		@Qualifier("mongoAuditDeadLetterQueue") Queue mongoAuditDeadLetterQueue,
		@Qualifier("mongoAuditDeadLetterExchange") DirectExchange mongoAuditDeadLetterExchange) {
		return BindingBuilder.bind(mongoAuditDeadLetterQueue)
			.to(mongoAuditDeadLetterExchange)
			.with(DEAD_LETTER_QUEUE);
	}
}
