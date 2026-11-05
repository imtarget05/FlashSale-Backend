package com.flashsale.payment.config;

import org.springframework.amqp.core.*;
import org.springframework.amqp.rabbit.connection.ConnectionFactory;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.amqp.support.converter.JacksonJsonMessageConverter;
import org.springframework.amqp.support.converter.MessageConverter;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class RabbitConfig {

	public static final String ORDER_EVENTS_EXCHANGE = "order.events";
	public static final String PAYMENT_EVENTS_EXCHANGE = "payment.events";
	public static final String PAYMENT_QUEUE = "payment.queue";
	public static final String PAYMENT_DLQ = "payment.dlq";
	public static final String PAYMENT_RETRY_EXCHANGE = "payment.retry";
	public static final String ROUTING_KEY_ORDER_CREATED = "order.created";
	public static final String ROUTING_KEY_ORDER_CANCELLED = "order.cancelled";
	public static final String ROUTING_KEY_PAYMENT_COMPLETED = "payment.completed";
	public static final String ROUTING_KEY_PAYMENT_FAILED = "payment.failed";

	@Bean
	public DirectExchange orderEventsExchange() {
		return new DirectExchange(ORDER_EVENTS_EXCHANGE);
	}

	@Bean
	public DirectExchange paymentEventsExchange() {
		return new DirectExchange(PAYMENT_EVENTS_EXCHANGE);
	}

	@Bean
	public DirectExchange paymentRetryExchange() {
		return new DirectExchange(PAYMENT_RETRY_EXCHANGE);
	}

	@Bean
	public Queue paymentQueue() {
		return QueueBuilder.durable(PAYMENT_QUEUE)
			.withArgument("x-dead-letter-exchange", PAYMENT_EVENTS_EXCHANGE)
			.withArgument("x-dead-letter-routing-key", ROUTING_KEY_PAYMENT_FAILED)
			.build();
	}

	@Bean
	public Queue paymentDlq() {
		return QueueBuilder.durable(PAYMENT_DLQ).build();
	}

	@Bean
	public Binding paymentQueueBinding(Queue paymentQueue, DirectExchange orderEventsExchange) {
		return BindingBuilder.bind(paymentQueue)
			.to(orderEventsExchange)
			.with(ROUTING_KEY_ORDER_CREATED);
	}

	@Bean
	public Binding paymentDlqBinding(Queue paymentDlq, DirectExchange paymentEventsExchange) {
		return BindingBuilder.bind(paymentDlq)
			.to(paymentEventsExchange)
			.with(ROUTING_KEY_PAYMENT_FAILED);
	}

	@Bean
	public RabbitTemplate rabbitTemplate(ConnectionFactory connectionFactory) {
		RabbitTemplate template = new RabbitTemplate(connectionFactory);
		template.setMessageConverter(messageConverter());
		template.setMandatory(true);
		return template;
	}

	@Bean
	public MessageConverter messageConverter() {
		return new JacksonJsonMessageConverter(
			"com.flashsale.payment.event", "com.flashsale.payment.dto");
	}
}
