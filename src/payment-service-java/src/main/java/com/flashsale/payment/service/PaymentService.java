package com.flashsale.payment.service;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.PaymentNotFoundException;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.exception.PaymentTransientException;
import com.flashsale.payment.repository.PaymentRepository;
import org.springframework.cache.annotation.CacheEvict;
import org.springframework.cache.annotation.Cacheable;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import io.micrometer.core.annotation.Counted;
import io.micrometer.core.annotation.Timed;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.UUID;

@Service
public class PaymentService {

	private static final Logger log = LoggerFactory.getLogger(PaymentService.class);

	private final PaymentRepository paymentRepository;
	private final StringRedisTemplate redisTemplate;

	public PaymentService(PaymentRepository paymentRepository, StringRedisTemplate redisTemplate) {
		this.paymentRepository = paymentRepository;
		this.redisTemplate = redisTemplate;
	}

	@Transactional(noRollbackFor = PaymentTransientException.class)
	@CacheEvict(value = "payments", key = "#request.idempotencyKey")
	@Timed(value = "payment.process.payment", description = "Time spent processing a payment")
	@Counted(value = "payment.process.payment.calls", description = "Number of processPayment calls")
	public PaymentRecordDto processPayment(ProcessPaymentRequest request) {
		log.info("[payment-service] processPayment started idempotencyKey={} paymentId={} orderId={} amount={} currency={}",
			request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(), request.currency());

		var existing = paymentRepository.findByIdempotencyKey(request.idempotencyKey());
		if (existing.isPresent()) {
			log.info("[payment-service] idempotency replay detected idempotencyKey={}", request.idempotencyKey());
			return replay(request, existing.get());
		}

		BigDecimal amount = request.amount();
		int cents = amount.remainder(BigDecimal.ONE).movePointRight(2).intValue();

		Payment record;
		if (cents == 2) {
			record = new Payment(
				request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(),
				request.currency(), false, "Declined", null,
				"Insufficient funds in customer account", false, 				OffsetDateTime.now());
		} else if (cents == 3) {
			record = new Payment(
				request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(),
				request.currency(), false, "TimedOut", null,
				"Upstream payment gateway timeout", true, 				OffsetDateTime.now());
		} else {
			record = new Payment(
				request.idempotencyKey(), request.paymentId(), request.orderId(), request.amount(),
				request.currency(), true, "Succeeded",
				"tx_" + UUID.randomUUID().toString().replace("-", ""), null, false, 				OffsetDateTime.now());
		}

		Payment saved;
		try {
			// @Transactional places the save and the eventual event publication inside one
			// transaction boundary so the payment record and the downstream event succeed or
			// fail together (atomicity). A duplicate insert can be retried after its
			// transaction has rolled back.
			saved = paymentRepository.saveAndFlush(record);
		} catch (org.springframework.dao.DataIntegrityViolationException ex) {
			Payment winner = paymentRepository.findByIdempotencyKey(request.idempotencyKey())
				.orElseThrow(() -> ex);
			log.warn("[payment-service] duplicate idempotencyKey detected, replaying winner idempotencyKey={}",
				request.idempotencyKey());
			return replay(request, winner);
		}

		if ("TimedOut".equals(saved.getStatus())) {
			log.warn("[payment-service] transient gateway timeout recorded idempotencyKey={} paymentId={}",
				saved.getIdempotencyKey(), saved.getPaymentId());
			throw new PaymentTransientException("Upstream payment gateway timeout", saved);
		}

		log.info("[payment-service] processPayment succeeded idempotencyKey={} status={} transactionId={}",
			saved.getIdempotencyKey(), saved.getStatus(), saved.getTransactionId());
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

	@Cacheable(value = "payments", key = "#idempotencyKey")
	@Timed(value = "payment.get.payment", description = "Time spent getting a payment")
	@Counted(value = "payment.get.payment.calls", description = "Number of getPayment calls")
	public PaymentRecordDto getPayment(String idempotencyKey) {
		log.info("[payment-service] getPayment cache miss idempotencyKey={}", idempotencyKey);
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
