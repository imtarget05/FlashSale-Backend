package com.flashsale.payment.security;

import org.junit.jupiter.api.Test;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtDecoder;

import java.util.List;

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
	void requiresAnExpectedAudience() {
		assertThatThrownBy(() -> ProductionSecurityConfig.requireAudience(token -> null, ""))
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
	void acceptsTokensForTheConfiguredAudience() {
		Jwt jwt = Jwt.withTokenValue("test-token")
			.header("alg", "RS256")
			.claim("aud", List.of("payment-service"))
			.build();
		JwtDecoder decoder = ProductionSecurityConfig.requireAudience(token -> jwt, "payment-service");

		assertThat(decoder.decode("test-token")).isSameAs(jwt);
	}
}
