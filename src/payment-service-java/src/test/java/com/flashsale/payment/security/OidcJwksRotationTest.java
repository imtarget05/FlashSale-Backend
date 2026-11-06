package com.flashsale.payment.security;

import com.sun.net.httpserver.HttpServer;
import io.jsonwebtoken.Jwts;
import org.junit.jupiter.api.Test;
import org.springframework.security.oauth2.jwt.JwtDecoder;
import org.springframework.security.oauth2.jwt.NimbusJwtDecoder;

import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Date;
import java.util.List;
import java.util.concurrent.atomic.AtomicReference;

import static org.assertj.core.api.Assertions.assertThat;

class OidcJwksRotationTest {

	private static final String AUDIENCE = "payment-service";
	private static final String SUBJECT = "rotation-test-user";

	private record SigningKey(KeyPair keyPair, String keyId) {
	}

	@Test
	void decoderAcceptsNewSigningKeyDuringJwksOverlap() throws Exception {
		SigningKey originalKey = new SigningKey(generateRsaKeyPair(), "key-1");
		SigningKey rotatedKey = new SigningKey(generateRsaKeyPair(), "key-2");
		AtomicReference<String> jwks = new AtomicReference<>(jwks(originalKey));

		HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
		server.createContext("/jwks", exchange -> {
			byte[] body = jwks.get().getBytes(StandardCharsets.UTF_8);
			exchange.getResponseHeaders().set("Content-Type", "application/json");
			exchange.sendResponseHeaders(200, body.length);
			try (var response = exchange.getResponseBody()) {
				response.write(body);
			}
		});
		server.start();

		try {
			String jwksUri = "http://127.0.0.1:" + server.getAddress().getPort() + "/jwks";
			JwtDecoder decoder = ProductionSecurityConfig.requireAudience(
				NimbusJwtDecoder.withJwkSetUri(jwksUri).build(),
				AUDIENCE);

			assertThat(decoder.decode(token(originalKey)).getSubject()).isEqualTo(SUBJECT);

			jwks.set(jwks(originalKey, rotatedKey));

			assertThat(decoder.decode(token(rotatedKey)).getSubject()).isEqualTo(SUBJECT);
			assertThat(decoder.decode(token(originalKey)).getSubject()).isEqualTo(SUBJECT);
		} finally {
			server.stop(0);
		}
	}

	private KeyPair generateRsaKeyPair() throws Exception {
		KeyPairGenerator generator = KeyPairGenerator.getInstance("RSA");
		generator.initialize(2048);
		return generator.generateKeyPair();
	}

	private String token(SigningKey signingKey) {
		Instant now = Instant.now();
		return Jwts.builder()
			.header()
				.keyId(signingKey.keyId())
				.and()
			.subject(SUBJECT)
			.issuedAt(Date.from(now))
			.expiration(Date.from(now.plusSeconds(60)))
			.audience()
				.add(AUDIENCE)
				.and()
			.signWith(signingKey.keyPair().getPrivate(), Jwts.SIG.RS256)
			.compact();
	}

	private String jwks(SigningKey... keys) {
		List<String> jwkEntries = new ArrayList<>();
		for (SigningKey key : keys) {
			RSAPublicKey publicKey = (RSAPublicKey) key.keyPair().getPublic();
			jwkEntries.add("""
				{"kty":"RSA","kid":"%s","use":"sig","alg":"RS256","n":"%s","e":"%s"}
				""".formatted(key.keyId(), base64Url(publicKey.getModulus().toByteArray()),
					base64Url(publicKey.getPublicExponent().toByteArray())).trim());
		}
		return "{\"keys\":[" + String.join(",", jwkEntries) + "]}";
	}

	private String base64Url(byte[] value) {
		int firstNonZero = 0;
		while (firstNonZero < value.length - 1 && value[firstNonZero] == 0) {
			firstNonZero++;
		}
		byte[] unsignedValue = java.util.Arrays.copyOfRange(value, firstNonZero, value.length);
		return Base64.getUrlEncoder().withoutPadding().encodeToString(unsignedValue);
	}
}
