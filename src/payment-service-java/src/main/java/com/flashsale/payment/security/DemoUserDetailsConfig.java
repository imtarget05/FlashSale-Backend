package com.flashsale.payment.security;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.provisioning.InMemoryUserDetailsManager;

@Configuration
@Profile("dev")
public class DemoUserDetailsConfig {
	@Bean
	public UserDetailsService userDetailsService(PasswordEncoder passwordEncoder) {
		return new InMemoryUserDetailsManager(
			User.withUsername("customer@flashsale.local")
				.password(passwordEncoder.encode("password")).roles("CUSTOMER").build(),
			User.withUsername("staff@flashsale.local")
				.password(passwordEncoder.encode("password")).roles("STAFF").build(),
			User.withUsername("admin@flashsale.local")
				.password(passwordEncoder.encode("password")).roles("ADMIN").build());
	}
}
