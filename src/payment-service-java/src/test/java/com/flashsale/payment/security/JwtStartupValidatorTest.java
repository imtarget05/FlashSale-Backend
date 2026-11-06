package com.flashsale.payment.security;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThatCode;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class JwtStartupValidatorTest {

	@Test
	void rejectsMissingSigningKey() {
		assertThatThrownBy(() -> new JwtStartupValidator(null).run())
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("must be set");
	}

	@Test
	void rejectsBlankSigningKey() {
		assertThatThrownBy(() -> new JwtStartupValidator(" \t").run())
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("must be set");
	}

	@Test
	void rejectsSigningKeyShorterThanThirtyTwoUtf8Bytes() {
		assertThatThrownBy(() -> new JwtStartupValidator("x".repeat(31)).run())
			.isInstanceOf(IllegalStateException.class)
			.hasMessageContaining("at least 32 bytes");
	}

	@Test
	void acceptsThirtyTwoUtf8BytesAtTheMinimumBoundary() {
		assertThatCode(() -> new JwtStartupValidator("x".repeat(32)).run())
			.doesNotThrowAnyException();
	}

	@Test
	void measuresTheKeyLengthInUtf8Bytes() {
		assertThatCode(() -> new JwtStartupValidator("é".repeat(16)).run())
			.doesNotThrowAnyException();
	}
}
