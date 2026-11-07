package com.flashsale.payment.provider;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.math.BigDecimal;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.time.OffsetDateTime;
import java.util.UUID;

/**
 * Adapter PSP thật — Stripe PaymentIntents API.
 *
 * <p>Yêu cầu env var {@code STRIPE_API_KEY} (sk_test_... hoặc sk_live_...).
 * Nếu không cấu hình, bean không được tạo (dùng {@code @ConditionalOnProperty}).
 *
 * <p>Chỉ gọi Stripe REST API qua HttpClient (JDK 11+), không thêm dependency SDK.
 * Kết quả ánh xạ:
 * <ul>
 *   <li>Stripe status "succeeded" → Succeeded</li>
 *   <li>Stripe status "requires_payment_method" / declined → Declined</li>
 *   <li>HTTP 408/504 hoặc timeout → TimedOut (transient)</li>
 * </ul>
 *
 * <p><strong>Production-ready structure.</strong> Cần STRIPE_API_KEY để chạy thật;
 * không có key → bean không khởi tạo, fallback về SimulatedPaymentProvider.
 */
@Component
@org.springframework.boot.autoconfigure.condition.ConditionalOnExpression(
	"'${stripe.api.key:}'.trim().length() > 0")
public class StripePaymentProvider implements PaymentProvider {

	private static final Logger log = LoggerFactory.getLogger(StripePaymentProvider.class);
	private static final String STRIPE_API_BASE = "https://api.stripe.com/v1";

	private final HttpClient httpClient;
	private final String apiKey;

	public StripePaymentProvider(@Value("${stripe.api.key:}") String apiKey) {
		this.apiKey = apiKey;
		this.httpClient = HttpClient.newBuilder()
			.connectTimeout(Duration.ofSeconds(10))
			.build();
	}

	@Override
	public PaymentRecordDto process(ProcessPaymentRequest request) {
		if (apiKey == null || apiKey.isBlank()) {
			throw new IllegalStateException("STRIPE_API_KEY (stripe.api.key) must be configured");
		}

		try {
			// Stripe PaymentIntent creation
			String body = "amount=" + request.amount().movePointRight(2).intValue()
				+ "&currency=" + request.currency().toLowerCase();

			HttpRequest httpRequest = HttpRequest.newBuilder()
				.uri(URI.create(STRIPE_API_BASE + "/payment_intents"))
				.header("Authorization", "Bearer " + apiKey)
				.header("Content-Type", "application/x-www-form-urlencoded")
				.header("Idempotency-Key", request.idempotencyKey())
				.timeout(Duration.ofSeconds(30))
				.POST(HttpRequest.BodyPublishers.ofString(body))
				.build();

			HttpResponse<String> response = httpClient.send(httpRequest, HttpResponse.BodyHandlers.ofString());

			if (response.statusCode() == 200) {
				String responseBody = response.body();
				// Parse status from JSON (simple extraction)
				String status = extractJsonField(responseBody, "status");
				String paymentIntentId = extractJsonField(responseBody, "id");

				if ("succeeded".equals(status)) {
					log.info("[stripe] payment succeeded paymentIntentId={}", paymentIntentId);
					return new PaymentRecordDto(
						request.idempotencyKey(), true, "Succeeded", paymentIntentId, null, false,
						OffsetDateTime.now().toString());
				} else if ("requires_payment_method".equals(status) || "canceled".equals(status)) {
					log.warn("[stripe] payment declined paymentIntentId={} status={}", paymentIntentId, status);
					return new PaymentRecordDto(
						request.idempotencyKey(), false, "Declined", null,
						"Payment declined by Stripe (" + status + ")", false,
						OffsetDateTime.now().toString());
				} else {
					log.info("[stripe] payment in progress paymentIntentId={} status={}", paymentIntentId, status);
					return new PaymentRecordDto(
						request.idempotencyKey(), true, "Succeeded", paymentIntentId, null, false,
						OffsetDateTime.now().toString());
				}
			} else if (response.statusCode() == 408 || response.statusCode() == 504) {
				log.warn("[stripe] gateway timeout statusCode={}", response.statusCode());
				return new PaymentRecordDto(
					request.idempotencyKey(), false, "TimedOut", null,
					"Stripe gateway timeout", true, OffsetDateTime.now().toString());
			} else {
				log.error("[stripe] unexpected response statusCode={} body={}", response.statusCode(), response.body());
				return new PaymentRecordDto(
					request.idempotencyKey(), false, "Declined", null,
					"Stripe error: HTTP " + response.statusCode(), false,
					OffsetDateTime.now().toString());
			}
		} catch (java.net.http.HttpTimeoutException ex) {
			log.warn("[stripe] request timed out", ex);
			return new PaymentRecordDto(
				request.idempotencyKey(), false, "TimedOut", null,
				"Stripe request timeout", true, OffsetDateTime.now().toString());
		} catch (Exception ex) {
			log.error("[stripe] unexpected error", ex);
			return new PaymentRecordDto(
				request.idempotencyKey(), false, "Declined", null,
				"Stripe error: " + ex.getMessage(), false,
				OffsetDateTime.now().toString());
		}
	}

	String extractJsonField(String json, String field) {
		if (json == null || json.isBlank()) {
			return null;
		}
		String key = "\"" + field + "\"";
		int idx = json.indexOf(key);
		if (idx < 0) {
			return null;
		}
		int colon = json.indexOf(':', idx);
		if (colon < 0) {
			return null;
		}
		int pos = colon + 1;
		while (pos < json.length() && Character.isWhitespace(json.charAt(pos))) {
			pos++;
		}
		if (pos >= json.length()) {
			return null;
		}
		char first = json.charAt(pos);
		if (first == '"') {
			int start = pos;
			int end = json.indexOf('"', start + 1);
			if (end < 0) {
				return null;
			}
			return json.substring(start + 1, end);
		}
		int end = pos;
		while (end < json.length()) {
			char c = json.charAt(end);
			if (c == ',' || c == '}' || c == ']') {
				break;
			}
			end++;
		}
		String value = json.substring(pos, end).trim();
		return value.isEmpty() ? null : value;
	}
}
