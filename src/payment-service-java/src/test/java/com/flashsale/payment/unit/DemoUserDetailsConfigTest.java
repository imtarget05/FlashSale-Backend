package com.flashsale.payment.unit;

import com.flashsale.payment.security.DemoUserDetailsConfig;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.core.userdetails.UsernameNotFoundException;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class DemoUserDetailsConfigTest {
	private final ApplicationContextRunner runner = new ApplicationContextRunner()
		.withUserConfiguration(DemoUserDetailsConfig.class, PasswordConfig.class);

	@Test
	void demoProfileProvidesConfiguredDemoAccounts() {
		runner.withPropertyValues("demo.auth.password=interview-demo-pass")
			.withInitializer(context -> context.getEnvironment().setActiveProfiles("demo"))
			.run(context -> {
				assertThat(context).hasNotFailed();
				UserDetailsService users = context.getBean(UserDetailsService.class);
				PasswordEncoder passwordEncoder = context.getBean(PasswordEncoder.class);

				assertDemoUser(users, passwordEncoder, "customer@flashsale.local", "ROLE_CUSTOMER");
				assertDemoUser(users, passwordEncoder, "staff@flashsale.local", "ROLE_STAFF");
				assertDemoUser(users, passwordEncoder, "admin@flashsale.local", "ROLE_ADMIN");
			});
	}

	@Test
	void defaultProfileHasNoDemoAccountsAndAuthenticationFailsClosed() {
		assertDemoAccountsAreUnavailable(runner);
	}

	@Test
	void productionProfileHasNoDemoAccountsAndAuthenticationFailsClosed() {
		assertDemoAccountsAreUnavailable(
			runner.withInitializer(context -> context.getEnvironment().setActiveProfiles("production")));
	}

	@Test
	void devProfileDoesNotEnableDemoAccounts() {
		assertDemoAccountsAreUnavailable(
			runner.withInitializer(context -> context.getEnvironment().setActiveProfiles("dev")));
	}

	@Test
	void demoProfileRequiresConfiguredPassword() {
		runner.withInitializer(context -> context.getEnvironment().setActiveProfiles("demo"))
			.run(context -> assertThat(context).hasFailed());
	}

	private void assertDemoAccountsAreUnavailable(ApplicationContextRunner contextRunner) {
		contextRunner.run(context -> {
			assertThat(context).hasNotFailed();
			assertThat(context).hasSingleBean(UserDetailsService.class);
			UserDetailsService users = context.getBean(UserDetailsService.class);
			assertThatThrownBy(() -> users.loadUserByUsername("customer@flashsale.local"))
				.isInstanceOf(UsernameNotFoundException.class);
		});
	}

	private void assertDemoUser(
			UserDetailsService users,
			PasswordEncoder passwordEncoder,
			String username,
			String authority) {
		var user = users.loadUserByUsername(username);
		assertThat(passwordEncoder.matches("interview-demo-pass", user.getPassword())).isTrue();
		assertThat(user.getAuthorities())
			.extracting(role -> role.getAuthority())
			.containsExactly(authority);
	}

	@Configuration
	static class PasswordConfig {
		@Bean
		PasswordEncoder passwordEncoder() {
			return new BCryptPasswordEncoder();
		}
	}
}
