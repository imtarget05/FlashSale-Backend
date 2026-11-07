package com.flashsale.payment.controller.stripe;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.outbox.PaymentOutbox;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import com.flashsale.payment.repository.PaymentRepository;
import com.flashsale.payment.config.RabbitConfig;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import jakarta.annotation.PostConstruct;
import java.nio.charset.StandardCharsets;
import java.security.InvalidKeyException;
import java.security.NoSuchAlgorithmException;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.Base64;
import java.util.Map;

@RestController
@RequestMapping("/api/webhooks/stripe")
public class StripeWebhookController {

    private static final Logger log = LoggerFactory.getLogger(StripeWebhookController.class);
    private static final int TOLERANCE_SECONDS = 300;

    private final PaymentRepository paymentRepository;
    private final PaymentOutboxRepository outboxRepository;
    private final ObjectMapper objectMapper;
    private final String webhookSecret;

    public StripeWebhookController(
            PaymentRepository paymentRepository,
            PaymentOutboxRepository outboxRepository,
            ObjectMapper objectMapper,
            @Value("${stripe.webhook.secret:}") String webhookSecret) {
        this.paymentRepository = paymentRepository;
        this.outboxRepository = outboxRepository;
        this.objectMapper = objectMapper;
        this.webhookSecret = webhookSecret;
    }

