package com.flashsale.payment.security;

import org.junit.jupiter.api.Test;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.web.cors.CorsConfiguration;
import org.springframework.web.servlet.config.annotation.CorsRegistry;

import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class ProductionOidcConfigTest {

	private final ProductionSecurityConfig config = new ProductionSecurityConfig();

	@Test
	void requiresAnIssuerForProduction() {
		assertThatThrownBy(() -> config.oidcJwtDecoder("", "payment-service"))
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("OIDC_ISSUER_URI must be configured");
	}

	@Test
	void rejectsNonHttpsIssuer() {
		assertThatThrownBy(() -> config.oidcJwtDecoder("http://issuer.example", "payment-service"))
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("absolute HTTPS issuer URL");
	}

	@Test
	void requiresAnAbsoluteHttpsIssuerWithoutUserInfoQueryOrFragment() {
		assertThatThrownBy(() -> ProductionSecurityConfig.requireHttpsIssuer(null))
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("must be configured");

		for (String issuer : List.of(
				"https:/issuer.example",
				"https://user:password@issuer.example",
				"https://issuer.example?tenant=one",
				"https://issuer.example#fragment")) {
			assertThatThrownBy(() -> ProductionSecurityConfig.requireHttpsIssuer(issuer))
				.as("issuer %s", issuer)
				.isInstanceOf(IllegalStateException.class)
				.hasMessageContaining("absolute HTTPS issuer URL");
		}
	}

	@Test
	void acceptsAnAbsoluteHttpsIssuer() {
		assertThat(ProductionSecurityConfig.requireHttpsIssuer("https://issuer.example/tenant"))
			.isEqualTo("https://issuer.example/tenant");
	}

	@Test
	void requiresAnExpectedAudience() {
		assertThatThrownBy(() -> ProductionSecurityConfig.requireAudience(token -> null, ""))
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("OIDC_AUDIENCE must be configured");

		assertThatThrownBy(() -> ProductionSecurityConfig.requireAudience(token -> null, null))
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("OIDC_AUDIENCE must be configured");
	}

	@Test
	void rejectsTokensWithoutTheConfiguredAudience() {
		Jwt jwt = Jwt.withTokenValue("test-token")
			.header("alg", "RS256")
			.claim("aud", List.of("another-service"))
			.build();
		JwtDecoder decoder = ProductionSecurityConfig.requireAudience(token -> jwt, "payment-service");

		assertThatThrownBy(() -> decoder.decode("test-token"))
			.isInstanceOf(org.springframework.security.oauth2.jwt.JwtValidationException.class)
			.hasMessageContaining("Invalid token audience");
	}

	@Test
	void rejectsTokensWithoutAnAudienceClaim() {
		Jwt jwt = org.mockito.Mockito.mock(Jwt.class);
		JwtDecoder decoder = ProductionSecurityConfig.requireAudience(token -> jwt, "payment-service");

		assertThatThrownBy(() -> decoder.decode("test-token"))
			.isInstanceOf(org.springframework.security.oauth2.jwt.JwtValidationException.class)
			.hasMessageContaining("Invalid token audience");
	}

	@Test
	void acceptsTokensForTheConfiguredAudience() {
		Jwt jwt = Jwt.withTokenValue("test-token")
			.header("alg", "RS256")
			.claim("aud", List.of("payment-service"))
			.build();
		JwtDecoder decoder = ProductionSecurityConfig.requireAudience(token -> jwt, "payment-service");

		assertThat(decoder.decode("test-token")).isSameAs(jwt);
	}

	@Test
	void mapsConfiguredOidcAuthoritiesToRoles() {
		ReflectionTestUtils.setField(config, "authoritiesClaim", "roles");
		Jwt jwt = Jwt.withTokenValue("test-token")
			.header("alg", "RS256")
			.claim("roles", List.of("ADMIN"))
			.build();

		assertThat(config.oidcJwtAuthenticationConverter().convert(jwt).getAuthorities())
			.contains(new SimpleGrantedAuthority("ROLE_ADMIN"));
	}

	@Test
	void corsIsNotRegisteredWhenNoOriginsAreAllowed() {
		ReflectionTestUtils.setField(config, "allowedOrigins", List.of());
		ExposedCorsRegistry registry = new ExposedCorsRegistry();

		config.corsConfigurer().addCorsMappings(registry);

		assertThat(registry.configurations()).isEmpty();
	}

	@Test
	void corsUsesOnlyConfiguredOriginsWithoutCredentials() {
		ReflectionTestUtils.setField(config, "allowedOrigins", List.of("https://web.example"));
		ExposedCorsRegistry registry = new ExposedCorsRegistry();

		config.corsConfigurer().addCorsMappings(registry);

		CorsConfiguration cors = registry.configurations().get("/**");
		assertThat(cors.getAllowedOrigins()).containsExactly("https://web.example");
		assertThat(cors.getAllowCredentials()).isFalse();
	}

	private static final class ExposedCorsRegistry extends CorsRegistry {
		Map<String, CorsConfiguration> configurations() {
			return getCorsConfigurations();
		}
	}
}
