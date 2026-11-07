package com.flashsale.payment.controller;

import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.security.AuthService;
import com.flashsale.payment.security.RateLimiter;
import com.flashsale.payment.service.PaymentService;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.Parameter;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import io.swagger.v3.oas.annotations.responses.ApiResponses;
import io.swagger.v3.oas.annotations.tags.Tag;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.validation.Valid;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.web.PageableDefault;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.*;


@RestController
@RequestMapping("/api")
@Tag(name = "Payment", description = "Payment processing API")
public class PaymentController {

	private final PaymentService paymentService;
	private final AuthService authService;
	private final RateLimiter rateLimiter;

	public PaymentController(PaymentService paymentService, AuthService authService,
			RateLimiter rateLimiter) {
		this.paymentService = paymentService;
		this.authService = authService;
		this.rateLimiter = rateLimiter;
	}

	@Operation(summary = "Process a payment", description = "Creates a payment with idempotency key. Cent value .01=succeeds, .02=declined, .03=transient timeout (504). Rate limited to 100 requests per minute per client IP.")
	@ApiResponses({
		@ApiResponse(responseCode = "200", description = "Payment processed successfully"),
		@ApiResponse(responseCode = "400", description = "Validation error"),
		@ApiResponse(responseCode = "429", description = "Rate limit exceeded"),
		@ApiResponse(responseCode = "504", description = "Transient gateway timeout")
	})
	@PostMapping("/payments")
	public ResponseEntity<?> processPayment(HttpServletRequest httpRequest,
			@Valid @RequestBody ProcessPaymentRequest request) {
		String clientId = httpRequest.getRemoteAddr();
		if (clientId == null || clientId.isBlank()) {
			clientId = "anonymous";
		}
		if (!rateLimiter.allowRequest(clientId)) {
			return ResponseEntity.status(HttpStatus.TOO_MANY_REQUESTS)
				.body("{\"error\":\"RATE_LIMITED\",\"message\":\"Rate limit exceeded. Try again later.\"}");
		}
		PaymentRecordDto dto = paymentService.processPayment(request);
		if ("TimedOut".equals(dto.status())) {
			return ResponseEntity.status(HttpStatus.GATEWAY_TIMEOUT).body(dto);
		}
		return ResponseEntity.ok(dto);
	}

	@Operation(summary = "Get payment by idempotency key")
	@ApiResponses({
		@ApiResponse(responseCode = "200", description = "Payment found"),
		@ApiResponse(responseCode = "403", description = "Payment does not belong to the authenticated user"),
		@ApiResponse(responseCode = "404", description = "Payment not found")
	})
	@GetMapping("/payments/{idempotencyKey}")
	@PreAuthorize("hasAnyRole('CUSTOMER', 'STAFF', 'ADMIN')")
	public ResponseEntity<PaymentRecordDto> getPayment(
		@Parameter(description = "Idempotency key of the payment") @PathVariable String idempotencyKey,
		Authentication authentication) {
		PaymentRecordDto dto = paymentService.getPayment(idempotencyKey);
		if (dto != null && dto.customerEmail() != null && authentication != null) {
			boolean isPrivileged = authentication.getAuthorities().stream()
				.anyMatch(a -> "ROLE_STAFF".equals(a.getAuthority()) || "ROLE_ADMIN".equals(a.getAuthority()));
			if (!isPrivileged && !dto.customerEmail().equals(authentication.getName())) {
				throw new AccessDeniedException(
					"Payment '" + idempotencyKey + "' does not belong to the authenticated user");
			}
		}
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

}

