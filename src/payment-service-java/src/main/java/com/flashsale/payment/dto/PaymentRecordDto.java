package com.flashsale.payment.dto;

import com.fasterxml.jackson.annotation.JsonInclude;

@JsonInclude(JsonInclude.Include.NON_NULL)
public record PaymentRecordDto(
	String idempotencyKey,
	boolean success,
	String status,
	String transactionId,
	String failureReason,
	boolean isTransientError,
	String processedAt,
	String customerEmail
) {
	public PaymentRecordDto(
		String idempotencyKey,
		boolean success,
		String status,
		String transactionId,
		String failureReason,
		boolean isTransientError,
		String processedAt
	) {
		this(idempotencyKey, success, status, transactionId, failureReason, isTransientError, processedAt, null);
	}
}
