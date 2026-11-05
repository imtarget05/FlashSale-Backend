package com.flashsale.payment.outbox;

import com.flashsale.payment.event.PaymentEventPublisher;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.amqp.AmqpException;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;

@Component
public class PaymentOutboxRelay {

	private static final Logger log = LoggerFactory.getLogger(PaymentOutboxRelay.class);
	private static final int BATCH_SIZE = 25;

	private final PaymentOutboxRepository outboxRepository;
	private final PaymentEventPublisher eventPublisher;

	public PaymentOutboxRelay(PaymentOutboxRepository outboxRepository, PaymentEventPublisher eventPublisher) {
		this.outboxRepository = outboxRepository;
		this.eventPublisher = eventPublisher;
	}

	@Scheduled(fixedDelayString = "${payment.outbox.poll-interval-ms:1000}")
	@Transactional
	public void publishPendingEvents() {
		OffsetDateTime now = OffsetDateTime.now(ZoneOffset.UTC);
		List<PaymentOutbox> events = outboxRepository.lockDueEvents(now, BATCH_SIZE);

		for (PaymentOutbox event : events) {
			try {
				eventPublisher.publish(event);
				event.markPublished(OffsetDateTime.now(ZoneOffset.UTC));
				log.info("[payment-service] published outbox event idempotencyKey={} routingKey={}",
					event.getIdempotencyKey(), event.getRoutingKey());
			} catch (AmqpException ex) {
				event.recordFailure(OffsetDateTime.now(ZoneOffset.UTC), ex);
				log.warn("[payment-service] outbox publish failed idempotencyKey={} attempts={} retryAt={}",
					event.getIdempotencyKey(), event.getAttempts(), event.getNextAttemptAt(), ex);
			}
		}
	}
}
