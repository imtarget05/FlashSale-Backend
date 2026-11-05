package com.flashsale.payment.outbox;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

import java.time.OffsetDateTime;
import java.util.UUID;

@Entity
@Table(name = "payment_outbox")
public class PaymentOutbox {

	@Id
	@GeneratedValue(strategy = GenerationType.UUID)
	private UUID id;

	@Column(name = "idempotency_key", nullable = false, unique = true, length = 128)
	private String idempotencyKey;

	@Column(name = "routing_key", nullable = false, length = 64)
	private String routingKey;

	@Column(name = "payload", nullable = false, columnDefinition = "TEXT")
	private String payload;

	@Column(name = "created_at", nullable = false)
	private OffsetDateTime createdAt;

	@Column(name = "next_attempt_at", nullable = false)
	private OffsetDateTime nextAttemptAt;

	@Column(name = "published_at")
	private OffsetDateTime publishedAt;

	@Column(name = "attempts", nullable = false)
	private int attempts;

	@Column(name = "last_error", columnDefinition = "TEXT")
	private String lastError;

	protected PaymentOutbox() {
	}

	public PaymentOutbox(String idempotencyKey, String routingKey, String payload, OffsetDateTime createdAt) {
		this.idempotencyKey = idempotencyKey;
		this.routingKey = routingKey;
		this.payload = payload;
		this.createdAt = createdAt;
		this.nextAttemptAt = createdAt;
	}

	public void markPublished(OffsetDateTime publishedAt) {
		this.publishedAt = publishedAt;
		this.lastError = null;
	}

	public void recordFailure(OffsetDateTime failedAt, Throwable failure) {
		attempts++;
		long delaySeconds = Math.min(300L, 1L << Math.min(attempts - 1, 8));
		nextAttemptAt = failedAt.plusSeconds(delaySeconds);
		String message = failure.getMessage();
		lastError = message == null
			? failure.getClass().getSimpleName()
			: message.substring(0, Math.min(1000, message.length()));
	}

	public UUID getId() {
		return id;
	}

	public String getIdempotencyKey() {
		return idempotencyKey;
	}

	public String getRoutingKey() {
		return routingKey;
	}

	public String getPayload() {
		return payload;
	}

	public OffsetDateTime getCreatedAt() {
		return createdAt;
	}

	public OffsetDateTime getNextAttemptAt() {
		return nextAttemptAt;
	}

	public void setNextAttemptAt(OffsetDateTime nextAttemptAt) {
		this.nextAttemptAt = nextAttemptAt;
	}

	public OffsetDateTime getPublishedAt() {
		return publishedAt;
	}

	public int getAttempts() {
		return attempts;
	}

	public String getLastError() {
		return lastError;
	}
}
