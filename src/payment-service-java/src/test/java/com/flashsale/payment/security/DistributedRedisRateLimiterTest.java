package com.flashsale.payment.security;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.data.redis.RedisConnectionFailureException;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.data.redis.core.ZSetOperations;

import java.time.Duration;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyDouble;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.*;

@ExtendWith(MockitoExtension.class)
class DistributedRedisRateLimiterTest {

    @Mock
    private StringRedisTemplate redisTemplate;

    @Mock
    private ZSetOperations<String, String> zSetOperations;

    @Mock
    private InMemoryTokenBucketRateLimiter fallbackLimiter;

    private DistributedRedisRateLimiter rateLimiter;

    @BeforeEach
    void setUp() {
        rateLimiter = new DistributedRedisRateLimiter(
                redisTemplate,
                fallbackLimiter,
                5,   // max 5 requests
                60   // per 60 seconds
        );
    }

    @Test
    @DisplayName("When RedisTemplate is null, seamlessly delegate to InMemory fallback")
    void whenRedisTemplateIsNull_delegatesToFallback() {
        DistributedRedisRateLimiter nullRedisLimiter = new DistributedRedisRateLimiter(
                null,
                fallbackLimiter,
                5,
                60
        );

        when(fallbackLimiter.allowRequest("client-1")).thenReturn(true);
        boolean allowed = nullRedisLimiter.allowRequest("client-1");

        assertThat(allowed).isTrue();
        verify(fallbackLimiter).allowRequest("client-1");
    }

    @Test
    @DisplayName("When request count is under limit, allow request and record timestamp")
    void whenUnderLimit_allowsRequest() {
        when(redisTemplate.opsForZSet()).thenReturn(zSetOperations);
        when(zSetOperations.zCard(eq("rate_limit:client-1"))).thenReturn(2L);

        boolean allowed = rateLimiter.allowRequest("client-1");

        assertThat(allowed).isTrue();
        verify(zSetOperations).removeRangeByScore(eq("rate_limit:client-1"), eq(0.0), anyDouble());
        verify(zSetOperations).add(eq("rate_limit:client-1"), anyString(), anyDouble());
        verify(redisTemplate).expire(eq("rate_limit:client-1"), any(Duration.class));
        verifyNoInteractions(fallbackLimiter);
    }

    @Test
    @DisplayName("When request count reaches limit, reject request without recording")
    void whenAtLimit_rejectsRequest() {
        when(redisTemplate.opsForZSet()).thenReturn(zSetOperations);
        when(zSetOperations.zCard(eq("rate_limit:client-1"))).thenReturn(5L);

        boolean allowed = rateLimiter.allowRequest("client-1");

        assertThat(allowed).isFalse();
        verify(zSetOperations, never()).add(eq("rate_limit:client-1"), anyString(), anyDouble());
        verifyNoInteractions(fallbackLimiter);
    }

    @Test
    @DisplayName("When Redis throws connection failure, gracefully fall back to in-memory limiter without throwing 500")
    void whenRedisFails_gracefullyFallsBackToInMemory() {
        when(redisTemplate.opsForZSet()).thenThrow(new RedisConnectionFailureException("Redis connection refused"));
        when(fallbackLimiter.allowRequest("client-1")).thenReturn(true);

        boolean allowed = rateLimiter.allowRequest("client-1");

        assertThat(allowed).isTrue();
        verify(fallbackLimiter).allowRequest("client-1");
    }
}
