package com.flashsale.payment.provider;

import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import org.springframework.stereotype.Component;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.UUID;

/**
 * Adapter mô phỏng PSP cục bộ cho demo / portfolio. Quy tắc deterministic:
 * <ul>
 *   <li>cents == 1 → Succeeded</li>
 *   <li>cents == 2 → Declined</li>
 *   <li>cents == 3 → TimedOut (transient)</li>
 *   <li>ngược lại → Succeeded</li>
 * </ul>
 * <p><strong>Không phải PSP thật.</strong> Không có credential, hợp đồng, hoặc hành vi
 * bên ngoài nào được gọi. Dùng cho showcase kỹ thuật cục bộ.
 */
@Component
@org.springframework.boot.autoconfigure.condition.ConditionalOnExpression(
	"'${stripe.api.key:}'.trim().length() == 0")
public class SimulatedPaymentProvider implements PaymentProvider {

	@Override
	public PaymentRecordDto process(ProcessPaymentRequest request) {
		BigDecimal amount = request.amount();
		int cents = amount.remainder(BigDecimal.ONE).movePointRight(2).intValue();

		boolean success;
		String status;
		String transactionId;
		String failureReason;
		boolean isTransientError;

		if (cents == 2) {
			success = false;
			status = "Declined";
			transactionId = null;
			failureReason = "Insufficient funds in customer account";
			isTransientError = false;
		} else if (cents == 3) {
			success = false;
			status = "TimedOut";
			transactionId = null;
			failureReason = "Upstream payment gateway timeout";
			isTransientError = true;
		} else {
			success = true;
			status = "Succeeded";
			transactionId = "tx_" + UUID.randomUUID().toString().replace("-", "");
			failureReason = null;
			isTransientError = false;
		}

		return new PaymentRecordDto(
			request.idempotencyKey(),
			success,
			status,
			transactionId,
			failureReason,
			isTransientError,
			OffsetDateTime.now().toString()
		);
	}
}
