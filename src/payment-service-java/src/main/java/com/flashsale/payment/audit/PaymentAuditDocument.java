package com.flashsale.payment.audit;

import org.springframework.data.annotation.Id;
import org.springframework.data.mongodb.core.mapping.Document;

@Document("payment_audit")
public record PaymentAuditDocument(
	@Id String idempotencyKey,
	boolean success,
	String status,
	String failureReason,
	boolean transientError,
	String processedAt
) {
}
