# Technical Interview Summary — FlashSale Payment Service

This document summarizes the five core engineering topics most likely to come up
in a backend/cloud interview for the FlashSale payment service
(`src/payment-service-java`, Spring Boot 3.2.5, Java 17). Each topic is tied to
the actual implementation and tests in this repository.

## 1. Idempotency

**Problem.** A flash-sale client may retry the same payment (network hiccup,
mobile double-tap) and we must not charge the customer twice.

**Implementation.** `processPayment` is keyed by an `idempotencyKey`:
- Before creating a payment, `PaymentRepository.findByIdempotencyKey(key)` is
  consulted. If a payment already exists for that key, the call is treated as a
  replay and the previously stored result is returned instead of creating a new
  payment (`PaymentService.processPayment`, `IdempotencyConflictException`).
- If two requests race for the same key, only one wins the insert; the loser
  detects the conflict and replays the winner's result rather than throwing a
  raw database error to the caller.

**Verification.** `PaymentOutboxRelayTest` and `PaymentSchemaMigrationTest`
exercise the idempotent insert/replay path; gRPC tests map an
`IdempotencyConflictException` to gRPC `ALREADY_EXISTS` ("Idempotency key
conflict") without leaking internal details.

## 2. Outbox Pattern

**Problem.** We must atomically persist a payment AND publish a domain event
(`payment.completed`) to the message broker. A local transaction cannot span
the database and RabbitMQ, so naive "save then publish" can lose events or
publish events for payments that were rolled back.

**Implementation.** The transactional outbox pattern:
- `PaymentOutbox` rows (`idempotencyKey`, `type`, `payload`, `attempts`,
  `lastError`, `nextAttemptAt`, `publishedAt`) are saved in the same
  PostgreSQL transaction as the payment.
- `PaymentOutboxRelay.publishPendingEvents()` locks due events
  (`lockDueEvents`), publishes each via `PaymentEventPublisher` (RabbitMQ),
  and marks the row published on success.
- On publish failure (`AmqpException`), the event stays `PENDING`,
  `attempts` is incremented, `lastError` is recorded, and `nextAttemptAt` is
  backoff-scheduled for retry — at-least-once delivery with explicit retry.

**Verification.** `PaymentOutboxRelayTest.failedPublish_remainsPendingAndCanBeRetried`
proves a failed publish keeps the event pending, records the error, schedules a
retry, and publishes exactly once on the retry. This demonstrates at-least-once
delivery with idempotent consumers downstream.

## 3. Caching Strategy (Redis, `CACHE_TYPE=redis`)

**Problem.** `getPayment` is read-heavy; repeated reads for the same idempotency
key should not hit PostgreSQL every time, but the cache must never block or
corrupt the payment flow when Redis is down.

**Implementation.**
- `getPayment` is `@Cacheable(value = "payments", key = "#idempotencyKey")`;
  `processPayment` is `@CacheEvict(value = "payments",
  key = "#request.idempotencyKey")` so a new payment invalidates the stale
  cached read (cache hit → miss → re-query on mutation).
- `CacheConfig` installs a `CacheErrorHandler` that logs cache get/put/evict
  failures (`RedisConnectionFailureException`) and lets the request continue —
  the cache is best-effort, never fail-closed for the business path.
- Readiness includes `redis` (`management.endpoint.health.group.readiness`),
  so a Redis outage is observable as a downstream `READINESS DOWN` signal
  while payment lookup still falls back to PostgreSQL.

**Verification.** `PaymentCacheRedisTest` covers cache miss (populate),
cache hit (no re-query), cache eviction (processPayment evicts → re-query),
and graceful fallback when Redis throws `RedisConnectionFailureException`
(falls back to the repository without error).
`CacheFailureFallbackTest` proves cache get/put/evict failures fall back to
the database read/write. `RedisOutageIntegrationTest` proves lookup continues
during a paused Redis container and recovers afterward.

## 4. Messaging Choices (RabbitMQ vs Kafka)

**Decision.** The service uses **RabbitMQ** (Spring AMQP 0-9-1), not Kafka.

**Why RabbitMQ.**
- The outbox relay publishes to RabbitMQ via `PaymentEventPublisher`; the
  topology test (`DefaultMessagingTopologyTest`) asserts `RabbitTemplate` and
  `OrderEventConsumer` are present and that no Kafka classes (`KafkaTemplate`,
  `KafkaOrderEventConsumer`) are on the classpath.
- Azure Service Bus is **incompatible** with Spring AMQP 0-9-1
  (project constraint `azure.rabbitmq_servicebus_incompatibility`), so a
  managed/container RabbitMQ (CloudAMQP-compatible TLS/vhost) is used instead
  of Service Bus.
- RabbitMQ suits the outbox/retry model (per-queue delivery, acknowledgements,
  DLQ-friendly) better than Kafka's log model for this request/reply payment
  flow.

**Trade-off.** Kafka would be chosen for high-throughput event streaming or
log compaction; RabbitMQ was chosen because the outbox needs reliable,
acknowledged delivery with retry/backoff and DLQ handling — a better fit for
payment events.

## 5. Token Signing (HS256 vs RSA)

**Demo profile (HS256).** `JwtUtil` signs demo JWTs with `app.jwt.secret`
using HMAC-SHA256 (`io.jsonwebtoken` / jjwt). The secret must be at least 32
bytes and is never logged (`security.leaked_secrets_must_be_rotated`). Demo
tokens are validated by `JwtAuthFilter` + `AuthService`.

**Production profile (OIDC / RSA).** `ProductionSecurityConfig` (`@Profile
("production | prod")`) replaces demo auth with OIDC bearer validation:
- `OIDC_ISSUER_URI` must be an absolute HTTPS URL (no userinfo/query/fragment).
- `OIDC_AUDIENCE` is mandatory; tokens without it, or with a different
  audience, are rejected with `JwtValidationException` → HTTP 401.
- Keys are rotated via JWKS (`NimbusJwtDecoder` with a JWKS endpoint,
  `OidcJwksRotationTest` proves decoder accepts a new signing key during a
  JWKS overlap window). RSA-256 is used so public key rotation does not require
  redistributing a shared secret.

**HS256 vs RSA.** HS256 is simple and fine for a single-service demo with a
shared secret. RSA/OIDC is required in production because verification keys can
be published (JWKS) and rotated without sharing a secret, and because the issuer
+ audience model supports multi-service trust. `ProductionOidcSecurityTest`
verifies the HTTP mapping: expired token → 401, invalid issuer → 401, missing
audience → 401, insufficient role → 403, valid staff token → 200.

---

**How to run the relevant tests:**
```
cd src/payment-service-java
mvn -B test -Dtest='!*Integration*'   # full unit suite (144 tests, green)
mvn -B test -Dtest='PaymentCacheRedisTest,CacheFailureFallbackTest'  # caching
mvn -B test -Dtest='ProductionOidcSecurityTest,ProductionOidcConfigTest'  # OIDC
mvn -B test -Dtest='PaymentGrpcServiceImplTest'  # gRPC ProcessPayment/CheckHealth
```
