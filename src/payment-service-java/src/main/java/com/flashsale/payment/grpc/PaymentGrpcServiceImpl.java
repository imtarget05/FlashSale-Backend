package com.flashsale.payment.grpc;

import com.flashsale.payment.grpc.PaymentProto.HealthCheckRequest;
import com.flashsale.payment.grpc.PaymentProto.HealthCheckResponse;
import com.flashsale.payment.grpc.PaymentProto.ProcessPaymentRequest;
import com.flashsale.payment.grpc.PaymentProto.ProcessPaymentResponse;
import com.flashsale.payment.grpc.PaymentGrpc.PaymentServiceImplBase;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.service.PaymentService;
import io.grpc.stub.StreamObserver;
import java.math.BigDecimal;
import java.util.UUID;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Configuration;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * gRPC payment service. Delegates to the existing {@link PaymentService} so that
 * business logic lives in one place. gRPC is used for service-to-service calls;
 * REST remains the client-facing API.
 */
@Configuration
public class PaymentGrpcServiceImpl extends PaymentServiceImplBase {

    private static final Logger log = LoggerFactory.getLogger(PaymentGrpcServiceImpl.class);

    private final PaymentService paymentService;

    public PaymentGrpcServiceImpl(PaymentService paymentService) {
        this.paymentService = paymentService;
    }

    @Override
    public void processPayment(ProcessPaymentRequest request, StreamObserver<ProcessPaymentResponse> responseObserver) {
        log.info("[grpc] processPayment started idempotencyKey={} orderId={}", request.getIdempotencyKey(), request.getOrderId());

        var dtoRequest = new com.flashsale.payment.dto.ProcessPaymentRequest(
            parseUuid(request.getPaymentId()),
            (int) request.getOrderId(),
            request.getIdempotencyKey(),
            new BigDecimal(request.getAmount()),
            request.getCurrency()
        );

        try {
            PaymentRecordDto result = paymentService.processPayment(dtoRequest);
            ProcessPaymentResponse response = ProcessPaymentResponse.newBuilder()
                .setIdempotencyKey(result.idempotencyKey())
                .setSuccess(result.success())
                .setStatus(result.status())
                .setTransactionId(result.transactionId() == null ? "" : result.transactionId())
                .setFailureReason(result.failureReason() == null ? "" : result.failureReason())
                .setIsTransientError(result.isTransientError())
                .setProcessedAt(result.processedAt() == null ? "" : result.processedAt())
                .build();
            responseObserver.onNext(response);
            responseObserver.onCompleted();
        } catch (Exception ex) {
            log.warn("[grpc] processPayment failed idempotencyKey={} reason={}", request.getIdempotencyKey(), ex.getMessage());
            responseObserver.onError(ex);
        }
    }

    @Override
    public void checkHealth(HealthCheckRequest request, StreamObserver<HealthCheckResponse> responseObserver) {
        responseObserver.onNext(
            HealthCheckResponse.newBuilder()
                .setStatus("healthy")
                .setService("payment-service")
                .build()
        );
        responseObserver.onCompleted();
    }

    private static UUID parseUuid(String value) {
        try {
            return UUID.fromString(value);
        } catch (IllegalArgumentException ex) {
            return UUID.randomUUID();
        }
    }
}
