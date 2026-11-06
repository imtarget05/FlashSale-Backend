package com.flashsale.payment.security;

import jakarta.servlet.DispatcherType;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.env.Environment;
import org.springframework.core.env.Profiles;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.security.authentication.AuthenticationManager;
import org.springframework.security.authentication.ProviderManager;
import org.springframework.security.authentication.dao.DaoAuthenticationProvider;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configuration.EnableWebSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.HttpStatusEntryPoint;
import org.springframework.security.web.authentication.UsernamePasswordAuthenticationFilter;

@Configuration
@EnableWebSecurity
public class SecurityConfig {

	private final JwtUtil jwtUtil;
	private final boolean demoOnlyProfile;

	public SecurityConfig(JwtUtil jwtUtil, Environment environment) {
		this.jwtUtil = jwtUtil;
		this.demoOnlyProfile = environment.acceptsProfiles(Profiles.of("demo & !production & !prod"));
	}

	@Bean
	public SecurityFilterChain filterChain(HttpSecurity http, JwtAuthFilter jwtAuthFilter) throws Exception {
		http
			.csrf(csrf -> csrf.disable())
			.exceptionHandling(errors -> errors.authenticationEntryPoint(new HttpStatusEntryPoint(HttpStatus.UNAUTHORIZED)))
			.sessionManagement(session -> session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
			.authorizeHttpRequests(auth -> {
				// Preserve the original 403/404 when the servlet container dispatches to /error.
				auth.dispatcherTypeMatchers(DispatcherType.ERROR).permitAll();
				auth.requestMatchers("/api/auth/**", "/health", "/health/live", "/healthz", "/actuator/health/readiness").permitAll();
				if (demoOnlyProfile) {
					auth.requestMatchers("/v3/api-docs/**", "/swagger-ui/**", "/swagger-ui.html").permitAll();
				} else {
					auth.requestMatchers("/v3/api-docs/**", "/swagger-ui/**", "/swagger-ui.html").hasRole("ADMIN");
				}
				auth.requestMatchers(HttpMethod.POST, "/api/payments").hasAnyRole("STAFF", "ADMIN")
					.requestMatchers("/api/payments/{idempotencyKey}").hasAnyRole("CUSTOMER", "STAFF", "ADMIN")
					.requestMatchers(HttpMethod.GET, "/api/payments").hasAnyRole("STAFF", "ADMIN")
					.requestMatchers("/api/audit/**").hasAnyRole("STAFF", "ADMIN")
					.anyRequest().authenticated();
			})
			.addFilterBefore(jwtAuthFilter, UsernamePasswordAuthenticationFilter.class);
		return http.build();
	}

	@Bean
	public JwtAuthFilter jwtAuthFilter(UserDetailsService userDetailsService) {
		return new JwtAuthFilter(jwtUtil, userDetailsService);
	}

	@Bean
	public PasswordEncoder passwordEncoder() {
		return new BCryptPasswordEncoder();
	}

	@Bean
	public AuthenticationManager authenticationManager(UserDetailsService userDetailsService, PasswordEncoder passwordEncoder) {
		DaoAuthenticationProvider authProvider = new DaoAuthenticationProvider(userDetailsService);
		authProvider.setPasswordEncoder(passwordEncoder);
		return new ProviderManager(authProvider);
	}
}
