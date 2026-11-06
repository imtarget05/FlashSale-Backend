package com.flashsale.payment.security;

import io.jsonwebtoken.*;
import io.jsonwebtoken.security.Keys;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.stereotype.Component;

import javax.crypto.SecretKey;
import java.nio.charset.StandardCharsets;
import java.util.Date;
import java.util.function.Function;

@Component
public class JwtUtil {

	private static final int MIN_KEY_BYTES = 32;

	private final String signingSecret;
	private final long expirationMs;

	public JwtUtil(@Value("${app.jwt.secret}") String secret,
				   @Value("${app.jwt.expiration-ms:86400000}") long expirationMs) {
		this.signingSecret = secret;
		this.expirationMs = expirationMs;
	}

	public String generateToken(UserDetails userDetails) {
		return Jwts.builder()
			.subject(userDetails.getUsername())
			.issuedAt(new Date(System.currentTimeMillis()))
			.expiration(new Date(System.currentTimeMillis() + expirationMs))
			.signWith(signingKey())
			.compact();
	}

	public String extractUsername(String token) {
		return extractClaim(token, Claims::getSubject);
	}

	public Date extractExpiration(String token) {
		return extractClaim(token, Claims::getExpiration);
	}

	public <T> T extractClaim(String token, Function<Claims, T> claimsResolver) {
		final Claims claims = extractAllClaims(token);
		return claimsResolver.apply(claims);
	}

	private Claims extractAllClaims(String token) {
		return Jwts.parser()
			.verifyWith(signingKey())
			.build()
			.parseSignedClaims(token)
			.getPayload();
	}

	public boolean isTokenValid(String token, UserDetails userDetails) {
		try {
			final String username = extractUsername(token);
			return (username.equals(userDetails.getUsername())) && !isTokenExpired(token);
		} catch (JwtException | IllegalArgumentException e) {
			return false;
		}
	}

	private boolean isTokenExpired(String token) {
		try {
			return extractExpiration(token).before(new Date());
		} catch (JwtException | IllegalArgumentException e) {
			return true;
		}
	}

	public boolean validateToken(String token) {
		try {
			Jwts.parser().verifyWith(signingKey()).build().parseSignedClaims(token);
			return true;
		} catch (JwtException | IllegalArgumentException e) {
			return false;
		}
	}

	private SecretKey signingKey() {
		if (signingSecret == null || signingSecret.isBlank()) {
			throw new IllegalStateException(
				"JWT_SIGNING_KEY (app.jwt.secret) must be set before using demo HS256 authentication");
		}
		byte[] keyBytes = signingSecret.getBytes(StandardCharsets.UTF_8);
		if (keyBytes.length < MIN_KEY_BYTES) {
			throw new IllegalStateException(
				"JWT_SIGNING_KEY must be at least 32 bytes before using demo HS256 authentication");
		}
		return Keys.hmacShaKeyFor(keyBytes);
	}
}
