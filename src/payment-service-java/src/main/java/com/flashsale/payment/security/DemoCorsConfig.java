package com.flashsale.payment.security;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.web.cors.CorsConfiguration;
import org.springframework.web.cors.CorsConfigurationSource;
import org.springframework.web.cors.UrlBasedCorsConfigurationSource;

import java.util.Arrays;
import java.util.List;

@Configuration
@Profile("demo & !production & !prod")
public class DemoCorsConfig {

	private final List<String> allowedOrigins;

	public DemoCorsConfig(@Value("${app.security.allowed-origins:}") String origins) {
		this.allowedOrigins = Arrays.stream(origins.split(","))
			.map(String::trim)
			.filter(origin -> !origin.isEmpty())
			.toList();
		if (allowedOrigins.contains("*")) {
			throw new IllegalArgumentException("Demo CORS requires explicit origins");
		}
	}

	@Bean
	public CorsConfigurationSource corsConfigurationSource() {
		UrlBasedCorsConfigurationSource source = new UrlBasedCorsConfigurationSource();
		if (allowedOrigins.isEmpty()) {
			return source;
		}
		CorsConfiguration cors = new CorsConfiguration();
		cors.setAllowedOrigins(allowedOrigins);
		cors.setAllowedMethods(List.of("GET", "POST", "PUT", "DELETE", "OPTIONS"));
		cors.setAllowedHeaders(List.of("Authorization", "Content-Type", "X-Requested-With"));
		cors.setAllowCredentials(false);
		cors.setMaxAge(3600L);
		source.registerCorsConfiguration("/api/**", cors);
		return source;
	}
}
