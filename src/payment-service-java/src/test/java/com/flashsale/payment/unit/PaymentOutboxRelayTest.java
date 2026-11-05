package com.flashsale.payment.unit;

import com.flashsale.payment.event.PaymentEventPublisher;
import com.flashsale.payment.outbox.PaymentOutbox;
import com.flashsale.payment.outbox.PaymentOutboxRelay;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.amqp.AmqpException;

import java.time.OffsetDateTime;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

@ExtendWith(MockitoExtension.class)
class PaymentOutboxRelayTest {

	@Mock
	private PaymentOutboxRepository outboxRepository;

	@Mock
	private PaymentEventPublisher eventPublisher;

	@Test
	void failedPublish_remainsPendingAndCanBeRetried() {
		OffsetDateTime createdAt = OffsetDateTime.now().minusMinutes(1);
		PaymentOutbox event = new PaymentOutbox(
			"idem-outbox-retry", "payment.completed", "{}", createdAt);
		PaymentOutboxRelay relay = new PaymentOutboxRelay(outboxRepository, eventPublisher);
		when(outboxRepository.lockDueEvents(any(), eq(25))).thenReturn(List.of(event), List.of(event));
		doThrow(new AmqpException("broker unavailable"))
			.doNothing().when(eventPublisher).publish(event);

		relay.publishPendingEvents();

		assertThat(event.getPublishedAt()).isNull();
		assertThat(event.getAttempts()).isEqualTo(1);
		assertThat(event.getLastError()).contains("broker unavailable");
		assertThat(event.getNextAttemptAt()).isAfter(createdAt);

		event.setNextAttemptAt(OffsetDateTime.now().minusSeconds(1));
		relay.publishPendingEvents();

		assertThat(event.getPublishedAt()).isNotNull();
		assertThat(event.getAttempts()).isEqualTo(1);
		verify(eventPublisher, times(2)).publish(event);
	}
}
