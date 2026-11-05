package com.flashsale.payment.controller;

import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.security.AuthService;
import com.flashsale.payment.service.PaymentService;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.responses.ApiResponses;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.validation.Valid;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.web.PageableDefault;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.*;

import java.util.HashMap;
import java.util.Map;

@RestController
@RequestMapping("/api")
@Tag(name = "Payment", description = "Payment processing API")
public class PaymentController {

	private final PaymentService paymentService;
	private final AuthService authService;

	public PaymentController(PaymentService paymentService, AuthService authService) {
		this.paymentService = paymentService;
		this.authService = authService;
	}

	@Operation(summary = "Process a payment", description = "Creates a payment with idempotency key. Cent value .01=succeeds, .02=declined, .03=transient timeout (504).")
	@ApiResponses({
		@ApiResponse(responseCode = "200", description = "Payment processed successfully"),
		@ApiResponse(responseCode = "400", description = "Validation error"),
		@ApiResponse(responseCode = "504", description = "Transient gateway timeout")
	})
	@PostMapping("/payments")
	public ResponseEntity<?> processPayment(@Valid @RequestBody ProcessPaymentRequest request) {
		PaymentRecordDto dto = paymentService.processPayment(request);
		if ("TimedOut".equals(dto.status())) {
			return ResponseEntity.status(HttpStatus.GATEWAY_TIMEOUT).body(dto);
		}
		return ResponseEntity.ok(dto);
	}

	@Operation(summary = "Get payment by idempotency key")
	@ApiResponses({
		@ApiResponse(responseCode = "200", description = "Payment found"),
		@ApiResponse(responseCode = "404", description = "Payment not found")
	})
	@GetMapping("/payments/{idempotencyKey}")
	@PreAuthorize("hasAnyRole('CUSTOMER', 'STAFF', 'ADMIN')")
	public ResponseEntity<PaymentRecordDto> getPayment(
		@Parameter(description = "Idempotency key of the payment") @PathVariable String idempotencyKey) {
		PaymentRecordDto dto = paymentService.getPayment(idempotencyKey);
		return ResponseEntity.ok(dto);
	}

	@Operation(summary = "List payments with pagination")
	@ApiResponses({
		@ApiResponse(responseCode = "200", description = "Paginated list of payments")
	})
	@GetMapping("/payments")
	@PreAuthorize("hasAnyRole('STAFF', 'ADMIN')")
	public ResponseEntity<Page<PaymentRecordDto>> listPayments(
		@PageableDefault(size = 20, page = 0) Pageable pageable) {
		Page<PaymentRecordDto> page = paymentService.listPayments(pageable);
		return ResponseEntity.ok(page);
	}

	@Operation(summary = "Authenticate and receive JWT token")
	@ApiResponses({
		@ApiResponse(responseCode = "200", description = "Authentication successful"),
		@ApiResponse(responseCode = "401", description = "Invalid credentials")
	})
	@PostMapping("/auth/login")
	public ResponseEntity<AuthResponse> login(@Valid @RequestBody LoginRequest request) {
		AuthResponse response = authService.authenticate(request);
		if (response == null) {
			return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build();
		}
		return ResponseEntity.ok(response);
	}

	@GetMapping("/health")
	public ResponseEntity<Map<String, String>> health() {
		Map<String, String> body = new HashMap<>();
		body.put("status", "Healthy");
		body.put("service", "payment-service");
		return ResponseEntity.ok(body);
	}

	@GetMapping("/health/live")
	public ResponseEntity<Map<String, String>> live() {
		Map<String, String> body = new HashMap<>();
		body.put("status", "healthy");
		return ResponseEntity.ok(body);
	}

	@GetMapping("/healthz")
	public ResponseEntity<Map<String, String>> healthz() {
		Map<String, String> body = new HashMap<>();
		body.put("status", "healthy");
		return ResponseEntity.ok(body);
	}
}
