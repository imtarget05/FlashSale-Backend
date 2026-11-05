package com.flashsale.payment.event;

import java.math.BigDecimal;
import java.time.OffsetDateTime;

public record OrderEventDTO(
	String orderId,
	String customerId,
	BigDecimal amount,
	String currency,
	String eventType,
	OffsetDateTime timestamp
) {
}
