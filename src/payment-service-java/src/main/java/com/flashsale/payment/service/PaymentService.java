package com.flashsale.payment.service;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.PaymentNotFoundException;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.exception.PaymentTransientException;
import com.flashsale.payment.repository.PaymentRepository;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.stereotype.Service;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.UUID;

@Service
public class PaymentService {

	private final PaymentRepository paymentRepository;

	public PaymentService(PaymentRepository paymentRepository) {
		this.paymentRepository = paymentRepository;
	}

	public PaymentRecordDto processPayment(ProcessPaymentRequest request) {
		var existing = paymentRepository.findByIdempotencyKey(request.idempotencyKey());
		if (existing.isPresent()) {
			return replay(request, existing.get());
		}

		BigDecimal amount = request.amount();
		int cents = amount.remainder(BigDecimal.ONE).movePointRight(2).intValue();

		Payment record;
		if (cents == 2) {
			record = new Payment(
				request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(),
				request.currency(), false, "Declined", null,
				"Insufficient funds in customer account", false, OffsetDateTime.now());
		} else if (cents == 3) {
			record = new Payment(
				request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(),
				request.currency(), false, "TimedOut", null,
				"Upstream payment gateway timeout", true, OffsetDateTime.now());
		} else {
			record = new Payment(
				request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(),
				request.currency(), true, "Succeeded",
				"tx_" + UUID.randomUUID().toString().replace("-", ""), null, false, OffsetDateTime.now());
		}

		Payment saved;
		try {
			// The repository owns this transaction. A duplicate insert can be retried
			// after its transaction has rolled back; the service has no outer transaction.
			saved = paymentRepository.saveAndFlush(record);
		} catch (org.springframework.dao.DataIntegrityViolationException ex) {
			Payment winner = paymentRepository.findByIdempotencyKey(request.idempotencyKey())
				.orElseThrow(() -> ex);
			return replay(request, winner);
		}

		if ("TimedOut".equals(saved.getStatus())) {
			throw new PaymentTransientException("Upstream payment gateway timeout", saved);
		}

		return toDto(saved);
	}

	private PaymentRecordDto replay(ProcessPaymentRequest request, Payment stored) {
		if (!stored.getPaymentId().equals(request.paymentId())
			|| !stored.getOrderId().equals(request.orderId())
			|| stored.getAmount().compareTo(request.amount()) != 0
			|| !stored.getCurrency().equals(request.currency())) {
			throw new IdempotencyConflictException("Idempotency key already belongs to a different payment");
		}
		return toDto(stored);
	}

	public PaymentRecordDto getPayment(String idempotencyKey) {
		return paymentRepository.findByIdempotencyKey(idempotencyKey)
			.map(this::toDto)
			.orElseThrow(() -> new PaymentNotFoundException("Payment for key '" + idempotencyKey + "' not found"));
	}

	public Page<PaymentRecordDto> listPayments(Pageable pageable) {
		return paymentRepository.findAll(pageable).map(this::toDto);
	}

	private PaymentRecordDto toDto(Payment payment) {
		return new PaymentRecordDto(
			payment.getIdempotencyKey(),
			payment.getSuccess(),
			payment.getStatus(),
			payment.getTransactionId(),
			payment.getFailureReason(),
			payment.getIsTransientError(),
			payment.getProcessedAt() != null ? payment.getProcessedAt().toString() : null
		);
	}
}
