package com.flashsale.payment.security;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.core.userdetails.UsernameNotFoundException;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.provisioning.InMemoryUserDetailsManager;

@Configuration
public class DemoUserDetailsConfig {

	@Bean
	@Profile("demo & !production & !prod")
	public UserDetailsService demoUserDetailsService(
			PasswordEncoder passwordEncoder,
			@Value("${demo.auth.password:}") String demoPassword) {
		if (demoPassword.isBlank() || demoPassword.length() < 16) {
			throw new IllegalStateException(
				"demo.auth.password must be configured with at least 16 characters for the demo profile");
		}

		return new InMemoryUserDetailsManager(
			User.withUsername("customer@flashsale.local")
				.password(passwordEncoder.encode(demoPassword)).roles("CUSTOMER").build(),
			User.withUsername("staff@flashsale.local")
				.password(passwordEncoder.encode(demoPassword)).roles("STAFF").build(),
			User.withUsername("admin@flashsale.local")
				.password(passwordEncoder.encode(demoPassword)).roles("ADMIN").build());
	}

	@Bean
	@Profile("!demo | production | prod")
	public UserDetailsService noConfiguredIdentityProvider() {
		return username -> {
			throw new UsernameNotFoundException("No identity provider is configured");
		};
	}
}
