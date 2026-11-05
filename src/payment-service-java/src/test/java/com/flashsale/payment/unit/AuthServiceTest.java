package com.flashsale.payment.unit;

import com.flashsale.payment.dto.AuthResponse;
import com.flashsale.payment.dto.LoginRequest;
import com.flashsale.payment.security.AuthService;
import com.flashsale.payment.security.JwtUtil;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.security.authentication.BadCredentialsException;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.crypto.password.PasswordEncoder;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.*;

@ExtendWith(MockitoExtension.class)
class AuthServiceTest {

	@Mock
	private org.springframework.security.authentication.AuthenticationManager authenticationManager;

	@Mock
	private org.springframework.security.core.userdetails.UserDetailsService userDetailsService;

	@Mock
	private JwtUtil jwtUtil;

	@Mock
	private PasswordEncoder passwordEncoder;

	private AuthService authService;

	@BeforeEach
	void setUp() {
		authService = new AuthService(authenticationManager, userDetailsService, jwtUtil, passwordEncoder);
	}

	@Test
	void authenticate_shouldReturnJwtToken_whenUserExists() {
		LoginRequest request = new LoginRequest("customer@flashsale.local", "password123");
		UserDetails user = User.withUsername("customer@flashsale.local")
			.password("encoded-password")
			.roles("CUSTOMER")
			.build();

		when(userDetailsService.loadUserByUsername("customer@flashsale.local")).thenReturn(user);
		when(jwtUtil.generateToken(user)).thenReturn("jwt-token-123");

		AuthResponse response = authService.authenticate(request);

		assertThat(response.token()).isEqualTo("jwt-token-123");
		assertThat(response.type()).isEqualTo("Bearer");
		verify(authenticationManager).authenticate(any());
		verify(userDetailsService).loadUserByUsername("customer@flashsale.local");
	}

	@Test
	void authenticate_shouldThrowBadCredentials_whenAuthenticationFails() {
		LoginRequest request = new LoginRequest("customer@flashsale.local", "wrong-password");
		when(authenticationManager.authenticate(any())).thenThrow(new BadCredentialsException("bad credentials"));

		assertThatThrownBy(() -> authService.authenticate(request))
			.isInstanceOf(BadCredentialsException.class)
			.hasMessage("bad credentials");
		verify(userDetailsService, never()).loadUserByUsername(anyString());
	}

	@Test
	void encodePassword_shouldReturnEncodedHash() {
		when(passwordEncoder.encode("raw-secret")).thenReturn("$2a$10$encoded");

		String encoded = authService.encodePassword("raw-secret");

		assertThat(encoded).isEqualTo("$2a$10$encoded");
		verify(passwordEncoder).encode("raw-secret");
	}
}
