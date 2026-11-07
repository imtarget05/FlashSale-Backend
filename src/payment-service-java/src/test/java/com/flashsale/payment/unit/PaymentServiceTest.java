package com.flashsale.payment.unit;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.config.RabbitConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.PaymentNotFoundException;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.outbox.PaymentOutbox;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import com.flashsale.payment.provider.PaymentProvider;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.service.PaymentService;
import org.junit.jupiter.api.AfterEach;
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
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.context.SecurityContextHolder;

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

	@Mock
	private PaymentOutboxRepository outboxRepository;

	@Mock
	private PaymentProvider paymentProvider;

	private PaymentService paymentService;

	private ProcessPaymentRequest validRequest;

	@AfterEach
	void clearSecurityContext() {
		SecurityContextHolder.clearContext();
	}

	@BeforeEach
	void setUp() {
		paymentService = new PaymentService(paymentRepository, redisTemplate, outboxRepository, paymentProvider, new ObjectMapper());
		validRequest = new ProcessPaymentRequest(
			UUID.randomUUID(),
			100,
			"idem-123",
			new BigDecimal("10.01"),
			"USD"
		);
		lenient().when(paymentProvider.process(any(ProcessPaymentRequest.class)))
			.thenReturn(new PaymentRecordDto("idem-123", true, "Succeeded", "tx_123", null, false, OffsetDateTime.now().toString()));
	}

	@Test
	void processPayment_shouldSaveAndReturnDto_whenNewIdempotencyKey() {
		when(paymentRepository.findByIdempotencyKey("idem-123")).thenReturn(Optional.empty());
		when(paymentRepository.insertIfAbsent(any(Payment.class))).thenReturn(1);

		PaymentRecordDto result = paymentService.processPayment(validRequest);

		assertThat(result.success()).isTrue();
		assertThat(result.status()).isEqualTo("Succeeded");
		assertThat(result.transactionId()).startsWith("tx_");
		verify(paymentRepository).insertIfAbsent(any(Payment.class));
		org.mockito.ArgumentCaptor<PaymentOutbox> outboxCaptor =
			org.mockito.ArgumentCaptor.forClass(PaymentOutbox.class);
		verify(outboxRepository).save(outboxCaptor.capture());
		assertThat(outboxCaptor.getValue().getRoutingKey())
			.isEqualTo(RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED);
	}

	@Test
	void processPayment_shouldReturnDeclined_whenCentsIsTwo() {
		ProcessPaymentRequest declined = new ProcessPaymentRequest(
			UUID.randomUUID(), 101, "idem-2", new BigDecimal("10.02"), "USD");
		when(paymentRepository.findByIdempotencyKey("idem-2")).thenReturn(Optional.empty());
		when(paymentRepository.insertIfAbsent(any(Payment.class))).thenReturn(1);
		when(paymentProvider.process(any(ProcessPaymentRequest.class)))
			.thenReturn(new PaymentRecordDto("idem-2", false, "Declined", null, "Insufficient funds in customer account", false, OffsetDateTime.now().toString()));

		PaymentRecordDto result = paymentService.processPayment(declined);

		assertThat(result.success()).isFalse();
		assertThat(result.status()).isEqualTo("Declined");
		assertThat(result.failureReason()).isEqualTo("Insufficient funds in customer account");
		verify(outboxRepository).save(any(PaymentOutbox.class));
	}

	@Test
	void processPayment_shouldReturn504_whenCentsIsThree() {
		ProcessPaymentRequest timedOut = new ProcessPaymentRequest(
			UUID.randomUUID(), 102, "idem-3", new BigDecimal("10.03"), "USD");
		when(paymentRepository.findByIdempotencyKey("idem-3")).thenReturn(Optional.empty());
		when(paymentRepository.insertIfAbsent(any(Payment.class))).thenReturn(1);
		when(paymentProvider.process(any(ProcessPaymentRequest.class)))
			.thenReturn(new PaymentRecordDto("idem-3", false, "TimedOut", null, "Upstream payment gateway timeout", true, OffsetDateTime.now().toString()));

		assertThatThrownBy(() -> paymentService.processPayment(timedOut))
			.isInstanceOf(com.flashsale.payment.exception.PaymentTransientException.class);
		verify(paymentRepository).insertIfAbsent(any(Payment.class));
		verify(outboxRepository).save(any(PaymentOutbox.class));
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
		verify(paymentRepository, never()).insertIfAbsent(any());
		verify(outboxRepository, never()).save(any());
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
		verify(paymentRepository, never()).insertIfAbsent(any());
		verify(outboxRepository, never()).save(any());
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

	@Test
	void getPayment_allowsAccess_whenPaymentOwnedByCurrentUser() {
		Payment ownedByA = new Payment("idem-1", UUID.randomUUID(), 100, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx_1", null, false, OffsetDateTime.now());
		ownedByA.setCustomerEmail("userA@flashsale.local");
		when(paymentRepository.findByIdempotencyKey("idem-1")).thenReturn(Optional.of(ownedByA));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("userA@flashsale.local", "password",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		PaymentRecordDto result = paymentService.getPayment("idem-1");

		assertThat(result.idempotencyKey()).isEqualTo("idem-1");
		assertThat(result.status()).isEqualTo("Succeeded");
	}

	@Test
	void getPayment_deniesAccess_whenPaymentOwnedByAnotherUser() {
		Payment ownedByA = new Payment("idem-1", UUID.randomUUID(), 100, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx_1", null, false, OffsetDateTime.now());
		ownedByA.setCustomerEmail("userA@flashsale.local");
		when(paymentRepository.findByIdempotencyKey("idem-1")).thenReturn(Optional.of(ownedByA));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("userB@flashsale.local", "password",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		assertThatThrownBy(() -> paymentService.getPayment("idem-1"))
			.isInstanceOf(AccessDeniedException.class);
	}

	@Test
	void getPayment_allowsAccess_whenOwnerNotRecorded_legacyPayment() {
		Payment legacy = new Payment("idem-legacy", UUID.randomUUID(), 100, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx_legacy", null, false, OffsetDateTime.now());
		when(paymentRepository.findByIdempotencyKey("idem-legacy")).thenReturn(Optional.of(legacy));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("userB@flashsale.local", "password",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		assertThat(paymentService.getPayment("idem-legacy")).isNotNull();
	}

}
