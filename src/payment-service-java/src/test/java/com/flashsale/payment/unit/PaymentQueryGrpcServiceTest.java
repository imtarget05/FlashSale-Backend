package com.flashsale.payment.unit;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.exception.PaymentNotFoundException;
import com.flashsale.payment.grpc.PaymentQueryGrpcService;
import com.flashsale.payment.grpc.v1.GetPaymentRequest;
import com.flashsale.payment.grpc.v1.PaymentQueryServiceGrpc;
import com.flashsale.payment.grpc.v1.PaymentRecord;
import com.flashsale.payment.service.PaymentService;
import io.grpc.Status;
import io.grpc.stub.StreamObserver;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import java.util.ArrayList;
import java.util.List;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;

@ExtendWith(MockitoExtension.class)
class PaymentQueryGrpcServiceTest {

	@Mock
	private PaymentService paymentService;

	@InjectMocks
	private PaymentQueryGrpcService grpcService;

	@Test
	void getsPaymentAndMapsTheCompleteResponse() {
		when(paymentService.getPayment("idem-123")).thenReturn(
			new PaymentRecordDto("idem-123", true, "Succeeded", "tx-123", null, false, "2026-01-02T03:04:05Z"));
		TestObserver<PaymentRecord> observer = new TestObserver<>();

		grpcService.getPayment(GetPaymentRequest.newBuilder().setIdempotencyKey("idem-123").build(), observer);

		assertThat(observer.values).containsExactly(PaymentRecord.newBuilder()
			.setIdempotencyKey("idem-123")
			.setSuccess(true)
			.setStatus("Succeeded")
			.setTransactionId("tx-123")
			.setProcessedAt("2026-01-02T03:04:05Z")
			.build());
		assertThat(observer.completed).isTrue();
		verify(paymentService).getPayment("idem-123");
	}

	@Test
	void rejectsBlankIdempotencyKey() {
		TestObserver<PaymentRecord> observer = new TestObserver<>();

		grpcService.getPayment(GetPaymentRequest.getDefaultInstance(), observer);

		assertThat(Status.fromThrowable(observer.error).getCode()).isEqualTo(Status.Code.INVALID_ARGUMENT);
		verifyNoInteractions(paymentService);
	}

	@Test
	void returnsNotFoundWhenPaymentDoesNotExist() {
		when(paymentService.getPayment("missing")).thenThrow(new PaymentNotFoundException("not found"));
		TestObserver<PaymentRecord> observer = new TestObserver<>();

		grpcService.getPayment(GetPaymentRequest.newBuilder().setIdempotencyKey("missing").build(), observer);

		assertThat(Status.fromThrowable(observer.error).getCode()).isEqualTo(Status.Code.NOT_FOUND);
	}

	private static final class TestObserver<T> implements StreamObserver<T> {
		private final List<T> values = new ArrayList<>();
		private Throwable error;
		private boolean completed;

		@Override
		public void onNext(T value) {
			values.add(value);
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
