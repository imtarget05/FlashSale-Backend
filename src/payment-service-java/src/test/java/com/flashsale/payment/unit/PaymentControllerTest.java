package com.flashsale.payment.unit;

import com.flashsale.payment.controller.PaymentController;
import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.security.AuthService;
import com.flashsale.payment.security.JwtUtil;
import com.flashsale.payment.security.SecurityConfig;
import com.flashsale.payment.security.DemoUserDetailsConfig;
import com.flashsale.payment.service.PaymentService;
import org.junit.jupiter.api.Test;
import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.WebMvcTest;
import org.springframework.boot.test.mock.mockito.MockBean;
import org.springframework.context.annotation.Import;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.PageImpl;
import org.springframework.data.domain.PageRequest;
import org.springframework.http.MediaType;
import org.springframework.security.test.context.support.WithMockUser;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.context.ActiveProfiles;

import java.math.BigDecimal;
import java.util.List;
import java.util.UUID;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

@WebMvcTest(PaymentController.class)
@Import({SecurityConfig.class, DemoUserDetailsConfig.class})
@ActiveProfiles("dev")
class PaymentControllerTest {

	@Autowired
	private MockMvc mockMvc;

	@MockBean
	private PaymentService paymentService;

	@MockBean
	private AuthService authService;

	@MockBean
	private JwtUtil jwtUtil;

	@Test
	@WithMockUser(roles = "STAFF")
	void processPayment_shouldReturnCreated_whenValidRequest() throws Exception {
		PaymentRecordDto dto = new PaymentRecordDto("idem-1", true, "Succeeded", "tx_123", null, false, "2024-01-01T00:00:00Z");
		when(paymentService.processPayment(any(ProcessPaymentRequest.class))).thenReturn(dto);

		String json = """
			{
			  "paymentId": "%s",
			  "orderId": 100,
			  "idempotencyKey": "idem-1",
			  "amount": 10.01,
			  "currency": "USD"
			}
			""".formatted(UUID.randomUUID());

		mockMvc.perform(post("/api/payments")
				.contentType(MediaType.APPLICATION_JSON)
				.content(json))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.success").value(true))
			.andExpect(jsonPath("$.status").value("Succeeded"));
	}

	@Test
	@WithMockUser(roles = "STAFF")
	void processPayment_shouldReturnBadRequest_whenMissingIdempotencyKey() throws Exception {
		String json = """
			{
			  "paymentId": "%s",
			  "orderId": 100,
			  "idempotencyKey": "",
			  "amount": 10.01,
			  "currency": "USD"
			}
			""".formatted(UUID.randomUUID());

		mockMvc.perform(post("/api/payments")
				.contentType(MediaType.APPLICATION_JSON)
				.content(json))
			.andExpect(status().isBadRequest());
	}

	@Test
	void processPayment_shouldReturnUnauthorized_whenAnonymous() throws Exception {
		mockMvc.perform(post("/api/payments")
				.contentType(MediaType.APPLICATION_JSON)
				.content("{}"))
			.andExpect(status().isUnauthorized());
	}

	@Test
	@WithMockUser(roles = "CUSTOMER")
	void processPayment_shouldReturnForbidden_whenCustomer() throws Exception {
		mockMvc.perform(post("/api/payments")
				.contentType(MediaType.APPLICATION_JSON)
				.content("{}"))
			.andExpect(status().isForbidden());
	}

	@Test
	@WithMockUser(roles = "CUSTOMER")
	void getPayment_shouldReturnOk_whenAuthenticated() throws Exception {
		PaymentRecordDto dto = new PaymentRecordDto("idem-1", true, "Succeeded", "tx_123", null, false, "2024-01-01T00:00:00Z");
		when(paymentService.getPayment("idem-1")).thenReturn(dto);

		mockMvc.perform(get("/api/payments/idem-1"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.success").value(true));
	}

	@Test
	void getPayment_shouldReturnUnauthorized_whenNotAuthenticated() throws Exception {
		mockMvc.perform(get("/api/payments/idem-1"))
			.andExpect(status().isUnauthorized());
	}

	@Test
	@WithMockUser(roles = "ADMIN")
	void login_shouldReturnToken() throws Exception {
		when(authService.authenticate(any(LoginRequest.class)))
			.thenReturn(new AuthResponse("token-123", "Bearer"));

		String json = """
			{
			  "email": "admin@flashsale.local",
			  "password": "password123"
			}
			""";

		mockMvc.perform(post("/api/auth/login")
				.contentType(MediaType.APPLICATION_JSON)
				.content(json))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.token").value("token-123"));
	}

	@Test
	void login_shouldReturnUnauthorized_whenCredentialsAreInvalid() throws Exception {
		when(authService.authenticate(any(LoginRequest.class)))
			.thenThrow(new BadCredentialsException("Bad credentials"));

		String json = """
			{
			  "email": "admin@flashsale.local",
			  "password": "wrong-password"
			}
			""";

		mockMvc.perform(post("/api/auth/login")
				.contentType(MediaType.APPLICATION_JSON)
				.content(json))
			.andExpect(status().isUnauthorized());
	}

	@Test
	void listPayments_shouldReturnOk_whenJwtTokenIsValidForStaff() throws Exception {
		Page<PaymentRecordDto> page = new PageImpl<>(List.of(), PageRequest.of(0, 20), 0);
		when(jwtUtil.extractUsername("jwt-staff-token")).thenReturn("staff@flashsale.local");
		when(jwtUtil.isTokenValid(anyString(), any())).thenReturn(true);
		when(paymentService.listPayments(any(org.springframework.data.domain.Pageable.class))).thenReturn(page);

		mockMvc.perform(get("/api/payments")
				.header("Authorization", "Bearer jwt-staff-token"))
			.andExpect(status().isOk());
	}

	@Test
	@WithMockUser(roles = "ADMIN")
	void listPayments_shouldReturnPage_whenAdmin() throws Exception {
		Page<PaymentRecordDto> page = new PageImpl<>(List.of(), PageRequest.of(0, 20), 0);
		when(paymentService.listPayments(any(org.springframework.data.domain.Pageable.class))).thenReturn(page);

		mockMvc.perform(get("/api/payments"))
			.andExpect(status().isOk());
	}

	@Test
	@WithMockUser(roles = "CUSTOMER")
	void listPayments_shouldReturnForbidden_whenCustomer() throws Exception {
		mockMvc.perform(get("/api/payments"))
			.andExpect(status().isForbidden());
	}
}
