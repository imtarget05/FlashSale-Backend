package com.flashsale.payment.audit;

import org.springframework.context.annotation.Profile;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@Profile("mongodb")
@RequestMapping("/api/audit/payments")
public class PaymentAuditController {

	private final PaymentAuditService auditService;

	public PaymentAuditController(PaymentAuditService auditService) {
		this.auditService = auditService;
	}

	@GetMapping("/{idempotencyKey}")
	@PreAuthorize("hasAnyRole('STAFF', 'ADMIN')")
	public ResponseEntity<PaymentAuditDocument> getPaymentAudit(@PathVariable String idempotencyKey) {
		return auditService.findByIdempotencyKey(idempotencyKey)
			.map(ResponseEntity::ok)
			.orElseGet(() -> ResponseEntity.notFound().build());
	}
}
