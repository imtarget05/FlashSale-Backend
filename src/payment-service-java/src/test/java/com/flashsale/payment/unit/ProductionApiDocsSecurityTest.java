package com.flashsale.payment.unit;

import com.flashsale.payment.controller.PaymentController;
import com.flashsale.payment.security.AuthService;
import com.flashsale.payment.security.DemoUserDetailsConfig;
import com.flashsale.payment.security.JwtUtil;
import com.flashsale.payment.security.ProductionSecurityConfig;
import com.flashsale.payment.security.SecurityConfig;
import com.flashsale.payment.service.PaymentService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.cache.CacheManager;
import org.springframework.cache.concurrent.ConcurrentMapCacheManager;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.core.Authentication;
import org.springframework.security.test.context.support.WithMockUser;
import org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors;
import org.springframework.security.test.web.servlet.setup.SecurityMockMvcConfigurers;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.context.WebApplicationContext;
import org.springframework.web.cors.CorsConfiguration;
import org.springframework.web.cors.CorsConfigurationSource;
import org.springframework.mock.web.MockHttpServletRequest;

import java.util.List;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.when;
import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@WebMvcTest(PaymentController.class)
@Import({SecurityConfig.class, ProductionSecurityConfig.class, DemoUserDetailsConfig.class, ProductionApiDocsSecurityTest.TestCacheConfiguration.class})
@ActiveProfiles("production")
@org.springframework.test.context.TestPropertySource(properties = {
	"app.security.oidc.issuer-uri=https://issuer.test",
	"app.security.oidc.audience=payment-service",
	"app.security.oidc.authorities-claim=roles",
	"app.security.allowed-origins=https://portal.example.test"
})
class ProductionApiDocsSecurityTest {
	@TestConfiguration(proxyBeanMethods = false)
	static class TestCacheConfiguration {
		@Bean
		CacheManager cacheManager() {
			return new ConcurrentMapCacheManager("payments");
		}

		@Bean
		JwtUtil jwtUtil(
				@Value("${app.jwt.secret:}") String signingKey,
				@Value("${app.jwt.expiration-ms:86400000}") long expirationMs) {
			return new JwtUtil(signingKey, expirationMs);
		}
	}

	@Autowired
	private WebApplicationContext applicationContext;

	@MockitoBean
	private PaymentService paymentService;

	@MockitoBean
	private AuthService authService;

	@Autowired
	private JwtUtil jwtUtil;

	@MockitoBean
	private JwtDecoder jwtDecoder;

	@Autowired
	private JwtAuthenticationConverter jwtAuthenticationConverter;

	@Autowired
	private CorsConfigurationSource corsConfigurationSource;

	private MockMvc mockMvc;

	@BeforeEach
	void setUpMockMvc() {
		mockMvc = MockMvcBuilders.webAppContextSetup(applicationContext)
			.apply(SecurityMockMvcConfigurers.springSecurity())
			.build();
	}

	@Test
	void apiDocsRequireAuthentication() throws Exception {
		mockMvc.perform(get("/v3/api-docs"))
			.andExpect(status().isUnauthorized());
	}

	@Test
	@WithMockUser(roles = "CUSTOMER")
	void swaggerUiRejectsCustomerRole() throws Exception {
		mockMvc.perform(get("/swagger-ui/index.html"))
			.andExpect(status().isForbidden());
	}

	@Test
	void oidcRoleClaimAuthorizesStaffPaymentList() throws Exception {
		when(paymentService.listPayments(any(Pageable.class))).thenReturn(Page.empty());

		mockMvc.perform(get("/api/payments")
				.with(SecurityMockMvcRequestPostProcessors.authentication(oidcAuthentication("STAFF"))))
			.andExpect(status().isOk());
	}

	@Test
	void productionBearerTokenUsesOidcDecoderWithoutLegacyHmacKey() throws Exception {
		when(jwtDecoder.decode("test-token")).thenReturn(oidcJwt("STAFF"));
		when(paymentService.listPayments(any(Pageable.class))).thenReturn(Page.empty());

		mockMvc.perform(get("/api/payments").header("Authorization", "Bearer test-token"))
			.andExpect(status().isOk());
	}

	@Test
	void oidcRoleClaimRejectsCustomerPaymentList() throws Exception {
		mockMvc.perform(get("/api/payments")
				.with(SecurityMockMvcRequestPostProcessors.authentication(oidcAuthentication("CUSTOMER"))))
			.andExpect(status().isForbidden());
	}

	@Test
	void productionRejectsLocalLoginEndpoint() throws Exception {
		mockMvc.perform(post("/api/auth/login"))
			.andExpect(status().isUnauthorized());
	}

	@Test
	void productionContextDoesNotRequireLegacyJwtSigningKey() {
		assertThat(jwtUtil).isNotNull();
	}

	@Test
	void productionCorsUsesOnlyConfiguredOriginsWithoutCredentials() {
		CorsConfiguration cors = corsConfigurationSource.getCorsConfiguration(
			new MockHttpServletRequest("GET", "/api/payments"));

		assertThat(cors).isNotNull();
		assertThat(cors.getAllowedOrigins()).containsExactly("https://portal.example.test");
		assertThat(cors.getAllowCredentials()).isFalse();
	}

	@Test
	void oidcRolesClaimIsMappedToSpringAuthorities() {
		assertThat(jwtAuthenticationConverter.convert(oidcJwt("STAFF")).getAuthorities())
			.extracting(authority -> authority.getAuthority())
			.contains("ROLE_STAFF");
	}

	private Authentication oidcAuthentication(String role) {
		return jwtAuthenticationConverter.convert(oidcJwt(role));
	}

	private Jwt oidcJwt(String role) {
		return Jwt.withTokenValue("test-token")
			.header("alg", "RS256")
			.claim("sub", "user@example.test")
			.claim("roles", List.of(role))
			.build();
	}
}
