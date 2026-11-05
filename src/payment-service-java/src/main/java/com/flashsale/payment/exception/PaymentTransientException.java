package com.flashsale.payment.exception;

import com.flashsale.payment.entity.Payment;

public class PaymentTransientException extends RuntimeException {

	private final Payment payment;

	public PaymentTransientException(String message, Payment payment) {
		super(message);
		this.payment = payment;
	}

	public Payment getPayment() {
		return payment;
	}
}