    @PostConstruct
    public void init() {
        if (webhookSecret == null || webhookSecret.isBlank()) {
            log.warn("[stripe-webhook] STRIPE_WEBHOOK_SECRET is not configured. Webhook verification will be SKIPPED.");
        } else {
            log.info("[stripe-webhook] Webhook secret configured. Endpoint ready.");
        }
    }
    @PostMapping(consumes = "application/json")
    public ResponseEntity<?> handleStripeWebhook(
            @RequestHeader(value = "Stripe-Signature", required = false) String signature,
            java.io.InputStream payload) {

        if (webhookSecret == null || webhookSecret.isBlank()) {
            log.error("[stripe-webhook] Webhook secret not configured; rejecting request.");
            return ResponseEntity.status(HttpStatus.SERVICE_UNAVAILABLE)
                    .body("{\"error\":\"webhook_not_configured\"}");
        }

        if (signature == null || signature.isBlank()) {
            log.warn("[stripe-webhook] Missing Stripe-Signature header.");
            return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                    .body("{\"error\":\"missing_signature\"}");
        }

        String body;
        try {
            body = new String(payload.readAllBytes(), StandardCharsets.UTF_8);
        } catch (Exception ex) {
            log.error("[stripe-webhook] Failed to read payload.", ex);
            return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                    .body("{\"error\":\"invalid_payload\"}");
        }

        if (!verifySignature(body, signature)) {
            log.warn("[stripe-webhook] Invalid signature. Rejecting webhook.");
            return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                    .body("{\"error\":\"invalid_signature\"}");
        }
        Map<String, Object> event;
        try {
            event = objectMapper.readValue(body, Map.class);
        } catch (Exception ex) {
            log.error("[stripe-webhook] Failed to parse event JSON.", ex);
            return ResponseEntity.status(HttpStatus.BAD_REQUEST)
                    .body("{\"error\":\"invalid_json\"}");
        }

        String type = (String) event.get("type");
        @SuppressWarnings("unchecked")
        Map<String, Object> data = (Map<String, Object>) event.get("data");
        @SuppressWarnings("unchecked")
        Map<String, Object> object = data != null ? (Map<String, Object>) data.get("object") : null;

        if (object == null) {
            log.warn("[stripe-webhook] Event {} has no data.object; ignoring.", type);
            return ResponseEntity.ok().body("{\"status\":\"ignored\"}");
        }

        String stripePaymentIntentId = (String) object.get("id");
        String status = (String) object.get("status");

        log.info("[stripe-webhook] Received event type={} paymentIntentId={} status={}",
                type, stripePaymentIntentId, status);

        var existing = paymentRepository.findByTransactionId(stripePaymentIntentId);
        if (existing.isEmpty()) {
            log.warn("[stripe-webhook] No payment found for transactionId={}; ignoring.", stripePaymentIntentId);
            return ResponseEntity.ok().body("{\"status\":\"not_found\"}");
        }

        Payment payment = existing.get();

        boolean updated = false;
        switch (type) {
            case "payment_intent.succeeded" -> {
                if (!Boolean.TRUE.equals(payment.getSuccess())) {
                    payment.setSuccess(true);
                    payment.setStatus("Succeeded");
                    payment.setFailureReason(null);
                    payment.setIsTransientError(false);
                    updated = true;
                }
            }
            case "payment_intent.payment_failed", "payment_intent.canceled" -> {
                if (Boolean.TRUE.equals(payment.getSuccess())) {
                    return ResponseEntity.ok().body("{\"status\":\"already_succeeded\"}");
                }
                payment.setSuccess(false);
                payment.setStatus("Declined");
                payment.setFailureReason("Payment " + type.replace("payment_intent.", ""));
                payment.setIsTransientError(false);
                updated = true;
            }
            default -> {
                log.info("[stripe-webhook] Unhandled event type={}; ignoring.", type);
                return ResponseEntity.ok().body("{\"status\":\"unhandled\"}");
            }
        }

        if (updated) {
            payment.setProcessedAt(OffsetDateTime.now());
            paymentRepository.save(payment);

            String routingKey = payment.getSuccess()
                    ? RabbitConfig.ROUTING_KEY_PAYMENT_COMPLETED
                    : RabbitConfig.ROUTING_KEY_PAYMENT_FAILED;
            try {
                String resultJson = objectMapper.writeValueAsString(
                        new com.flashsale.payment.dto.PaymentRecordDto(
                                payment.getIdempotencyKey(),
                                payment.getSuccess(),
                                payment.getStatus(),
                                payment.getTransactionId(),
                                payment.getFailureReason(),
                                payment.getIsTransientError(),
                                payment.getProcessedAt() != null ? payment.getProcessedAt().toString() : null
                        ));
                outboxRepository.save(new com.flashsale.payment.outbox.PaymentOutbox(
                        payment.getIdempotencyKey(), routingKey, resultJson, OffsetDateTime.now()));
            } catch (Exception ex) {
                log.error("[stripe-webhook] Failed to publish outbox event for {}.",
                        payment.getIdempotencyKey(), ex);
            }
        }

        return ResponseEntity.ok().body("{\"status\":\"processed\"}");
    }
    /**
     * XÃ¡c thá»¥c Stripe-Signature header.
     * <p>Format: {@code t=timestamp,v1=signature,v0=...}
     * Signature = HMAC-SHA256(webhookSecret, timestamp + "." + payload)
     */
    private boolean verifySignature(String payload, String signature) {
        try {
            long timestamp = -1;
            String v1 = null;

            for (String part : signature.split(",")) {
                String[] kv = part.split("=", 2);
                if (kv.length != 2) continue;
                String key = kv[0].trim();
                String value = kv[1].trim();
                if ("t".equals(key)) {
                    timestamp = Long.parseLong(value);
                } else if ("v1".equals(key)) {
                    v1 = value;
                }
            }

            if (timestamp < 0 || v1 == null) {
                log.warn("[stripe-webhook] Malformed signature header.");
                return false;
            }

            long now = Instant.now().getEpochSecond();
            if (Math.abs(now - timestamp) > TOLERANCE_SECONDS) {
                log.warn("[stripe-webhook] Signature timestamp outside tolerance. now={} ts={}",
                        now, timestamp);
                return false;
            }

            String signedPayload = timestamp + "." + payload;
            String expected = hmacSha256Hex(webhookSecret, signedPayload);

            return messageDigestEquals(expected, v1);
        } catch (Exception ex) {
            log.warn("[stripe-webhook] Signature verification failed.", ex);
            return false;
        }
    }

    private String hmacSha256Hex(String key, String data) throws NoSuchAlgorithmException, InvalidKeyException {
        javax.crypto.Mac mac = javax.crypto.Mac.getInstance("HmacSHA256");
        mac.init(new javax.crypto.spec.SecretKeySpec(key.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
        byte[] hmac = mac.doFinal(data.getBytes(StandardCharsets.UTF_8));
        return Base64.getEncoder().encodeToString(hmac);
    }

    private boolean messageDigestEquals(String a, String b) {
        if (a == null || b == null || a.length() != b.length()) {
            return false;
        }
        int result = 0;
        for (int i = 0; i < a.length(); i++) {
            result |= a.charAt(i) ^ b.charAt(i);
        }
        return result == 0;
    }
}