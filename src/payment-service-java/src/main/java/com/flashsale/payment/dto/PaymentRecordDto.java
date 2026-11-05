package com.flashsale.payment.dto;

public record PaymentRecordDto(
	String idempotencyKey,
	boolean success,
	String status,
	String transactionId,
	String failureReason,
	boolean isTransientError,
	String processedAt
) {
}
