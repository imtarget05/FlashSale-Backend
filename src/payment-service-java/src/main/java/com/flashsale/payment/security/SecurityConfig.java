package com.flashsale.payment.security;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.security.authentication.AuthenticationManager;
import org.springframework.security.authentication.ProviderManager;
import org.springframework.security.authentication.dao.DaoAuthenticationProvider;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configuration.EnableWebSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.provisioning.InMemoryUserDetailsManager;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.HttpStatusEntryPoint;
import org.springframework.security.web.authentication.UsernamePasswordAuthenticationFilter;

@Configuration
@EnableWebSecurity
public class SecurityConfig {

	private final JwtUtil jwtUtil;

	public SecurityConfig(JwtUtil jwtUtil) {
		this.jwtUtil = jwtUtil;
	}

	@Bean
	public SecurityFilterChain filterChain(HttpSecurity http, JwtAuthFilter jwtAuthFilter) throws Exception {
		http
			.csrf(csrf -> csrf.disable())
			.exceptionHandling(errors -> errors.authenticationEntryPoint(new HttpStatusEntryPoint(HttpStatus.UNAUTHORIZED)))
			.sessionManagement(session -> session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
			.authorizeHttpRequests(auth -> auth
				.requestMatchers("/api/auth/**", "/health", "/health/live", "/healthz", "/health/ready", "/v3/api-docs/**", "/swagger-ui/**", "/swagger-ui.html").permitAll()
				.requestMatchers(HttpMethod.POST, "/api/payments").hasAnyRole("STAFF", "ADMIN")
				.requestMatchers("/api/payments/{idempotencyKey}").hasAnyRole("CUSTOMER", "STAFF", "ADMIN")
				.requestMatchers(HttpMethod.GET, "/api/payments").hasAnyRole("STAFF", "ADMIN")
				.anyRequest().authenticated()
			)
			.addFilterBefore(jwtAuthFilter, UsernamePasswordAuthenticationFilter.class);
		return http.build();
	}

	@Bean
	public JwtAuthFilter jwtAuthFilter() {
		return new JwtAuthFilter(jwtUtil, userDetailsService());
	}

	@Bean
	@Profile("dev")
	// DEMO ONLY: in-memory demo users for local development. Not active in production;
	// replace with a database-backed UserDetailsService before deploying.
	public UserDetailsService userDetailsService() {
		UserDetails customer = User.builder()
			.username("customer@flashsale.local")
			.password(passwordEncoder().encode("password"))
			.roles("CUSTOMER")
			.build();
		UserDetails staff = User.builder()
			.username("staff@flashsale.local")
			.password(passwordEncoder().encode("password"))
			.roles("STAFF")
			.build();
		UserDetails admin = User.builder()
			.username("admin@flashsale.local")
			.password(passwordEncoder().encode("password"))
			.roles("ADMIN")
			.build();
		return new InMemoryUserDetailsManager(customer, staff, admin);
	}

	@Bean
	public PasswordEncoder passwordEncoder() {
		return new BCryptPasswordEncoder();
	}

	@Bean
	public AuthenticationManager authenticationManager(UserDetailsService userDetailsService, PasswordEncoder passwordEncoder) {
		DaoAuthenticationProvider authProvider = new DaoAuthenticationProvider();
		authProvider.setUserDetailsService(userDetailsService);
		authProvider.setPasswordEncoder(passwordEncoder);
		return new ProviderManager(authProvider);
	}
}
