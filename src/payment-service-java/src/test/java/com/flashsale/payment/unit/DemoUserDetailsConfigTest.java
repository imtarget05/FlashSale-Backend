package com.flashsale.payment.unit;

import com.flashsale.payment.security.DemoUserDetailsConfig;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;

import static org.assertj.core.api.Assertions.assertThat;

class DemoUserDetailsConfigTest {
	private final ApplicationContextRunner runner = new ApplicationContextRunner()
		.withUserConfiguration(DemoUserDetailsConfig.class, PasswordConfig.class);

	@Test
	void defaultProfileDoesNotCreateDemoUsers() {
		runner.run(context -> assertThat(context).doesNotHaveBean(UserDetailsService.class));
	}

	@Test
	void devProfileCreatesDemoUsers() {
		runner.withInitializer(context -> context.getEnvironment().setActiveProfiles("dev"))
			.run(context -> assertThat(context).hasSingleBean(UserDetailsService.class));
	}

	@Configuration
	static class PasswordConfig {
		@Bean
		PasswordEncoder passwordEncoder() {
			return new BCryptPasswordEncoder();
		}
	}
}
