package com.flashsale.payment.security;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.web.servlet.config.annotation.CorsRegistry;
import org.springframework.web.servlet.config.annotation.WebMvcConfigurer;

import java.util.List;

/**
 * Production-specific security hardening.
 * <p>
 * This configuration is active only when the {@code production} or {@code prod}
 * Spring profile is set. It enforces fail-closed behaviour:
 * <ul>
 *   <li>Demo identity provider is disabled ({@link DemoUserDetailsConfig} returns
 *       "No identity provider is configured" outside the demo profile).</li>
 *   <li>JWT signing key is mandatory ({@link JwtStartupValidator}).</li>
 *   <li>CORS is restricted to declared origins (default: none).</li>
 *   <li>Security response headers are applied.</li>
 * </ul>
 */
@Configuration
@Profile("production | prod")
public class ProductionSecurityConfig {

	@Value("${app.security.allowed-origins:}")
	private List<String> allowedOrigins;

	@Value("${app.security.rate-limit-enabled:true}")
	private boolean rateLimitEnabled;

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

	public boolean isRateLimitEnabled() {
		return rateLimitEnabled;
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
