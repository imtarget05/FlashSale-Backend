package com.flashsale.payment.config;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.config.BeanPostProcessor;
import org.springframework.boot.data.redis.autoconfigure.DataRedisProperties;
import org.springframework.cache.Cache;
import org.springframework.cache.annotation.CachingConfigurer;
import org.springframework.cache.interceptor.CacheErrorHandler;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

@Configuration
public class CacheConfig implements CachingConfigurer {

	private static final Logger log = LoggerFactory.getLogger(CacheConfig.class);

	@Bean
	public static BeanPostProcessor dataRedisPropertiesPostProcessor() {
		return new BeanPostProcessor() {
			@Override
			public Object postProcessBeforeInitialization(Object bean, String beanName) {
				if (bean instanceof DataRedisProperties properties) {
					if (properties.getUrl() != null && properties.getUrl().isBlank()) {
						properties.setUrl(null);
					}
				}
				return bean;
			}

			@Override
			public Object postProcessAfterInitialization(Object bean, String beanName) {
				if (bean instanceof DataRedisProperties properties) {
					if (properties.getUrl() != null && properties.getUrl().isBlank()) {
						properties.setUrl(null);
					}
				}
				return bean;
			}
		};
	}

	@Bean
	@Override
	public CacheErrorHandler errorHandler() {
		return new CacheErrorHandler() {
			@Override
			public void handleCacheGetError(RuntimeException ex, Cache cache, Object key) {
				log.warn("[payment-service] cache get failed cache={} error={}", cache.getName(), ex.getClass().getSimpleName());
			}

			@Override
			public void handleCachePutError(RuntimeException ex, Cache cache, Object key, Object value) {
				log.warn("[payment-service] cache put failed cache={} error={}", cache.getName(), ex.getClass().getSimpleName());
			}

			@Override
			public void handleCacheEvictError(RuntimeException ex, Cache cache, Object key) {
				log.warn("[payment-service] cache evict failed cache={} error={}", cache.getName(), ex.getClass().getSimpleName());
			}

			@Override
			public void handleCacheClearError(RuntimeException ex, Cache cache) {
				log.warn("[payment-service] cache clear failed cache={} error={}", cache.getName(), ex.getClass().getSimpleName());
			}
		};
	}
}
