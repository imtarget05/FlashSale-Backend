package com.flashsale.payment.dto;

public record AuthResponse(
	String token,
	String type
) {
}
