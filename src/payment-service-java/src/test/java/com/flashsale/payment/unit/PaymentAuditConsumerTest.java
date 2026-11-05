package com.flashsale.payment.unit;

import com.flashsale.payment.audit.PaymentAuditConsumer;
import com.flashsale.payment.audit.PaymentAuditService;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.rabbitmq.client.Channel;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.dao.DataAccessResourceFailureException;

import java.io.IOException;

import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoMoreInteractions;
import static org.mockito.Mockito.doThrow;

@ExtendWith(MockitoExtension.class)
class PaymentAuditConsumerTest {

	@Mock
	private PaymentAuditService auditService;

	@Mock
	private Channel channel;

	@InjectMocks
	private PaymentAuditConsumer consumer;

	@Test
	void acknowledgesMessageAfterProjectionIsPersisted() throws IOException {
		consumer.consume(paymentEvent(), channel, 42L);

		verify(auditService).record(paymentEvent());
		verify(channel).basicAck(42L, false);
		verifyNoMoreInteractions(channel);
	}

	@Test
	void deadLettersMessageWhenMongoIsUnavailable() throws IOException {
		doThrow(new DataAccessResourceFailureException("MongoDB unavailable"))
			.when(auditService).record(paymentEvent());

		consumer.consume(paymentEvent(), channel, 43L);

		verify(channel).basicNack(43L, false, false);
		verifyNoMoreInteractions(channel);
	}

	private PaymentRecordDto paymentEvent() {
		return new PaymentRecordDto("idem-123", true, "Succeeded", "tx-123", null, false, "2026-01-02T03:04:05Z");
	}
}
