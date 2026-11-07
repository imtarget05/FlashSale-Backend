# Recruiter Evidence — FlashSale-Backend (Java payment service)

This repository is **Java-only**. The C#/.NET ASP.NET Core artifacts (`Dockerfile.api`,
`Dockerfile.payment`, `OpsBridge.slnx`, `tests/OpsBridge.UnitTests/`) and the
OpsBridge bounded context have been removed from the tree. The active,
maintained implementation is the Java Spring Boot payment service under
`src/payment-service-java/`. Nothing below claims a live cloud deployment or a
real payment provider integration.

## Problem

Process flash-sale payments without double-charging when the same idempotency
key is submitted concurrently, simulate gateway outcomes (success / declined /
timeout), and keep payment state durable in PostgreSQL while notifying
downstream systems through RabbitMQ.

## Architecture (verified)

Ports & Adapters, Java 17 + Spring Boot 4.0.9:

- `PaymentProvider` port isolates PSP integration; `SimulatedPaymentProvider` is
  a deterministic local simulation (not a real PSP). A `StripePaymentProvider`
  HTTP-client adapter exists but is `@ConditionalOnProperty` on
  `STRIPE_API_KEY` and is **not** exercised against live Stripe in this repo.
- PostgreSQL is the source of truth; `payment_records` uses
  `INSERT ... ON CONFLICT DO NOTHING` keyed by idempotency key (V1/V2 Flyway
  migrations).
- Redis backs the `payments` cache (`@Cacheable` on `getPayment`,
  `@CacheEvict` on `processPayment`) via `CacheConfig` with a fail-closed
  `CacheErrorHandler`. Redis is optional (`CACHE_TYPE`); the service falls back
  to PostgreSQL when Redis is unavailable.
- Transactional outbox (`payment_outbox`) published to RabbitMQ with correlated
  publisher confirms; malformed events go to a dead-letter queue.
- JWT auth (local/demo) and an OIDC resource-server production profile that is
  fail-closed (rejects missing/invalid issuer or audience). Role-based access:
  CUSTOMER / STAFF / ADMIN.

## Verified capabilities (CV evidence — reproducible locally)

The local suite `mvn test` passes cleanly: **173 tests, 0 failures, 0 errors**
(Testcontainers-backed integration tests use Docker; no cloud credentials).

| Capability | Evidence (reproducible locally) |
|---|---|
| Idempotent payment — same key + same payload → one DB row, one outbox event | `PaymentServiceIntegrationTest.concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed`, `processPayment_idempotentReplay_returnsSameResult` |
| Idempotency conflict — same key, different payload → HTTP 409 | `PaymentServiceIntegrationTest.concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment` |
| Gateway simulation — cents `.02` → Declined, `.03` → TimedOut | `PaymentServiceIntegrationTest.processPayment_declined_whenCentsIsTwo`, `processPayment_transientTimeout_whenCentsIsThree` |
| Redis cache miss → queries repository and populates cache | `PaymentCacheRedisTest.testCacheMiss_queriesRepositoryAndPopulatesCache` |
| Redis cache hit → 2nd call returns cached DTO without re-querying | `PaymentCacheRedisTest.testCacheHit_returnsCachedDtoWithoutRequeryingRepository` |
| Redis cache evict → `processPayment` evicts key, subsequent get re-queries | `PaymentCacheRedisTest.testCacheEviction_processPaymentEvictsCacheAndSubsequentGetMisses` |
| Graceful fallback — Redis down → `getPayment` still returns from PostgreSQL | `CacheFailureFallbackTest`, `RedisOutageIntegrationTest`, `BrokerCacheFailureDrillTest.redisOutage_getPaymentFallsBackToPostgres` |
| OIDC fail-closed — production profile rejects missing/invalid issuer or audience | `ProductionOidcConfigTest` |
| JWT validation — expired/tampered/missing token → 401 | `AuthSecurityIntegrationTest` |
| Role-based access — CUSTOMER POST /api/payments → 403; STAFF/ADMIN → 200 | `AuthSecurityIntegrationTest` |
| Transactional outbox — outbox row → RabbitMQ publish with confirms | `PaymentOutboxRelay`, `PaymentServiceIntegrationTest.validOrderEvent_persistsPaymentAndPublishesCompletion` |
| DLQ — malformed order events dead-lettered | `PaymentServiceIntegrationTest.malformedOrderEvent_isDeadLettered` |
| Local CI gate — compile + unit tests + Docker build + k8s manifest validation, no cloud credentials | `scripts/ci-gate.sh`, `.github/workflows/ci.yml` |

## CV bullet points (honest, verified)

- Designed and implemented an idempotent Java/Spring Boot payment service for flash-sale checkout: PostgreSQL source of truth with `ON CONFLICT DO NOTHING` deduplication, Redis look-aside cache with graceful fallback, and a RabbitMQ transactional outbox with DLQ. **173 automated tests pass locally (0 failures), including cache hit/miss/evict/fallback and Redis-outage scenarios.**
- Implemented fail-closed JWT/OIDC authentication with role-based authorization (Customer/Staff/Admin) and verified security behavior with integration tests (expired/missing tokens → 401, unauthorized role → 403).
- Wrote Testcontainers-backed integration tests (PostgreSQL, Redis, RabbitMQ) and a local CI gate (`scripts/ci-gate.sh`) that compiles, runs unit tests, builds a Docker image, and validates Kubernetes manifests — no cloud credentials required.
- Maintained a Java-only portfolio repository: removed legacy .NET artifacts and aligned CI workflows to reference GitHub Actions secrets (`${{ secrets.* }}`) instead of plaintext credentials.

## Not claimed — do not present these as verified

| Claim | Status |
|---|---|
| Real payment service provider (live Stripe) | **Never exercised.** `StripePaymentProvider` requires a real `STRIPE_API_KEY`; tests use a dummy key and cover JSON parsing only. |
| Live Render / AKS / production deployment | **Configuration only.** k8s overlays exist under `src/payment-service-java/k8s/overlays/{local,production}` and `infrastructure/kubernetes/`; never applied to a real cluster. |
| Production-scale data or production readiness | **None.** Schema verified from empty against local PostgreSQL only. |
| Azure infrastructure provisioned by this repo | **None.** Verification runs locally with Docker/Kind/testcontainers; no Azure resources are created or mutated. |
| Kafka, Azure Service Bus, or Azure Container Apps | **Not included.** RabbitMQ is the only broker implemented and tested. |

This is an index of verified evidence, not a production-readiness claim. Nothing in
this repository is production-ready, and no statement above should be presented as
one. Interview discussion should reference the Java-scoped
[evidence-based notes](docs/portfolio/flashsale-java-interview-notes.md).
