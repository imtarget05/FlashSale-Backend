package com.flashsale.payment.exception;

import com.flashsale.payment.dto.PaymentRecordDto;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.validation.FieldError;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

import java.util.HashMap;
import java.util.Map;

@RestControllerAdvice
public class GlobalExceptionHandler {
	private static final Logger log = LoggerFactory.getLogger(GlobalExceptionHandler.class);

	@ExceptionHandler(PaymentNotFoundException.class)
	public ResponseEntity<ErrorResponse> handlePaymentNotFound(PaymentNotFoundException ex) {
		return ResponseEntity.status(HttpStatus.NOT_FOUND)
			.body(new ErrorResponse("NOT_FOUND", ex.getMessage()));
	}

	@ExceptionHandler(IdempotencyConflictException.class)
	public ResponseEntity<ErrorResponse> handleIdempotencyConflict(IdempotencyConflictException ex) {
		return ResponseEntity.status(HttpStatus.CONFLICT)
			.body(new ErrorResponse("IDEMPOTENCY_CONFLICT", ex.getMessage()));
	}

	@ExceptionHandler(PaymentTransientException.class)
	public ResponseEntity<PaymentRecordDto> handlePaymentTransient(PaymentTransientException ex) {
		PaymentRecordDto dto = new PaymentRecordDto(
			ex.getPayment().getIdempotencyKey(),
			ex.getPayment().getSuccess(),
			ex.getPayment().getStatus(),
			ex.getPayment().getTransactionId(),
			ex.getPayment().getFailureReason(),
			ex.getPayment().getIsTransientError(),
			ex.getPayment().getProcessedAt() != null ? ex.getPayment().getProcessedAt().toString() : null
		);
		return ResponseEntity.status(HttpStatus.GATEWAY_TIMEOUT).body(dto);
	}

	@ExceptionHandler(MethodArgumentNotValidException.class)
	public ResponseEntity<ErrorResponse> handleValidation(MethodArgumentNotValidException ex) {
		Map<String, String> errors = new HashMap<>();
		ex.getBindingResult().getAllErrors().forEach((error) -> {
			String fieldName = ((FieldError) error).getField();
			String errorMessage = error.getDefaultMessage();
			errors.put(fieldName, errorMessage);
		});
		return ResponseEntity.status(HttpStatus.BAD_REQUEST)
			.body(new ErrorResponse("VALIDATION_ERROR", "Request validation failed", errors));
	}

	@ExceptionHandler(IllegalArgumentException.class)
	public ResponseEntity<ErrorResponse> handleIllegalArgument(IllegalArgumentException ex) {
		return ResponseEntity.status(HttpStatus.BAD_REQUEST)
			.body(new ErrorResponse("BAD_REQUEST", ex.getMessage()));
	}

	@ExceptionHandler(Exception.class)
	public ResponseEntity<ErrorResponse> handleGeneric(Exception ex) {
		log.error("Unhandled payment API exception", ex);
		return ResponseEntity.status(HttpStatus.INTERNAL_SERVER_ERROR)
			.body(new ErrorResponse("INTERNAL_ERROR", "An unexpected error occurred"));
	}

	public record ErrorResponse(String error, String message) {
		public ErrorResponse(String error, String message, Map<String, String> details) {
			this(error, message);
		}
	}
}
