package com.flashsale.payment.unit;

import com.flashsale.payment.security.JwtUtil;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;
import org.junit.jupiter.api.Test;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetails;

import javax.crypto.SecretKey;
import java.nio.charset.StandardCharsets;
import java.util.Date;

import static org.assertj.core.api.Assertions.assertThat;

class JwtUtilTest {

	@Test
	void generateToken_shouldRoundTripAndValidate() {
		String secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
		JwtUtil jwtUtil = new JwtUtil(secret, 60_000L);
		UserDetails user = User.withUsername("staff@flashsale.local")
			.password("password")
			.roles("STAFF")
			.build();

		String token = jwtUtil.generateToken(user);

		assertThat(jwtUtil.validateToken(token)).isTrue();
		assertThat(jwtUtil.extractUsername(token)).isEqualTo("staff@flashsale.local");
		assertThat(jwtUtil.isTokenValid(token, user)).isTrue();
	}

	@Test
	void validateToken_shouldRejectExpiredToken() {
		String secret = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
		SecretKey key = Keys.hmacShaKeyFor(secret.getBytes(StandardCharsets.UTF_8));
		String expired = Jwts.builder()
			.subject("customer@flashsale.local")
			.issuedAt(new Date(System.currentTimeMillis() - 60_000))
			.expiration(new Date(System.currentTimeMillis() - 1_000))
			.signWith(key)
			.compact();

		JwtUtil jwtUtil = new JwtUtil(secret, 60_000L);
		UserDetails user = User.withUsername("customer@flashsale.local")
			.password("password")
			.roles("CUSTOMER")
			.build();

		assertThat(jwtUtil.validateToken(expired)).isFalse();
		assertThat(jwtUtil.isTokenValid(expired, user)).isFalse();
	}
}
