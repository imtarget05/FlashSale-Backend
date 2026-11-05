package com.flashsale.payment.dto;

import jakarta.validation.constraints.DecimalMin;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Positive;
import jakarta.validation.constraints.Size;
import java.math.BigDecimal;
import java.util.UUID;

public record ProcessPaymentRequest(
	@NotNull UUID paymentId,
	@NotNull Integer orderId,
	@NotBlank
	@Size(max = 128) String idempotencyKey,
	@NotNull
	@DecimalMin(value = "0.01", inclusive = true) BigDecimal amount,
	@NotBlank
	@Size(max = 8) String currency
) {
}
