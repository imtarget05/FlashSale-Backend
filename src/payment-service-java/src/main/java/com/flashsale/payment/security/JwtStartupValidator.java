package com.flashsale.payment.security;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.CommandLineRunner;
import org.springframework.context.annotation.Profile;
import org.springframework.stereotype.Component;

import java.nio.charset.StandardCharsets;

/**
 * Validates the JWT signing key at startup.
 * The service must refuse to start when no signing key is provided, because
 * falling back to an insecure/default key would invalidate every issued token.
 */
@Component
@Profile("!production & !prod")
public class JwtStartupValidator implements CommandLineRunner {

	private static final int MIN_KEY_BYTES = 32;

	private final String signingKey;

	public JwtStartupValidator(@Value("${app.jwt.secret:}") String signingKey) {
		this.signingKey = signingKey;
	}

	@Override
	public void run(String... args) {
		if (signingKey == null || signingKey.isBlank()) {
			throw new IllegalStateException(
				"JWT_SIGNING_KEY (app.jwt.secret) must be set; refusing to start without a JWT signing key");
		}
		if (signingKey.getBytes(StandardCharsets.UTF_8).length < MIN_KEY_BYTES) {
			throw new IllegalStateException(
				"JWT_SIGNING_KEY must be at least 32 bytes (HS256 minimum); current key is too weak");
		}
	}
}
