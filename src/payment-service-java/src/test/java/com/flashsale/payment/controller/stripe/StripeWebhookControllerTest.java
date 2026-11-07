package com.flashsale.payment.controller.stripe;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import com.flashsale.payment.repository.PaymentRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;

import java.io.ByteArrayInputStream;
import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.Base64;
import java.util.HashMap;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.*;

@ExtendWith(MockitoExtension.class)
class StripeWebhookControllerTest {

    private static final String WEBHOOK_SECRET = "whsec_test_secret_12345";

    @Mock
    private PaymentRepository paymentRepository;

    @Mock
    private PaymentOutboxRepository outboxRepository;

    @Mock
    private ObjectMapper objectMapper;

    private StripeWebhookController controller;

    @BeforeEach
    void setUp() {
        controller = new StripeWebhookController(
                paymentRepository, outboxRepository, objectMapper, WEBHOOK_SECRET);
    }
    @Test
    @DisplayName("rejects request when webhook secret is not configured")
    void rejectWhenSecretNotConfigured() {
        StripeWebhookController noSecretController = new StripeWebhookController(
                paymentRepository, outboxRepository, objectMapper, "");

        ResponseEntity<?> result = noSecretController.handleStripeWebhook(
                "t=1234567890,v1=somesig", new ByteArrayInputStream("payload".getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.SERVICE_UNAVAILABLE);
    }

    @Test
    @DisplayName("rejects request when Stripe-Signature header is missing")
    void rejectWhenMissingSignature() {
        ResponseEntity<?> result = controller.handleStripeWebhook(null, new ByteArrayInputStream("payload".getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.BAD_REQUEST);
        assertThat(result.getBody().toString()).contains("missing_signature");
    }

    @Test
    @DisplayName("rejects request when signature is invalid")
    void rejectWhenInvalidSignature() {
        ResponseEntity<?> result = controller.handleStripeWebhook(
                "t=1234567890,v1=fakesig", new ByteArrayInputStream("{\"type\":\"payment_intent.succeeded\"}".getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.BAD_REQUEST);
        assertThat(result.getBody().toString()).contains("invalid_signature");
    }

    @Test
    @DisplayName("updates payment to Succeeded on payment_intent.succeeded")
    void updatePaymentOnSucceeded() throws Exception {
        Payment payment = new Payment(
                "idem-1", UUID.randomUUID(), 100, new BigDecimal("10.01"), "USD",
                false, "processing", "pi_test_123", null, false, OffsetDateTime.now());
        when(paymentRepository.findByTransactionId("pi_test_123")).thenReturn(Optional.of(payment));
        when(objectMapper.writeValueAsString(any())).thenReturn("{\"idempotencyKey\":\"idem-1\"}");

        Map<String, Object> evt = new HashMap<>();
        evt.put("type", "payment_intent.succeeded");
        Map<String, Object> data = new HashMap<>();
        Map<String, Object> obj = new HashMap<>();
        obj.put("id", "pi_test_123");
        obj.put("status", "succeeded");
        data.put("object", obj);
        evt.put("data", data);
        when(objectMapper.readValue(anyString(), eq(Map.class))).thenReturn(evt);

        String payload = "{\"type\":\"payment_intent.succeeded\",\"data\":{\"object\":{\"id\":\"pi_test_123\",\"status\":\"succeeded\"}}}";
        ResponseEntity<?> result = controller.handleStripeWebhook(createValidSignature(payload), new ByteArrayInputStream(payload.getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.OK);
        assertThat(payment.getSuccess()).isTrue();
        assertThat(payment.getStatus()).isEqualTo("Succeeded");
        verify(paymentRepository).save(payment);
        verify(outboxRepository).save(any());
    }

    @Test
    @DisplayName("updates payment to Declined on payment_intent.payment_failed")
    void updatePaymentOnFailed() throws Exception {
        Payment payment = new Payment(
                "idem-2", UUID.randomUUID(), 101, new BigDecimal("20.02"), "USD",
                false, "processing", "pi_test_456", null, false, OffsetDateTime.now());
        when(paymentRepository.findByTransactionId("pi_test_456")).thenReturn(Optional.of(payment));
        when(objectMapper.writeValueAsString(any())).thenReturn("{\"idempotencyKey\":\"idem-2\"}");

        Map<String, Object> evt = new HashMap<>();
        evt.put("type", "payment_intent.payment_failed");
        Map<String, Object> data = new HashMap<>();
        Map<String, Object> obj = new HashMap<>();
        obj.put("id", "pi_test_456");
        obj.put("status", "failed");
        data.put("object", obj);
        evt.put("data", data);
        when(objectMapper.readValue(anyString(), eq(Map.class))).thenReturn(evt);

        String payload = "{\"type\":\"payment_intent.payment_failed\",\"data\":{\"object\":{\"id\":\"pi_test_456\",\"status\":\"failed\"}}}";
        ResponseEntity<?> result = controller.handleStripeWebhook(createValidSignature(payload), new ByteArrayInputStream(payload.getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.OK);
        assertThat(payment.getSuccess()).isFalse();
        assertThat(payment.getStatus()).isEqualTo("Declined");
        verify(paymentRepository).save(payment);
    }

    @Test
    @DisplayName("does not revert already-succeeded payment on failed event")
    void doNotRevertAlreadySucceeded() throws Exception {
        Payment payment = new Payment(
                "idem-3", UUID.randomUUID(), 102, new BigDecimal("30.03"), "USD",
                true, "Succeeded", "pi_test_789", null, false, OffsetDateTime.now());
        when(paymentRepository.findByTransactionId("pi_test_789")).thenReturn(Optional.of(payment));

        Map<String, Object> evt = new HashMap<>();
        evt.put("type", "payment_intent.payment_failed");
        Map<String, Object> data = new HashMap<>();
        Map<String, Object> obj = new HashMap<>();
        obj.put("id", "pi_test_789");
        obj.put("status", "failed");
        data.put("object", obj);
        evt.put("data", data);
        when(objectMapper.readValue(anyString(), eq(Map.class))).thenReturn(evt);

        String payload = "{\"type\":\"payment_intent.payment_failed\",\"data\":{\"object\":{\"id\":\"pi_test_789\",\"status\":\"failed\"}}}";
        ResponseEntity<?> result = controller.handleStripeWebhook(createValidSignature(payload), new ByteArrayInputStream(payload.getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.OK);
        assertThat(payment.getSuccess()).isTrue();
        assertThat(payment.getStatus()).isEqualTo("Succeeded");
        verify(paymentRepository, never()).save(any());
        verify(outboxRepository, never()).save(any());
    }

    @Test
    @DisplayName("ignores unhandled event types")
    void ignoreUnhandledEvent() throws Exception {
        when(paymentRepository.findByTransactionId("pi_test_xyz")).thenReturn(Optional.of(
                new Payment("idem-4", UUID.randomUUID(), 103, new BigDecimal("5.00"), "USD",
                        false, "processing", "pi_test_xyz", null, false, OffsetDateTime.now())));

        Map<String, Object> evt = new HashMap<>();
        evt.put("type", "charge.refunded");
        Map<String, Object> data = new HashMap<>();
        Map<String, Object> obj = new HashMap<>();
        obj.put("id", "pi_test_xyz");
        obj.put("status", "refunded");
        data.put("object", obj);
        evt.put("data", data);
        when(objectMapper.readValue(anyString(), eq(Map.class))).thenReturn(evt);

        String payload = "{\"type\":\"charge.refunded\",\"data\":{\"object\":{\"id\":\"pi_test_xyz\",\"status\":\"refunded\"}}}";
        ResponseEntity<?> result = controller.handleStripeWebhook(createValidSignature(payload), new ByteArrayInputStream(payload.getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.OK);
        assertThat(result.getBody().toString()).contains("unhandled");
        verify(paymentRepository, never()).save(any());
    }

    @Test
    @DisplayName("rejects when signature timestamp is outside tolerance")
    void rejectWhenTimestampOutsideTolerance() throws Exception {
        long oldTimestamp = java.time.Instant.now().getEpochSecond() - 600;
        String sig = "t=" + oldTimestamp + ",v1=fakesig";

        ResponseEntity<?> result = controller.handleStripeWebhook(
                sig, new ByteArrayInputStream("{\"type\":\"payment_intent.succeeded\"}".getBytes()));

        assertThat(result.getStatusCode()).isEqualTo(HttpStatus.BAD_REQUEST);
        assertThat(result.getBody().toString()).contains("invalid_signature");
    }

    private String createValidSignature(String payload) throws Exception {
        long timestamp = java.time.Instant.now().getEpochSecond();
        String signedPayload = timestamp + "." + payload;
        String expected = hmacSha256Hex(WEBHOOK_SECRET, signedPayload);
        return "t=" + timestamp + ",v1=" + expected;
    }

    private String hmacSha256Hex(String key, String data) throws Exception {
        javax.crypto.Mac mac = javax.crypto.Mac.getInstance("HmacSHA256");
        mac.init(new javax.crypto.spec.SecretKeySpec(key.getBytes(java.nio.charset.StandardCharsets.UTF_8), "HmacSHA256"));
        byte[] hmac = mac.doFinal(data.getBytes(java.nio.charset.StandardCharsets.UTF_8));
        return Base64.getEncoder().encodeToString(hmac);
    }
}