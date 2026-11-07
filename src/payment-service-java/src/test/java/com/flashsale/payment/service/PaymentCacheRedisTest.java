package com.flashsale.payment.service;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.flashsale.payment.config.CacheConfig;
import com.flashsale.payment.dto.PaymentRecordDto;
import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.entity.Payment;
import com.flashsale.payment.outbox.PaymentOutboxRepository;
import com.flashsale.payment.provider.PaymentProvider;
import com.flashsale.payment.repository.PaymentRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.mockito.Mockito;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.cache.Cache;
import org.springframework.cache.CacheManager;
import org.springframework.cache.annotation.EnableCaching;
import org.springframework.cache.support.SimpleCacheManager;
import org.springframework.cache.support.SimpleValueWrapper;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Import;
import org.springframework.data.redis.RedisConnectionFailureException;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.authentication.UsernamePasswordAuthenticationToken;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.test.context.junit.jupiter.SpringJUnitConfig;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

@SpringJUnitConfig(PaymentCacheRedisTest.TestConfig.class)
class PaymentCacheRedisTest {

	static class TestRedisCache implements Cache {
		private final String name = "payments";
		private final Map<Object, Object> store = new ConcurrentHashMap<>();
		private final AtomicInteger getCalls = new AtomicInteger();
		private final AtomicInteger putCalls = new AtomicInteger();
		private final AtomicInteger evictCalls = new AtomicInteger();
		private volatile boolean simulateRedisFailure = false;

		@Override
		public String getName() {
			return name;
		}

		@Override
		public Object getNativeCache() {
			return store;
		}

		@Override
		public ValueWrapper get(Object key) {
			getCalls.incrementAndGet();
			if (simulateRedisFailure) {
				throw new RedisConnectionFailureException("Redis connection refused: 127.0.0.1:6379");
			}
			Object val = store.get(key);
			return val != null ? new SimpleValueWrapper(val) : null;
		}

		@Override
		public <T> T get(Object key, Class<T> type) {
			ValueWrapper wrapper = get(key);
			return wrapper != null ? type.cast(wrapper.get()) : null;
		}

		@Override
		public <T> T get(Object key, Callable<T> valueLoader) {
			throw new UnsupportedOperationException();
		}

		@Override
		public void put(Object key, Object value) {
			putCalls.incrementAndGet();
			if (simulateRedisFailure) {
				throw new RedisConnectionFailureException("Redis connection refused: 127.0.0.1:6379");
			}
			store.put(key, value);
		}

		@Override
		public void evict(Object key) {
			evictCalls.incrementAndGet();
			if (simulateRedisFailure) {
				throw new RedisConnectionFailureException("Redis connection refused: 127.0.0.1:6379");
			}
			store.remove(key);
		}

		@Override
		public void clear() {
			store.clear();
		}

		public void setSimulateRedisFailure(boolean failure) {
			this.simulateRedisFailure = failure;
		}

		public int getGetCalls() {
			return getCalls.get();
		}

		public int getPutCalls() {
			return putCalls.get();
		}

		public int getEvictCalls() {
			return evictCalls.get();
		}

		public Map<Object, Object> getStore() {
			return store;
		}

		public void reset() {
			getCalls.set(0);
			putCalls.set(0);
			evictCalls.set(0);
			simulateRedisFailure = false;
			store.clear();
		}
	}

	@Configuration(proxyBeanMethods = false)
	@EnableCaching
	@Import(CacheConfig.class)
	static class TestConfig {

		@Bean
		TestRedisCache testRedisCache() {
			return new TestRedisCache();
		}

		@Bean
		CacheManager cacheManager(TestRedisCache testRedisCache) {
			SimpleCacheManager manager = new SimpleCacheManager();
			manager.setCaches(List.of(testRedisCache));
			return manager;
		}

		@Bean
		PaymentRepository paymentRepository() {
			return Mockito.mock(PaymentRepository.class);
		}

		@Bean
		PaymentOutboxRepository outboxRepository() {
			return Mockito.mock(PaymentOutboxRepository.class);
		}

		@Bean
		PaymentProvider paymentProvider() {
			return Mockito.mock(PaymentProvider.class);
		}

		@Bean
		ObjectMapper objectMapper() {
			return new ObjectMapper();
		}

		@Bean
		PaymentService paymentService(
				PaymentRepository paymentRepository,
				PaymentOutboxRepository outboxRepository,
				PaymentProvider paymentProvider,
				ObjectMapper objectMapper) {
			return new PaymentService(paymentRepository, outboxRepository, paymentProvider, objectMapper);
		}
	}

	@Autowired
	private PaymentService paymentService;

	@Autowired
	private PaymentRepository paymentRepository;

	@Autowired
	private PaymentProvider paymentProvider;

	@Autowired
	private TestRedisCache testRedisCache;

	@BeforeEach
	void setUp() {
		testRedisCache.reset();
		Mockito.reset(paymentRepository, paymentProvider);
		SecurityContextHolder.clearContext();
	}

