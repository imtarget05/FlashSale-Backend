package com.flashsale.payment.config;

import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.producer.ProducerConfig;
import org.apache.kafka.common.serialization.StringDeserializer;
import org.apache.kafka.common.serialization.StringSerializer;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.kafka.annotation.EnableKafka;
import org.springframework.kafka.config.ConcurrentKafkaListenerContainerFactory;
import org.springframework.kafka.core.*;

import java.util.HashMap;
import java.util.Map;

@EnableKafka
@Configuration
public class KafkaConfig {
	@Value("${spring.kafka.bootstrap-servers}")
	private String bootstrapServers;

	public static final String PAYMENT_EVENTS_TOPIC = "payment.events";
	public static final String ORDER_EVENTS_TOPIC = "order.events";
	public static final String PAYMENT_DLQ_TOPIC = "payment.dlq";
	public static final String CONSUMER_GROUP = "payment-service-group";

	@Bean
	public ProducerFactory<String, Object> paymentProducerFactory() {
		Map<String, Object> config = new HashMap<>();
		config.put(ProducerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
		config.put(ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG, StringSerializer.class);
		config.put(ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG, StringSerializer.class);
		config.put(ProducerConfig.RETRIES_CONFIG, 3);
		config.put(ProducerConfig.ACKS_CONFIG, "all");
		return new DefaultKafkaProducerFactory<>(config);
	}

	@Bean
	public KafkaTemplate<String, Object> paymentKafkaTemplate() {
		return new KafkaTemplate<>(paymentProducerFactory());
	}

	@Bean
	public ConsumerFactory<String, Object> paymentConsumerFactory() {
		Map<String, Object> config = new HashMap<>();
		config.put(ConsumerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
		config.put(ConsumerConfig.GROUP_ID_CONFIG, CONSUMER_GROUP);
		config.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class);
		config.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, StringDeserializer.class);
		config.put(ConsumerConfig.ENABLE_AUTO_COMMIT_CONFIG, false);
		config.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
		return new DefaultKafkaConsumerFactory<>(config);
	}

	@Bean
	public ConcurrentKafkaListenerContainerFactory<String, Object> paymentKafkaListenerFactory(
		ConsumerFactory<String, Object> consumerFactory) {
		ConcurrentKafkaListenerContainerFactory<String, Object> factory =
			new ConcurrentKafkaListenerContainerFactory<>();
		factory.setConsumerFactory(consumerFactory);
		return factory;
	}
}
