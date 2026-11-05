package com.flashsale.payment.exception;

import org.springframework.http.HttpStatus;
import org.springframework.web.server.ResponseStatusException;

public class PaymentNotFoundException extends ResponseStatusException {

	public PaymentNotFoundException(String message) {
		super(HttpStatus.NOT_FOUND, message);
	}
}
