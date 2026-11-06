package com.flashsale.payment.unit;

import com.flashsale.payment.controller.PaymentController;
import com.flashsale.payment.security.AuthService;
import com.flashsale.payment.security.DemoUserDetailsConfig;
import com.flashsale.payment.security.JwtUtil;
import com.flashsale.payment.security.SecurityConfig;
import com.flashsale.payment.service.PaymentService;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.cache.CacheManager;
import org.springframework.cache.concurrent.ConcurrentMapCacheManager;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.security.test.web.servlet.setup.SecurityMockMvcConfigurers;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.context.WebApplicationContext;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@WebMvcTest(PaymentController.class)
@Import({SecurityConfig.class, DemoUserDetailsConfig.class, DefaultApiDocsSecurityTest.TestCacheConfiguration.class})
class DefaultApiDocsSecurityTest {
	@TestConfiguration(proxyBeanMethods = false)
	static class TestCacheConfiguration {
		@Bean
		CacheManager cacheManager() {
			return new ConcurrentMapCacheManager("payments");
		}
	}

	@Autowired
	private WebApplicationContext applicationContext;

	@MockitoBean
	private PaymentService paymentService;

	@MockitoBean
	private AuthService authService;

	@MockitoBean
	private JwtUtil jwtUtil;

	@Test
	void apiDocsRequireAuthenticationWithoutDemoProfile() throws Exception {
		MockMvc mockMvc = MockMvcBuilders.webAppContextSetup(applicationContext)
			.apply(SecurityMockMvcConfigurers.springSecurity())
			.build();
		mockMvc.perform(get("/v3/api-docs"))
			.andExpect(status().isUnauthorized());
	}
}
