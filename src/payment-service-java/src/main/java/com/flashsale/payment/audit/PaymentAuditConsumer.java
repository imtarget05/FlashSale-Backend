package com.flashsale.payment.audit;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.rabbitmq.client.Channel;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.annotation.Profile;
import org.springframework.dao.DataAccessException;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.amqp.support.AmqpHeaders;
import org.springframework.messaging.handler.annotation.Header;
import org.springframework.stereotype.Component;

import java.io.IOException;

@Component
@Profile("mongodb")
public class PaymentAuditConsumer {

	private static final Logger log = LoggerFactory.getLogger(PaymentAuditConsumer.class);

	private final PaymentAuditService auditService;

	public PaymentAuditConsumer(PaymentAuditService auditService) {
		this.auditService = auditService;
	}

	@RabbitListener(queues = MongoAuditMessagingConfig.QUEUE_NAME, ackMode = "MANUAL")
	public void consume(PaymentRecordDto event, Channel channel,
		@Header(AmqpHeaders.DELIVERY_TAG) long deliveryTag) throws IOException {
		try {
			auditService.record(event);
			channel.basicAck(deliveryTag, false);
		} catch (DataAccessException ex) {
			log.error("[payment-service] MongoDB audit projection failed; dead-lettering event", ex);
			channel.basicNack(deliveryTag, false, false);
		}
	}
}
