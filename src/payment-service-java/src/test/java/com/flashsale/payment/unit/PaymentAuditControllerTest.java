package com.flashsale.payment.unit;

import com.flashsale.payment.audit.PaymentAuditController;
import com.flashsale.payment.audit.PaymentAuditDocument;
import com.flashsale.payment.audit.PaymentAuditService;
import com.flashsale.payment.security.DemoUserDetailsConfig;
import com.flashsale.payment.security.JwtUtil;
import com.flashsale.payment.security.SecurityConfig;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.BeforeEach;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.cache.CacheManager;
import org.springframework.cache.concurrent.ConcurrentMapCacheManager;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.security.test.context.support.WithMockUser;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.TestPropertySource;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.security.test.web.servlet.setup.SecurityMockMvcConfigurers;
import org.springframework.web.context.WebApplicationContext;

import java.util.Optional;

import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@WebMvcTest(PaymentAuditController.class)
@Import({SecurityConfig.class, DemoUserDetailsConfig.class, PaymentAuditControllerTest.TestCacheConfiguration.class})
@ActiveProfiles({"demo", "mongodb"})
@TestPropertySource(properties = {
	"demo.auth.password=test-password-for-audit",
	"spring.mongodb.uri=mongodb://localhost:27017/flashsale-test"
})
class PaymentAuditControllerTest {

	@TestConfiguration(proxyBeanMethods = false)
	static class TestCacheConfiguration {
		@Bean
		CacheManager cacheManager() {
			return new ConcurrentMapCacheManager("payments");
		}
	}

	@Autowired
	private WebApplicationContext applicationContext;
	private MockMvc mockMvc;

	@BeforeEach
	void setUpMockMvc() {
		mockMvc = MockMvcBuilders.webAppContextSetup(applicationContext)
			.apply(SecurityMockMvcConfigurers.springSecurity())
			.build();
	}

	@MockitoBean
	private PaymentAuditService auditService;

	@MockitoBean
	private JwtUtil jwtUtil;

	@Test
	@WithMockUser(roles = "STAFF")
	void returnsAuditProjectionForStaff() throws Exception {
		when(auditService.findByIdempotencyKey("idem-123")).thenReturn(Optional.of(
			new PaymentAuditDocument("idem-123", true, "Succeeded", null, false, "2026-01-02T03:04:05Z")));

		mockMvc.perform(get("/api/audit/payments/idem-123"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.idempotencyKey").value("idem-123"))
			.andExpect(jsonPath("$.status").value("Succeeded"));
	}

	@Test
	@WithMockUser(roles = "CUSTOMER")
	void deniesAuditProjectionToCustomers() throws Exception {
		mockMvc.perform(get("/api/audit/payments/idem-123"))
			.andExpect(status().isForbidden());
	}

	@Test
	@WithMockUser(roles = "STAFF")
	void returnsNotFoundWhenProjectionHasNotArrived() throws Exception {
		when(auditService.findByIdempotencyKey("idem-missing")).thenReturn(Optional.empty());

		mockMvc.perform(get("/api/audit/payments/idem-missing"))
			.andExpect(status().isNotFound());
	}
}
