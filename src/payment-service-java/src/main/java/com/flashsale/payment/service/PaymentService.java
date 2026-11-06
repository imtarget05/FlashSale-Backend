package com.flashsale.payment.service;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.PaymentNotFoundException;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.exception.PaymentTransientException;
import com.flashsale.payment.outbox.PaymentOutbox;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
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

import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.core.userdetails.UserDetails;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.UUID;

@Service
public class PaymentService {

	private static final Logger log = LoggerFactory.getLogger(PaymentService.class);

	private final PaymentRepository paymentRepository;
	private final StringRedisTemplate redisTemplate;
	private final PaymentOutboxRepository outboxRepository;
	private final ObjectMapper objectMapper;

	public PaymentService(PaymentRepository paymentRepository, StringRedisTemplate redisTemplate,
		PaymentOutboxRepository outboxRepository, ObjectMapper objectMapper) {
		this.paymentRepository = paymentRepository;
		this.redisTemplate = redisTemplate;
		this.outboxRepository = outboxRepository;
		this.objectMapper = objectMapper;
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

		record.setCustomerEmail(currentUserEmail());
		int inserted = paymentRepository.insertIfAbsent(record);
		if (inserted == 0) {
			Payment winner = paymentRepository.findByIdempotencyKey(request.idempotencyKey())
				.orElseThrow(() -> new IllegalStateException(
					"Payment row missing after idempotent insert for key '" + request.idempotencyKey() + "'"));
			log.warn("[payment-service] duplicate idempotencyKey detected, replaying winner idempotencyKey={}",
				request.idempotencyKey());
			return replay(request, winner);
		}

		PaymentRecordDto result = toDto(record);
		String routingKey = result.success()
			? RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED
			: RabbitConfig.ROUTING_KEY_PAYMENT_FAILED;
		try {
			outboxRepository.save(new PaymentOutbox(
				record.getIdempotencyKey(), routingKey, objectMapper.writeValueAsString(result),
				OffsetDateTime.now()));
		} catch (JsonProcessingException ex) {
			throw new IllegalStateException("Could not serialize payment outbox event", ex);
		}

		if ("TimedOut".equals(record.getStatus())) {
			log.warn("[payment-service] transient gateway timeout recorded idempotencyKey={} paymentId={}",
				record.getIdempotencyKey(), record.getPaymentId());
			throw new PaymentTransientException("Upstream payment gateway timeout", record);
		}

		log.info("[payment-service] processPayment succeeded idempotencyKey={} status={} transactionId={}",
			record.getIdempotencyKey(), record.getStatus(), record.getTransactionId());
		return result;
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

	@Timed(value = "payment.get.payment", description = "Time spent getting a payment")
	@Counted(value = "payment.get.payment.calls", description = "Number of getPayment calls")
	public PaymentRecordDto getPayment(String idempotencyKey) {
		log.info("[payment-service] getPayment idempotencyKey={}", idempotencyKey);
		String currentUser = currentUserEmail();
		Payment payment = paymentRepository.findByIdempotencyKey(idempotencyKey)
			.orElseThrow(() -> new PaymentNotFoundException("Payment for key '" + idempotencyKey + "' not found"));
		String ownerEmail = payment.getCustomerEmail();
		if (ownerEmail != null && !ownerEmail.equals(currentUser)) {
			throw new AccessDeniedException(
				"Payment '" + idempotencyKey + "' does not belong to the authenticated user");
		}
		return toDto(payment);
	}

	private String currentUserEmail() {
		var authentication = SecurityContextHolder.getContext().getAuthentication();
		if (authentication == null || !authentication.isAuthenticated()) {
			return null;
		}
		Object principal = authentication.getPrincipal();
		if (principal instanceof UserDetails) {
			return ((UserDetails) principal).getUsername();
		}
		return authentication.getName();
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
