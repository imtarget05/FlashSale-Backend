package com.flashsale.payment.security;

import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import org.springframework.security.authentication.AuthenticationManager;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

@Service
public class AuthService {

	private final AuthenticationManager authenticationManager;
	private final UserDetailsService userDetailsService;
	private final JwtUtil jwtUtil;
	private final PasswordEncoder passwordEncoder;

	public AuthService(AuthenticationManager authenticationManager,
					   UserDetailsService userDetailsService,
					   JwtUtil jwtUtil,
					   PasswordEncoder passwordEncoder) {
		this.authenticationManager = authenticationManager;
		this.userDetailsService = userDetailsService;
		this.jwtUtil = jwtUtil;
		this.passwordEncoder = passwordEncoder;
	}

	public AuthResponse authenticate(LoginRequest request) {
		authenticationManager.authenticate(
			new UsernamePasswordAuthenticationToken(request.email(), request.password()));
		UserDetails user = userDetailsService.loadUserByUsername(request.email());
		String token = jwtUtil.generateToken(user);
		return new AuthResponse(token, "Bearer");
	}

	@Transactional
	public void registerUser(String email, String rawPassword, String role) {
		throw new UnsupportedOperationException("User registration not implemented in this service");
	}

	public String encodePassword(String rawPassword) {
		return passwordEncoder.encode(rawPassword);
	}
}