	@Test
	@DisplayName("Cache Miss: initial getPayment queries repository and populates cache")
	void testCacheMiss_queriesRepositoryAndPopulatesCache() {
		String key = "idem-miss-" + UUID.randomUUID();
		Payment payment = new Payment(key, UUID.randomUUID(), 100, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx-miss", null, false, OffsetDateTime.now());
		payment.setCustomerEmail("user@example.test");
		when(paymentRepository.findByIdempotencyKey(key)).thenReturn(Optional.of(payment));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("user@example.test", "pass",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		PaymentRecordDto result = paymentService.getPayment(key);

		assertThat(result).isNotNull();
		assertThat(result.idempotencyKey()).isEqualTo(key);
		assertThat(result.transactionId()).isEqualTo("tx-miss");
		verify(paymentRepository, times(1)).findByIdempotencyKey(key);
		assertThat(testRedisCache.getStore()).containsKey(key);
	}

	@Test
	@DisplayName("Cache Hit: calling getPayment twice returns cached DTO on 2nd call without re-querying repository")
	void testCacheHit_returnsCachedDtoWithoutRequeryingRepository() {
		String key = "idem-hit-" + UUID.randomUUID();
		Payment payment = new Payment(key, UUID.randomUUID(), 101, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx-hit", null, false, OffsetDateTime.now());
		payment.setCustomerEmail("user@example.test");
		when(paymentRepository.findByIdempotencyKey(key)).thenReturn(Optional.of(payment));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("user@example.test", "pass",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		// 1st call: Cache Miss -> queries repository
		PaymentRecordDto firstCall = paymentService.getPayment(key);
		assertThat(firstCall).isNotNull();
		verify(paymentRepository, times(1)).findByIdempotencyKey(key);

		// 2nd call: Cache Hit -> returns cached DTO from Redis without querying repository
		PaymentRecordDto secondCall = paymentService.getPayment(key);
		assertThat(secondCall).isNotNull();
		assertThat(secondCall.idempotencyKey()).isEqualTo(key);
		assertThat(secondCall.transactionId()).isEqualTo(firstCall.transactionId());

		// Verify repository was NOT queried again
		verify(paymentRepository, times(1)).findByIdempotencyKey(key);
	}

	@Test
	@DisplayName("Cache Eviction: calling processPayment evicts cached key, so subsequent getPayment misses and re-queries repository")
	void testCacheEviction_processPaymentEvictsCacheAndSubsequentGetMisses() {
		String key = "idem-evict-" + UUID.randomUUID();
		UUID paymentId = UUID.randomUUID();
		Payment payment = new Payment(key, paymentId, 102, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx-evict", null, false, OffsetDateTime.now());
		payment.setCustomerEmail("user@example.test");
		when(paymentRepository.findByIdempotencyKey(key)).thenReturn(Optional.of(payment));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("user@example.test", "pass",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		// 1. Initial call populates cache
		paymentService.getPayment(key);
		verify(paymentRepository, times(1)).findByIdempotencyKey(key);
		assertThat(testRedisCache.getStore()).containsKey(key);

		// 2. Call processPayment with same idempotencyKey -> triggers @CacheEvict(value = "payments", key = "#request.idempotencyKey")
		ProcessPaymentRequest request = new ProcessPaymentRequest(
			paymentId, 102, key, new BigDecimal("10.01"), "USD");
		paymentService.processPayment(request);

		// Verify cache was evicted
		assertThat(testRedisCache.getStore()).doesNotContainKey(key);

		// 3. Subsequent getPayment must be a cache miss and re-query repository (1st getPayment + processPayment check + 2nd getPayment miss = 3 calls)
		paymentService.getPayment(key);
		verify(paymentRepository, times(3)).findByIdempotencyKey(key);
	}

	@Test
	@DisplayName("Graceful Fallback: when Redis throws RedisConnectionFailureException, getPayment gracefully falls back to database repository without error")
	void testGracefulFallback_whenRedisThrowsConnectionFailure_fallsBackToRepository() {
		String key = "idem-fallback-" + UUID.randomUUID();
		Payment payment = new Payment(key, UUID.randomUUID(), 103, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx-fallback", null, false, OffsetDateTime.now());
		payment.setCustomerEmail("user@example.test");
		when(paymentRepository.findByIdempotencyKey(key)).thenReturn(Optional.of(payment));

		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("user@example.test", "pass",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		// Simulate Redis connection failure
		testRedisCache.setSimulateRedisFailure(true);

		// Call getPayment - must NOT throw exception, but gracefully fall back to repository
		PaymentRecordDto result = paymentService.getPayment(key);

		assertThat(result).isNotNull();
		assertThat(result.idempotencyKey()).isEqualTo(key);
		assertThat(result.transactionId()).isEqualTo("tx-fallback");

		// Repository was queried as fallback
		verify(paymentRepository, times(1)).findByIdempotencyKey(key);
	}

	@Test
	@DisplayName("BOLA: getPayment denies access when payment is owned by another user")
	void testBolaOwnershipCheck_deniesAccessWhenNotOwner() {
		String key = "idem-bola-" + UUID.randomUUID();
		Payment payment = new Payment(key, UUID.randomUUID(), 104, new BigDecimal("10.01"), "USD",
			true, "Succeeded", "tx-bola", null, false, OffsetDateTime.now());
		payment.setCustomerEmail("userA@example.test");
		when(paymentRepository.findByIdempotencyKey(key)).thenReturn(Optional.of(payment));

		// Authenticated as userB
		SecurityContextHolder.getContext().setAuthentication(
			new UsernamePasswordAuthenticationToken("userB@example.test", "pass",
				List.of(new SimpleGrantedAuthority("ROLE_CUSTOMER"))));

		assertThatThrownBy(() -> paymentService.getPayment(key))
			.isInstanceOf(AccessDeniedException.class)
			.hasMessageContaining("does not belong to the authenticated user");
	}
}
