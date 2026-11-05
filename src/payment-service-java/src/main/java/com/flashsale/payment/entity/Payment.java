package com.flashsale.payment.entity;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.PostLoad;
import jakarta.persistence.PostPersist;
import jakarta.persistence.Table;
import jakarta.persistence.Transient;
import jakarta.persistence.Version;
import org.springframework.data.domain.Persistable;
import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.UUID;

@Entity
@Table(name = "payment_records")
public class Payment implements Persistable<String> {

	@Id
	@Column(name = "idempotency_key", length = 128, nullable = false)
	private String idempotencyKey;

	@Column(name = "payment_id", nullable = false)
	private UUID paymentId;

	@Column(name = "order_id", nullable = false)
	private Integer orderId;

	@Column(name = "amount", precision = 12, scale = 2, nullable = false)
	private BigDecimal amount;

	@Column(name = "currency", length = 8, nullable = false)
	private String currency;

	@Column(name = "success", nullable = false)
	private Boolean success;

	@Column(name = "status", length = 32, nullable = false)
	private String status;

	@Column(name = "transaction_id", length = 64)
	private String transactionId;

	@Column(name = "failure_reason", length = 256)
	private String failureReason;

	@Column(name = "is_transient_error", nullable = false)
	private Boolean isTransientError;

	@Column(name = "processed_at", nullable = false)
	private OffsetDateTime processedAt;

	@Version
	@Column(name = "version")
	private Long version;

	@Transient
	private boolean isNew = true;

	@Override
	public String getId() {
		return idempotencyKey;
	}

	@Override
	public boolean isNew() {
		return isNew;
	}

	@PostLoad
	@PostPersist
	void markPersisted() {
		isNew = false;
	}

	public Payment() {
	}

	public Payment(String idempotencyKey, UUID paymentId, Integer orderId, BigDecimal amount,
			String currency, Boolean success, String status, String transactionId, String failureReason,
			Boolean isTransientError, OffsetDateTime processedAt) {
		this.idempotencyKey = idempotencyKey;
		this.paymentId = paymentId;
		this.orderId = orderId;
		this.amount = amount;
		this.currency = currency;
		this.success = success;
		this.status = status;
		this.transactionId = transactionId;
		this.failureReason = failureReason;
		this.isTransientError = isTransientError;
		this.processedAt = processedAt;
	}

	public String getIdempotencyKey() {
		return idempotencyKey;
	}

	public void setIdempotencyKey(String idempotencyKey) {
		this.idempotencyKey = idempotencyKey;
	}

	public UUID getPaymentId() {
		return paymentId;
	}

	public void setPaymentId(UUID paymentId) {
		this.paymentId = paymentId;
	}

	public Integer getOrderId() {
		return orderId;
	}

	public void setOrderId(Integer orderId) {
		this.orderId = orderId;
	}

	public BigDecimal getAmount() {
		return amount;
	}

	public void setAmount(BigDecimal amount) {
		this.amount = amount;
	}

	public String getCurrency() {
		return currency;
	}

	public void setCurrency(String currency) {
		this.currency = currency;
	}

	public Boolean getSuccess() {
		return success;
	}

	public void setSuccess(Boolean success) {
		this.success = success;
	}

	public String getStatus() {
		return status;
	}

	public void setStatus(String status) {
		this.status = status;
	}

	public String getTransactionId() {
		return transactionId;
	}

	public void setTransactionId(String transactionId) {
		this.transactionId = transactionId;
	}

	public String getFailureReason() {
		return failureReason;
	}

	public void setFailureReason(String failureReason) {
		this.failureReason = failureReason;
	}

	public Boolean getIsTransientError() {
		return isTransientError;
	}

	public void setIsTransientError(Boolean isTransientError) {
		this.isTransientError = isTransientError;
	}

	public OffsetDateTime getProcessedAt() {
		return processedAt;
	}

	public void setProcessedAt(OffsetDateTime processedAt) {
		this.processedAt = processedAt;
	}

	public Long getVersion() {
		return version;
	}

	public void setVersion(Long version) {
		this.version = version;
	}
}
