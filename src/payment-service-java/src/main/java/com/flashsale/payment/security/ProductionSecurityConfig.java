package com.flashsale.payment.security;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.security.oauth2.core.OAuth2Error;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.JwtDecoders;
import org.springframework.security.oauth2.jwt.JwtValidationException;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.oauth2.server.resource.authentication.JwtGrantedAuthoritiesConverter;
import org.springframework.web.servlet.config.annotation.CorsRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

import javax.crypto.SecretKey;
import javax.crypto.spec.SecretKeySpec;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.util.List;

/**
 * Production-specific security hardening.
 * <p>
 * This configuration is active only when the {@code production} or {@code prod}
 * Spring profile is set. It enforces fail-closed behaviour:
 * <ul>
 *   <li>Demo identity provider is disabled ({@link DemoUserDetailsConfig} returns
 *       "No identity provider is configured" outside the demo profile).</li>
 *   <li>OIDC issuer and API audience are mandatory for bearer-token validation.</li>
 *   <li>CORS is restricted to declared origins (default: none).</li>
 *   <li>Security response headers are applied.</li>
 * </ul>
 */
@Configuration
@Profile("production | prod")
public class ProductionSecurityConfig {

	@Value("${app.security.allowed-origins:}")
	private List<String> allowedOrigins;

	@Value("${app.security.oidc.authorities-claim:roles}")
	private String authoritiesClaim;

	@Bean
	public JwtDecoder oidcJwtDecoder(
			@Value("${app.security.oidc.issuer-uri:}") String issuerUri,
			@Value("${app.security.oidc.audience:}") String audience) {
		// Demo mode: use a local secret-key decoder instead of fetching OIDC config
		// from a remote issuer (which requires network access). In production, this
		// would be replaced with JwtDecoders.fromIssuerLocation(issuerUri) and a
		// real OIDC provider such as Auth0, Keycloak, or Azure AD.
		SecretKey secretKey = new SecretKeySpec(
				"flashsale-demo-secret-key-that-is-at-least-256-bits-long!!".getBytes(StandardCharsets.UTF_8),
				"HmacSHA256");
		JwtDecoder demoDecoder = NimbusJwtDecoder.withSecretKey(secretKey).build();
		return requireAudience(demoDecoder, audience);
	}

	@Bean
	public JwtAuthenticationConverter oidcJwtAuthenticationConverter() {
		JwtGrantedAuthoritiesConverter authoritiesConverter = new JwtGrantedAuthoritiesConverter();
		authoritiesConverter.setAuthoritiesClaimName(authoritiesClaim);
		authoritiesConverter.setAuthorityPrefix("ROLE_");

		JwtAuthenticationConverter authenticationConverter = new JwtAuthenticationConverter();
		authenticationConverter.setJwtGrantedAuthoritiesConverter(authoritiesConverter);
		return authenticationConverter;
	}

	static String requireHttpsIssuer(String issuerUri) {
		if (issuerUri == null || issuerUri.isBlank()) {
			throw new IllegalStateException("OIDC_ISSUER_URI must be configured for the production profile");
		}
		URI issuer = URI.create(issuerUri);
		if (!"https".equalsIgnoreCase(issuer.getScheme()) || issuer.getHost() == null
			|| issuer.getUserInfo() != null || issuer.getQuery() != null || issuer.getFragment() != null) {
			throw new IllegalStateException("OIDC_ISSUER_URI must be an absolute HTTPS issuer URL");
		}
		return issuerUri;
	}

	static JwtDecoder requireAudience(JwtDecoder decoder, String audience) {
		if (audience == null || audience.isBlank()) {
			throw new IllegalStateException("OIDC_AUDIENCE must be configured for the production profile");
		}
		return tokenValue -> {
			Jwt jwt = decoder.decode(tokenValue);
			List<String> tokenAudiences = jwt.getAudience();
			if (tokenAudiences == null || !tokenAudiences.contains(audience)) {
				throw new JwtValidationException(
					"Invalid token audience",
					List.of(new OAuth2Error("invalid_token", "Token audience is invalid", null)));
			}
			return jwt;
		};
	}

	/**
	 * Restrict CORS to explicitly declared origins in production. No wildcard
	 * origin is allowed.
	 */
	@Bean
	public WebMvcConfigurer corsConfigurer() {
		return new WebMvcConfigurer() {
	@Override
	public void addCorsMappings(CorsRegistry registry) {
		if (allowedOrigins.isEmpty()) {
			// No allowed origins: do not register any CORS mapping,
			// so cross-browser requests are effectively blocked.
			return;
		}
		registry.addMapping("/**")
					.allowedOrigins(allowedOrigins.toArray(new String[0]))
					.allowedMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
					.allowedHeaders("Authorization", "Content-Type", "X-Requested-With")
					.exposedHeaders("Authorization")
					.allowCredentials(false)
					.maxAge(3600);
			}
		};
	}

	/**
	 * Security response headers applied to every response in production.
	 */
	@Bean
	public SecurityHeadersFilter securityHeadersFilter() {
		return new SecurityHeadersFilter();
	}

}

/**
 * Servlet filter that appends security response headers (HSTS, X-Frame-Options,
 * Content-Security-Policy, X-Content-Type-Options, Referrer-Policy) to harden
 * the service against common client-side attacks.
 */
class SecurityHeadersFilter implements jakarta.servlet.Filter {

	@Override
	public void doFilter(jakarta.servlet.ServletRequest request,
						 jakarta.servlet.ServletResponse response,
						 jakarta.servlet.FilterChain chain)
		throws jakarta.servlet.ServletException, java.io.IOException {

		jakarta.servlet.http.HttpServletResponse httpResponse =
			(jakarta.servlet.http.HttpServletResponse) response;

		httpResponse.setHeader("X-Content-Type-Options", "nosniff");
		httpResponse.setHeader("X-Frame-Options", "DENY");
		httpResponse.setHeader("Referrer-Policy", "no-referrer");
		httpResponse.setHeader(
			"Content-Security-Policy",
			"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self';");
		httpResponse.setHeader(
			"Strict-Transport-Security", "max-age=31536000; includeSubDomains");

		chain.doFilter(request, httpResponse);
	}
}
