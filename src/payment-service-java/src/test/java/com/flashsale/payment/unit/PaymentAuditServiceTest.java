package com.flashsale.payment.unit;

import com.flashsale.payment.audit.PaymentAuditDocument;
import com.flashsale.payment.audit.PaymentAuditRepository;
import com.flashsale.payment.audit.PaymentAuditService;
import com.flashsale.payment.dto.PaymentRecordDto;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.verify;

@ExtendWith(MockitoExtension.class)
class PaymentAuditServiceTest {

	@Mock
	private PaymentAuditRepository repository;

	@InjectMocks
	private PaymentAuditService service;

	@Test
	void record_persistsOnlyThePaymentAuditProjection() {
		PaymentRecordDto event = new PaymentRecordDto(
			"idem-123", true, "Succeeded", "tx-123", null, false, "2026-01-02T03:04:05Z");

		service.record(event);

		ArgumentCaptor<PaymentAuditDocument> document = ArgumentCaptor.forClass(PaymentAuditDocument.class);
		verify(repository).save(document.capture());
		assertThat(document.getValue()).isEqualTo(new PaymentAuditDocument(
			"idem-123", true, "Succeeded", null, false, "2026-01-02T03:04:05Z"));
	}
}
