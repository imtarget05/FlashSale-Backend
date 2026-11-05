package com.flashsale.payment.unit;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.PaymentNotFoundException;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.service.PaymentService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageImpl;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.domain.Pageable;
import org.springframework.data.redis.core.StringRedisTemplate;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

@ExtendWith(MockitoExtension.class)
class PaymentServiceTest {

	@Mock
	private PaymentRepository paymentRepository;

	@Mock
	private StringRedisTemplate redisTemplate;

	private PaymentService paymentService;

	private ProcessPaymentRequest validRequest;

	@BeforeEach
	void setUp() {
		paymentService = new PaymentService(paymentRepository, redisTemplate);
		validRequest = new ProcessPaymentRequest(
			UUID.randomUUID(),
			100,
			"idem-123",
			new BigDecimal("10.01"),
			"USD"
		);
	}

	@Test
	void processPayment_shouldSaveAndReturnDto_whenNewIdempotencyKey() {
		Payment saved = new Payment(
			"idem-123", validRequest.paymentId(), validRequest.orderId(), validRequest.amount(),
			validRequest.currency(), true, "Succeeded", "tx_abc123", null, false, OffsetDateTime.now());
		when(paymentRepository.findByIdempotencyKey("idem-123")).thenReturn(Optional.empty());
		when(paymentRepository.saveAndFlush(any(Payment.class))).thenReturn(saved);

		PaymentRecordDto result = paymentService.processPayment(validRequest);

		assertThat(result.success()).isTrue();
		assertThat(result.status()).isEqualTo("Succeeded");
		assertThat(result.transactionId()).isEqualTo("tx_abc123");
		verify(paymentRepository).saveAndFlush(any(Payment.class));
	}

	@Test
	void processPayment_shouldReturnDeclined_whenCentsIsTwo() {
		ProcessPaymentRequest declined = new ProcessPaymentRequest(
			UUID.randomUUID(), 101, "idem-2", new BigDecimal("10.02"), "USD");
		Payment saved = new Payment(
			"idem-2", declined.paymentId(), declined.orderId(), declined.amount(),
			declined.currency(), false, "Declined", null,
			"Insufficient funds in customer account", false, OffsetDateTime.now());
		when(paymentRepository.findByIdempotencyKey("idem-2")).thenReturn(Optional.empty());
		when(paymentRepository.saveAndFlush(any(Payment.class))).thenReturn(saved);

		PaymentRecordDto result = paymentService.processPayment(declined);

		assertThat(result.success()).isFalse();
		assertThat(result.status()).isEqualTo("Declined");
		assertThat(result.failureReason()).isEqualTo("Insufficient funds in customer account");
	}

	@Test
	void processPayment_shouldReturn504_whenCentsIsThree() {
		ProcessPaymentRequest timedOut = new ProcessPaymentRequest(
			UUID.randomUUID(), 102, "idem-3", new BigDecimal("10.03"), "USD");
		Payment saved = new Payment(
			"idem-3", timedOut.paymentId(), timedOut.orderId(), timedOut.amount(),
			timedOut.currency(), false, "TimedOut", null,
			"Upstream payment gateway timeout", true, OffsetDateTime.now());
		when(paymentRepository.findByIdempotencyKey("idem-3")).thenReturn(Optional.empty());
		when(paymentRepository.saveAndFlush(any(Payment.class))).thenReturn(saved);

		assertThatThrownBy(() -> paymentService.processPayment(timedOut))
			.isInstanceOf(com.flashsale.payment.exception.PaymentTransientException.class);
		verify(paymentRepository).saveAndFlush(any(Payment.class));
	}

	@Test
	void processPayment_shouldReturnExisting_whenIdempotencyKeyReused() {
		Payment existing = new Payment(
			"idem-replay", validRequest.paymentId(), validRequest.orderId(), validRequest.amount(),
			validRequest.currency(), true, "Succeeded", "tx_existing", null, false, OffsetDateTime.now());
		when(paymentRepository.findByIdempotencyKey("idem-replay")).thenReturn(Optional.of(existing));

		ProcessPaymentRequest replay = new ProcessPaymentRequest(
			validRequest.paymentId(), validRequest.orderId(), "idem-replay",
			validRequest.amount(), "USD");

		PaymentRecordDto result = paymentService.processPayment(replay);

		assertThat(result.transactionId()).isEqualTo("tx_existing");
		assertThat(result.status()).isEqualTo("Succeeded");
		verify(paymentRepository, never()).saveAndFlush(any());
	}

	@Test
	void processPayment_rejectsSameKeyWithDifferentAmount() {
		Payment existing = new Payment(
			"idem-123", validRequest.paymentId(), validRequest.orderId(), validRequest.amount(),
			validRequest.currency(), true, "Succeeded", "tx_existing", null, false, OffsetDateTime.now());
		when(paymentRepository.findByIdempotencyKey("idem-123")).thenReturn(Optional.of(existing));
		ProcessPaymentRequest changed = new ProcessPaymentRequest(
			validRequest.paymentId(), validRequest.orderId(), "idem-123", new BigDecimal("99.99"), "USD");

		assertThatThrownBy(() -> paymentService.processPayment(changed))
			.isInstanceOf(IdempotencyConflictException.class);
		verify(paymentRepository, never()).saveAndFlush(any());
	}

	@Test
	void getPayment_shouldThrowNotFound_whenKeyDoesNotExist() {
		when(paymentRepository.findByIdempotencyKey("missing")).thenReturn(Optional.empty());

		assertThatThrownBy(() -> paymentService.getPayment("missing"))
			.isInstanceOf(PaymentNotFoundException.class);
	}

	@Test
	void listPayments_shouldReturnPage() {
		Page<Payment> page = new PageImpl<>(List.of(), PageRequest.of(0, 20), 0);
		when(paymentRepository.findAll(any(Pageable.class))).thenReturn(page);

		Page<PaymentRecordDto> result = paymentService.listPayments(PageRequest.of(0, 20));

		assertThat(result).isNotNull();
		assertThat(result.getTotalElements()).isEqualTo(0);
	}
}
