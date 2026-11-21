package com.flashsale.payment.integration;

import com.flashsale.payment.PaymentApplication;
import org.springframework.boot.SpringApplication;

/** Explicit test-only launcher so the fault advice is registered deterministically. */
public final class CrashDrillApplication {
	private CrashDrillApplication() { }

	public static void main(String[] args) {
		new SpringApplication(PaymentApplication.class, CrashBeforeResponseAdvice.class).run(args);
	}
}
