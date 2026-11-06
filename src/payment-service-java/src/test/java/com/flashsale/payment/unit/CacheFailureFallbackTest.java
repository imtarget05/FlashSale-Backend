package com.flashsale.payment.unit;

import com.flashsale.payment.config.CacheConfig;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.cache.Cache;
import org.springframework.cache.CacheManager;
import org.springframework.cache.annotation.CacheEvict;
import org.springframework.cache.annotation.Cacheable;
import org.springframework.cache.annotation.EnableCaching;
import org.springframework.cache.concurrent.ConcurrentMapCache;
import org.springframework.cache.support.SimpleCacheManager;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.junit.jupiter.SpringJUnitConfig;

import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.assertThat;

@SpringJUnitConfig(CacheFailureFallbackTest.TestConfig.class)
class CacheFailureFallbackTest {

	@Configuration(proxyBeanMethods = false)
	@EnableCaching
	@Import(CacheConfig.class)
	static class TestConfig {
		@Bean
		CacheManager cacheManager() {
			Cache unavailableCache = new ConcurrentMapCache("payments") {
				@Override
				public ValueWrapper get(Object key) {
					throw new IllegalStateException("simulated cache outage");
				}

				@Override
				public void put(Object key, Object value) {
					throw new IllegalStateException("simulated cache outage");
				}

				@Override
				public void evict(Object key) {
					throw new IllegalStateException("simulated cache outage");
				}
			};
			SimpleCacheManager manager = new SimpleCacheManager();
			manager.setCaches(List.of(unavailableCache));
			return manager;
		}

		@Bean
		PaymentLookupService paymentLookupService() {
			return new PaymentLookupService();
		}
	}

	static class PaymentLookupService {
		private final AtomicInteger databaseReads = new AtomicInteger();
		private final AtomicInteger databaseWrites = new AtomicInteger();

		@Cacheable("payments")
		public String read(String key) {
			databaseReads.incrementAndGet();
			return "db:" + key;
		}

		@CacheEvict(value = "payments", key = "#key")
		public String write(String key) {
			databaseWrites.incrementAndGet();
			return "saved:" + key;
		}

		public int databaseReads() {
			return databaseReads.get();
		}

		public int databaseWrites() {
			return databaseWrites.get();
		}
	}

	@Autowired
	private PaymentLookupService service;

	@Test
	void cacheGetAndPutFailureFallsBackToDatabase() {
		assertThat(service.read("payment-1")).isEqualTo("db:payment-1");
		assertThat(service.read("payment-1")).isEqualTo("db:payment-1");
		assertThat(service.databaseReads()).isEqualTo(2);
	}

	@Test
	void cacheEvictFailureDoesNotFailBackendWrite() {
		assertThat(service.write("payment-1")).isEqualTo("saved:payment-1");
		assertThat(service.databaseWrites()).isEqualTo(1);
	}
}
