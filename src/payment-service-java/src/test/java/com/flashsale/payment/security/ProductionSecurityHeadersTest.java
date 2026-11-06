package com.flashsale.payment.security;

import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

import static org.assertj.core.api.Assertions.assertThat;

class ProductionSecurityHeadersTest {

	@Test
	void addsSecurityHeadersBeforeContinuingTheRequest() throws Exception {
		MockHttpServletRequest request = new MockHttpServletRequest();
		MockHttpServletResponse response = new MockHttpServletResponse();
		boolean[] chainInvoked = {false};

		new SecurityHeadersFilter().doFilter(request, response, (servletRequest, servletResponse) -> {
			chainInvoked[0] = true;
			assertThat(servletResponse).isSameAs(response);
		});

		assertThat(chainInvoked[0]).isTrue();
		assertThat(response.getHeader("X-Content-Type-Options")).isEqualTo("nosniff");
		assertThat(response.getHeader("X-Frame-Options")).isEqualTo("DENY");
		assertThat(response.getHeader("Referrer-Policy")).isEqualTo("no-referrer");
		assertThat(response.getHeader("Content-Security-Policy"))
			.contains("default-src 'self'", "script-src 'self'");
		assertThat(response.getHeader("Strict-Transport-Security"))
			.isEqualTo("max-age=31536000; includeSubDomains");
	}
}
