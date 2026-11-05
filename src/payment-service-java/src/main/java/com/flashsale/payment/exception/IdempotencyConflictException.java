package com.flashsale.payment.exception;

public class IdempotencyConflictException extends RuntimeException {
	public IdempotencyConflictException(String message) {
		super(message);
	}
}
