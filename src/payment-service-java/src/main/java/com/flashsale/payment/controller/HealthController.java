package com.flashsale.payment.controller;

import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

import java.util.Map;

@RestController
public class HealthController {
	@GetMapping("/health")
	public ResponseEntity<Map<String, String>> health() {
		return ResponseEntity.ok(Map.of("status", "Healthy", "service", "payment-service"));
	}

	@GetMapping({"/health/live", "/healthz"})
	public ResponseEntity<Map<String, String>> live() {
		return ResponseEntity.ok(Map.of("status", "healthy"));
	}
}
