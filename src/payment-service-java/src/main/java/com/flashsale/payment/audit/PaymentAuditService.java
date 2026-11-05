package com.flashsale.payment.audit;

import com.flashsale.payment.dto.PaymentRecordDto;
import org.springframework.context.annotation.Profile;
import org.springframework.stereotype.Service;

import java.util.Optional;

@Service
@Profile("mongodb")
public class PaymentAuditService {

	private final PaymentAuditRepository repository;

	public PaymentAuditService(PaymentAuditRepository repository) {
		this.repository = repository;
	}

	public void record(PaymentRecordDto event) {
		repository.save(new PaymentAuditDocument(
			event.idempotencyKey(),
			event.success(),
			event.status(),
			event.failureReason(),
			event.isTransientError(),
			event.processedAt()));
	}

	public Optional<PaymentAuditDocument> findByIdempotencyKey(String idempotencyKey) {
		return repository.findById(idempotencyKey);
	}
}
