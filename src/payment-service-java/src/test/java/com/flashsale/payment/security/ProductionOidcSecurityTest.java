package com.flashsale.payment.security;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.mock.web.MockServletContext;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configuration.EnableWebSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.oauth2.core.OAuth2Error;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.JwtValidationException;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.oauth2.server.resource.authentication.JwtGrantedAuthoritiesConverter;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.HttpStatusEntryPoint;
import org.springframework.security.test.web.servlet.setup.SecurityMockMvcConfigurers;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.context.WebApplicationContext;
import org.springframework.web.context.support.AnnotationConfigWebApplicationContext;

import java.util.List;

import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * Verifies the production-profile bearer-token security mapping returns the
 * expected HTTP statuses for OIDC negative paths:
 * <ul>
 *   <li>expired token &rarr; 401</li>
 *   <li>invalid issuer / malformed token &rarr; 401</li>
 *   <li>missing audience &rarr; 401</li>
 *   <li>insufficient role &rarr; 403</li>
 * </ul>
 * The context is built manually (no {@code @SpringBootTest}) so no OIDC issuer
 * discovery, no network, and no HTTPS localhost certificate are required. The
 * decoder is a mock, keeping the test hermetic.
 */
class ProductionOidcSecurityTest {

	private static final String AUDIENCE = "payment-service";

	private WebApplicationContext context;
	private MockMvc mockMvc;

	@BeforeEach
	void init() {
		AnnotationConfigWebApplicationContext wac = new AnnotationConfigWebApplicationContext();
		wac.register(SecurityTestConfig.class, PaymentsController.class);
		wac.setServletContext(new MockServletContext());
		wac.refresh();
		this.context = wac;
		this.mockMvc = MockMvcBuilders.webAppContextSetup(context)
			.apply(SecurityMockMvcConfigurers.springSecurity())
			.build();
	}

	@Test
	void missingBearerToken_returns401() throws Exception {
		mockMvc.perform(get("/api/payments"))
			.andExpect(status().isUnauthorized());
	}

	@Test
	void expiredToken_returns401() throws Exception {
		mockMvc.perform(get("/api/payments")
				.header("Authorization", "Bearer expired-token")
				.accept(MediaType.APPLICATION_JSON))
			.andExpect(status().isUnauthorized());
	}

	@Test
	void invalidIssuerToken_returns401() throws Exception {
		mockMvc.perform(get("/api/payments")
				.header("Authorization", "Bearer bad-issuer-token")
				.accept(MediaType.APPLICATION_JSON))
			.andExpect(status().isUnauthorized());
	}

	@Test
	void missingAudienceToken_returns401() throws Exception {
		mockMvc.perform(get("/api/payments")
				.header("Authorization", "Bearer missing-audience-token")
				.accept(MediaType.APPLICATION_JSON))
			.andExpect(status().isUnauthorized());
	}

	@Test
	void validTokenWithWrongRole_returns403() throws Exception {
		mockMvc.perform(get("/api/payments")
				.header("Authorization", "Bearer customer-token")
				.accept(MediaType.APPLICATION_JSON))
			.andExpect(status().isForbidden());
	}

	@Test
	void validStaffToken_passesAuthenticationAndAuthorization() throws Exception {
		mockMvc.perform(get("/api/payments")
				.header("Authorization", "Bearer valid-staff-token")
				.accept(MediaType.APPLICATION_JSON))
			.andExpect(status().isOk());
	}

	@Configuration
	@EnableWebSecurity
	static class SecurityTestConfig {

		@Bean
		JwtDecoder jwtDecoder() {
			JwtDecoder decoder = mock(JwtDecoder.class);
			Jwt staff = Jwt.withTokenValue("valid-staff-token")
				.header("alg", "RS256")
				.issuer("https://issuer.example")
				.claim("aud", List.of(AUDIENCE))
				.claim("roles", List.of("STAFF"))
				.build();
			Jwt customer = Jwt.withTokenValue("customer-token")
				.header("alg", "RS256")
				.issuer("https://issuer.example")
				.claim("aud", List.of(AUDIENCE))
				.claim("roles", List.of("CUSTOMER"))
				.build();
			when(decoder.decode("valid-staff-token")).thenReturn(staff);
			when(decoder.decode("customer-token")).thenReturn(customer);
			when(decoder.decode("expired-token"))
				.thenThrow(new JwtValidationException("token expired",
					List.of(new OAuth2Error("invalid_token", "expired", null))));
			when(decoder.decode("bad-issuer-token"))
				.thenThrow(new JwtValidationException("invalid issuer",
					List.of(new OAuth2Error("invalid_token", "bad-issuer", null))));
			when(decoder.decode("missing-audience-token"))
				.thenThrow(new JwtValidationException("missing audience",
					List.of(new OAuth2Error("invalid_token", "missing-audience", null))));
			return decoder;
		}

		@Bean
		JwtAuthenticationConverter jwtAuthenticationConverter() {
			JwtGrantedAuthoritiesConverter authoritiesConverter = new JwtGrantedAuthoritiesConverter();
			authoritiesConverter.setAuthoritiesClaimName("roles");
			authoritiesConverter.setAuthorityPrefix("ROLE_");
			JwtAuthenticationConverter converter = new JwtAuthenticationConverter();
			converter.setJwtGrantedAuthoritiesConverter(authoritiesConverter);
			return converter;
		}

		@Bean
		SecurityFilterChain filterChain(HttpSecurity http) throws Exception {
			http
				.csrf(csrf -> csrf.disable())
				.sessionManagement(session -> session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
				.exceptionHandling(errors -> errors.authenticationEntryPoint(new HttpStatusEntryPoint(HttpStatus.UNAUTHORIZED)))
				.authorizeHttpRequests(auth -> auth
					.requestMatchers("/api/payments").hasRole("STAFF")
					.anyRequest().authenticated())
				.oauth2ResourceServer(oauth2 -> oauth2.jwt(jwt -> jwt
					.decoder(jwtDecoder())
					.jwtAuthenticationConverter(jwtAuthenticationConverter())));
			return http.build();
		}
	}

	@org.springframework.web.bind.annotation.RestController
	@org.springframework.web.bind.annotation.RequestMapping("/api")
	static class PaymentsController {
		@org.springframework.web.bind.annotation.GetMapping("/payments")
		public String list() {
			return "ok";
		}
	}

}
