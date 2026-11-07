package com.flashsale.payment.unit;

import com.flashsale.payment.security.DemoCorsConfig;
import jakarta.servlet.http.HttpServletRequest;
import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.web.cors.CorsConfiguration;

import static org.assertj.core.api.Assertions.assertThat;

class DemoCorsConfigTest {

	@Test
	void onlyDeclaredPreviewOriginIsAllowed() {
		DemoCorsConfig config = new DemoCorsConfig("https://preview.flashsale-frontend.pages.dev");
		HttpServletRequest request = new MockHttpServletRequest("OPTIONS", "/api/auth/login");
		CorsConfiguration cors = config.corsConfigurationSource().getCorsConfiguration(request);

		assertThat(cors).isNotNull();
		assertThat(cors.getAllowedOrigins()).containsExactly("https://preview.flashsale-frontend.pages.dev");
		assertThat(cors.getAllowedMethods()).contains("POST", "OPTIONS");
		assertThat(cors.getAllowedHeaders()).contains("Authorization", "Content-Type");
		assertThat(cors.getAllowCredentials()).isFalse();
	}

	@Test
	void emptyOriginListKeepsCrossOriginRequestsClosed() {
		DemoCorsConfig config = new DemoCorsConfig("");
		HttpServletRequest request = new MockHttpServletRequest("OPTIONS", "/api/auth/login");
		assertThat(config.corsConfigurationSource().getCorsConfiguration(request)).isNull();
	}
}
