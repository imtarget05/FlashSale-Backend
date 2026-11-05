package com.flashsale.payment.grpc;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.exception.PaymentTransientException;
import com.flashsale.payment.grpc.v1.HealthCheckRequest;
import com.flashsale.payment.grpc.v1.ProcessPaymentRequest;
import com.flashsale.payment.grpc.v1.ProcessPaymentResponse;
import com.flashsale.payment.security.PaymentGrpcAuthInterceptor;
import com.flashsale.payment.service.PaymentService;
import io.grpc.Context;
import io.grpc.Status;
import io.grpc.stub.StreamObserver;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.Authentication;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

@ExtendWith(MockitoExtension.class)
class PaymentGrpcServiceImplTest {

	private static final String PAYMENT_ID = "7f377b72-8913-4f82-82fb-2529c2bccf51";

	@Mock
	private PaymentService paymentService;

	private PaymentGrpcServiceImpl grpcService;

	@BeforeEach
	void setUp() {
		grpcService = new PaymentGrpcServiceImpl(paymentService);
	}

	@Test
	void processesValidStaffRequestAndMapsResponse() {
		var expected = new PaymentRecordDto(
			"checkout-123", true, "Succeeded", "tx-123", null, false, "2026-03-03T10:15:30Z");
		when(paymentService.processPayment(any())).thenReturn(expected);
		var observer = new RecordingObserver<ProcessPaymentResponse>();

		asAuthenticated("ROLE_STAFF", () -> grpcService.processPayment(validRequest(), observer));

		assertNull(observer.error);
		assertEquals("checkout-123", observer.value.getIdempotencyKey());
		assertTrue(observer.value.getSuccess());
		assertEquals("tx-123", observer.value.getTransactionId());
		verify(paymentService).processPayment(any());
	}

	@Test
	void rejectsMalformedPaymentIdWithoutCallingPaymentService() {
		var request = validRequest().toBuilder().setPaymentId("not-a-uuid").build();
		var observer = new RecordingObserver<ProcessPaymentResponse>();

		asAuthenticated("ROLE_STAFF", () -> grpcService.processPayment(request, observer));

		assertEquals(Status.Code.INVALID_ARGUMENT, Status.fromThrowable(observer.error).getCode());
		verify(paymentService, never()).processPayment(any());
	}

	@Test
	void deniesPaymentProcessingToCustomerRole() {
		var observer = new RecordingObserver<ProcessPaymentResponse>();

		asAuthenticated("ROLE_CUSTOMER", () -> grpcService.processPayment(validRequest(), observer));

		assertEquals(Status.Code.PERMISSION_DENIED, Status.fromThrowable(observer.error).getCode());
		verify(paymentService, never()).processPayment(any());
	}

	@Test
	void mapsTransientPaymentExceptionToRecordedResponse() {
		var payment = new Payment(
			"checkout-123", UUID.fromString(PAYMENT_ID), 42, new BigDecimal("19.03"),
			"USD", false, "TimedOut", null, "Upstream payment gateway timeout", true,
			OffsetDateTime.parse("2026-03-03T10:15:30Z"));
		when(paymentService.processPayment(any()))
			.thenThrow(new PaymentTransientException("internal detail", payment));
		var observer = new RecordingObserver<ProcessPaymentResponse>();

		asAuthenticated("ROLE_ADMIN", () -> grpcService.processPayment(validRequest(), observer));

		assertNull(observer.error);
		assertEquals("TimedOut", observer.value.getStatus());
		assertFalse(observer.value.getSuccess());
		assertTrue(observer.value.getIsTransientError());
		assertEquals("Upstream payment gateway timeout", observer.value.getFailureReason());
	}

	@Test
	void mapsIdempotencyConflictToAlreadyExistsWithoutLeakingDetails() {
		when(paymentService.processPayment(any()))
			.thenThrow(new IdempotencyConflictException("sensitive internal details"));
		var observer = new RecordingObserver<ProcessPaymentResponse>();

		asAuthenticated("ROLE_STAFF", () -> grpcService.processPayment(validRequest(), observer));

		assertEquals(Status.Code.ALREADY_EXISTS, Status.fromThrowable(observer.error).getCode());
		assertEquals("Idempotency key conflict", Status.fromThrowable(observer.error).getDescription());
	}

	@Test
	void servesHealthCheckWithoutAuthentication() {
		var observer = new RecordingObserver<com.flashsale.payment.grpc.v1.HealthCheckResponse>();

		grpcService.checkHealth(HealthCheckRequest.getDefaultInstance(), observer);

		assertNull(observer.error);
		assertEquals("SERVING", observer.value.getStatus());
		assertEquals("payment-service", observer.value.getService());
	}

	private static ProcessPaymentRequest validRequest() {
		return ProcessPaymentRequest.newBuilder()
			.setIdempotencyKey("checkout-123")
			.setOrderId(42)
			.setPaymentId(PAYMENT_ID)
			.setAmount("19.03")
			.setCurrency("USD")
			.build();
	}

	private static void asAuthenticated(String role, Runnable action) {
		Authentication authentication = new UsernamePasswordAuthenticationToken(
			"test-user", null, List.of(new SimpleGrantedAuthority(role)));
		Context.current()
			.withValue(PaymentGrpcAuthInterceptor.AUTHENTICATION_CONTEXT_KEY, authentication)
			.run(action);
	}

	private static final class RecordingObserver<T> implements StreamObserver<T> {
		private T value;
		private Throwable error;
		private boolean completed;

		@Override
		public void onNext(T value) {
			this.value = value;
		}

		@Override
		public void onError(Throwable error) {
			this.error = error;
		}

		@Override
		public void onCompleted() {
			completed = true;
		}
	}
}
