package com.flashsale.payment.grpc;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.exception.IdempotencyConflictException;
import com.flashsale.payment.exception.PaymentTransientException;
import com.flashsale.payment.grpc.v1.HealthCheckRequest;
import com.flashsale.payment.grpc.v1.HealthCheckResponse;
import com.flashsale.payment.grpc.v1.PaymentServiceGrpc;
import com.flashsale.payment.grpc.v1.ProcessPaymentRequest;
import com.flashsale.payment.grpc.v1.ProcessPaymentResponse;
import com.flashsale.payment.security.PaymentGrpcAuthInterceptor;
import com.flashsale.payment.service.PaymentService;
import io.grpc.Status;
import io.grpc.stub.StreamObserver;
import org.springframework.security.core.Authentication;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.stereotype.Service;

import java.math.BigDecimal;
import java.util.UUID;

@Service
public class PaymentGrpcServiceImpl extends PaymentServiceGrpc.PaymentServiceImplBase {

	private static final String REQUIRED_ROLE_STAFF = "ROLE_STAFF";
	private static final String REQUIRED_ROLE_ADMIN = "ROLE_ADMIN";
	private static final BigDecimal MINIMUM_AMOUNT = new BigDecimal("0.01");

	private final PaymentService paymentService;

	public PaymentGrpcServiceImpl(PaymentService paymentService) {
		this.paymentService = paymentService;
	}

	@Override
	public void processPayment(
		ProcessPaymentRequest request,
		StreamObserver<ProcessPaymentResponse> responseObserver
	) {
		Authentication authentication =
			PaymentGrpcAuthInterceptor.AUTHENTICATION_CONTEXT_KEY.get();
		if (authentication == null || !hasPaymentRole(authentication)) {
			responseObserver.onError(Status.PERMISSION_DENIED
				.withDescription("Insufficient permissions")
				.asRuntimeException());
			return;
		}

		com.flashsale.payment.dto.ProcessPaymentRequest paymentRequest;
		try {
			paymentRequest = toDomainRequest(request);
		} catch (IllegalArgumentException ex) {
			responseObserver.onError(Status.INVALID_ARGUMENT
				.withDescription("Invalid payment request")
				.asRuntimeException());
			return;
		}

		try {
			responseObserver.onNext(toGrpcResponse(paymentService.processPayment(paymentRequest)));
			responseObserver.onCompleted();
		} catch (PaymentTransientException ex) {
			responseObserver.onNext(toGrpcResponse(ex.getPayment()));
			responseObserver.onCompleted();
		} catch (IdempotencyConflictException ex) {
			responseObserver.onError(Status.ALREADY_EXISTS
				.withDescription("Idempotency key conflict")
				.asRuntimeException());
		} catch (RuntimeException ex) {
			responseObserver.onError(Status.INTERNAL
				.withDescription("Payment processing failed")
				.asRuntimeException());
		}
	}

	@Override
	public void checkHealth(
		HealthCheckRequest request,
		StreamObserver<HealthCheckResponse> responseObserver
	) {
		responseObserver.onNext(HealthCheckResponse.newBuilder()
			.setStatus("SERVING")
			.setService("payment-service")
			.build());
		responseObserver.onCompleted();
	}

	private static boolean hasPaymentRole(Authentication authentication) {
		return authentication.getAuthorities().stream()
			.map(GrantedAuthority::getAuthority)
			.anyMatch(role -> REQUIRED_ROLE_STAFF.equals(role) || REQUIRED_ROLE_ADMIN.equals(role));
	}

	private static com.flashsale.payment.dto.ProcessPaymentRequest toDomainRequest(
		ProcessPaymentRequest request
	) {
		String idempotencyKey = request.getIdempotencyKey();
		String paymentIdValue = request.getPaymentId();
		String amountValue = request.getAmount();
		String currency = request.getCurrency();
		long orderId = request.getOrderId();

		if (idempotencyKey.isBlank() || idempotencyKey.length() > 128
			|| orderId <= 0 || orderId > Integer.MAX_VALUE
			|| !paymentIdValue.matches("[0-9a-fA-F-]{36}")
			|| !currency.matches("[A-Za-z0-9]{1,8}")) {
			throw new IllegalArgumentException("Invalid payment request");
		}

		UUID paymentId;
		BigDecimal amount;
		try {
			paymentId = UUID.fromString(paymentIdValue);
			amount = new BigDecimal(amountValue);
		} catch (IllegalArgumentException ex) {
			throw new IllegalArgumentException("Invalid payment request", ex);
		}
		if (!paymentId.toString().equalsIgnoreCase(paymentIdValue)
			|| amount.compareTo(MINIMUM_AMOUNT) < 0
			|| amount.scale() > 2
			|| amount.precision() > 12
			|| amount.precision() - amount.scale() > 10) {
			throw new IllegalArgumentException("Invalid payment request");
		}

		return new com.flashsale.payment.dto.ProcessPaymentRequest(
			paymentId, (int) orderId, idempotencyKey, amount, currency);
	}

	private static ProcessPaymentResponse toGrpcResponse(PaymentRecordDto payment) {
		ProcessPaymentResponse.Builder response = ProcessPaymentResponse.newBuilder()
			.setIdempotencyKey(payment.idempotencyKey())
			.setSuccess(payment.success())
			.setStatus(payment.status())
			.setTransactionId(payment.transactionId() == null ? "" : payment.transactionId())
			.setFailureReason(payment.failureReason() == null ? "" : payment.failureReason())
			.setIsTransientError(payment.isTransientError());
		if (payment.processedAt() != null) {
			response.setProcessedAt(payment.processedAt());
		}
		return response.build();
	}

	private static ProcessPaymentResponse toGrpcResponse(Payment payment) {
		ProcessPaymentResponse.Builder response = ProcessPaymentResponse.newBuilder()
			.setIdempotencyKey(payment.getIdempotencyKey())
			.setSuccess(Boolean.TRUE.equals(payment.getSuccess()))
			.setStatus(payment.getStatus())
			.setTransactionId(payment.getTransactionId() == null ? "" : payment.getTransactionId())
			.setFailureReason(payment.getFailureReason() == null ? "" : payment.getFailureReason())
			.setIsTransientError(Boolean.TRUE.equals(payment.getIsTransientError()));
		if (payment.getProcessedAt() != null) {
			response.setProcessedAt(payment.getProcessedAt().toString());
		}
		return response.build();
	}
}
