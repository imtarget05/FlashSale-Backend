# FlashSale-Backend — High-Reliability Flash-Sale Payment Microservice

[![Java 17](https://img.shields.io/badge/Java-17-orange.svg)](https://openjdk.org/projects/jdk/17/)
[![Spring Boot 4.0.9](https://img.shields.io/badge/Spring_Boot-4.0.9-green.svg)](https://spring.io/projects/spring-boot)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15-blue.svg)](https://www.postgresql.org/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3.13-orange.svg)](https://www.rabbitmq.com/)
[![Redis](https://img.shields.io/badge/Redis-7-red.svg)](https://redis.io/)
[![Testcontainers](https://img.shields.io/badge/Testcontainers-1.20.6-black.svg)](https://testcontainers.com/)
[![Security: Fail-Closed OIDC](https://img.shields.io/badge/Security-Fail--Closed_OIDC-blueviolet.svg)](#4-defense-in-depth-security--access-governance)
[![Tests: 173 Passed](https://img.shields.io/badge/Tests-173_Passed_(0_Failures)-brightgreen.svg)](#6-verifiable-claims-to-evidence-matrix)

---

## 1. Executive Summary & Recruiter Quick Facts (60–90s Scan)

FlashSale-Backend is a high-throughput, fault-tolerant payment microservice engineered in **Java 17 and Spring Boot 4.0.9**. It is designed specifically to handle extreme flash-sale traffic surges where thousands of customers attempt concurrent purchases against constrained stock and sensitive payment gateways.

Instead of relying on fragile in-memory synchronization or optimistic application-level checks, the system implements **database engine-level idempotent deduplication**, an **atomic Transactional Outbox pattern with correlated publisher confirms**, **resilient look-aside caching with graceful database fallback**, and **fail-closed OIDC security**.

### 60-Second Recruiter Summary

| Architectural Concern | Implementation Mechanism | Local & CI Verification |
|---|---|---|
| **Codebase Scope** | **100% Java Microservice**. All legacy C#/.NET ASP.NET Core artifacts (`Dockerfile.api`, `Dockerfile.payment`, `OpsBridge.slnx`, `tests/OpsBridge.UnitTests/`) were cleanly removed. | Confirmed: 0 C#/.NET files in repository tree (`find . -name "*.cs"` yields 0). |
| **Concurrency & Idempotency** | PostgreSQL `INSERT ... ON CONFLICT (idempotency_key) DO NOTHING` in single native query; winner replay on identical payload (HTTP 200), tamper detection on payload alteration (HTTP 409 Conflict). | `PaymentServiceIntegrationTest` concurrent multi-threaded suite (`CountDownLatch` & `CompletableFuture`). |
| **Dual-Write Consistency** | Atomic DB transaction committing payment record and outbox table simultaneously; scheduled polling relay using `SELECT ... FOR UPDATE SKIP LOCKED` across multi-instance replicas. | Testcontainers integration drill verifying zero missing outbox records and zero duplicate locks. |
| **Asynchronous Messaging** | Correlated RabbitMQ publisher confirms with 5-second SLA, mandatory return callback validation, and Dead-Letter Queue (`payment.dlq`) via manual channel `basicNack(requeue=false)`. | `RabbitMqOutageIntegrationTest`: Broker outage drill (`rabbitmqctl stop_app/start_app`) with exponential retry backoff. |
| **Cache Fault Tolerance** | Redis look-aside cache (`@Cacheable` / `@CacheEvict`) backed by custom `CacheErrorHandler` logging warnings and falling through to PostgreSQL without throwing 500s. | `RedisOutageIntegrationTest` using Testcontainers Docker API container pause (`dockerClient.pauseContainerCmd`). |
| **Security & Governance** | Fail-closed OIDC resource server under production profile (mandatory HTTPS issuer, audience check, strict CORS, security headers), BOLA ownership checks, and token bucket rate limiting (100 req/min). | `ProductionOidcConfigTest`, `ProductionSecurityHeadersTest`, `AuthSecurityIntegrationTest`. |
| **DevSecOps Automation** | Multi-stage Dockerfile (`USER 10001:10001`), Trivy image scanner (`HIGH,CRITICAL`), CycloneDX SBOM generation, Kubernetes manifest dry-run parsing, and Terraform validation in CI. | GitHub Actions CI (`.github/workflows/ci.yml`), `scripts/ci-gate.sh`. |
| **Automated Test Suite** | **173 automated tests passed (0 failures, 0 errors)**: 123 unit tests + 50 Testcontainers-backed integration tests (PostgreSQL 15, RabbitMQ 3, Redis 7, MongoDB 7). | Full test run reproducible via `mvn test -Dspring.flyway.enabled=true`. |

---

## 2. Architecture & Design Decisions (Ports & Adapters)

The service is organized strictly according to **Hexagonal Architecture (Ports and Adapters)**, ensuring that domain business logic remains completely decoupled from messaging brokers, databases, caching layers, and external payment processors.

```
                           +-------------------------------------------------------+
                           |               INBOUND ADAPTERS (Driving)              |
                           |  REST API (/api/payments)    gRPC Server (Port 9090)  |
                           |  Spring MVC Controllers      RabbitMQ Order Consumer  |
                           +---------------------------+---------------------------+
                                                       |
                                                       v
+---------------------------------------------------------------------------------------------------------+
|                                    APPLICATION & DOMAIN LAYER                                           |
|                                                                                                         |
|  [PaymentService]                                                                                       |
|    - Idempotency & Replay Engine (Winner Evaluation / Payload Conflict Detection)                       |
|    - Transaction Boundary Orchestration (@Transactional)                                                |
|    - Look-Aside Cache Invalidation (@CacheEvict, @Cacheable)                                            |
|                                                                                                         |
|  Domain Models: Payment (Entity), ProcessPaymentRequest (DTO), PaymentRecordDto (Immutable Record)      |
+---------------------------------------------------------------------------------------------------------+
                                                       |
                                                       v
                           +-------------------------------------------------------+
                           |              OUTBOUND ADAPTERS (Driven)               |
                           |  - Persistence: PaymentRepository (PostgreSQL JPA)    |
                           |  - Outbox: PaymentOutboxRepository (SKIP LOCKED)      |
                           |  - Broker: PaymentEventPublisher (RabbitMQ Confirms)  |
                           |  - Cache: RedisCacheManager (with Fallback Handler)   |
                           |  - PSP Gateway: PaymentProvider Port                  |
                           |      * SimulatedPaymentProvider (Local / Integration) |
                           |      * StripePaymentProvider (HTTP Client Adapter)    |
                           +-------------------------------------------------------+
```

### Key Architectural Trade-Offs

1. **Modular Microservice Boundary vs. Distributed Microservices Complexity**:
   - *Problem*: Flash-sale order-to-payment workflows often suffer from distributed transaction failures (SAGA complexity, two-phase commit overhead, partial network partitions).
   - *Decision*: Maintain a self-contained payment bounded context managing its own database schema (`payment_records`, `payment_outbox`) and consuming asynchronous order events over RabbitMQ.
   - *Trade-off*: Eventual consistency is accepted for order-to-payment communication, but strict ACID consistency is enforced within the payment domain.

2. **Database Engine Deduplication vs. Distributed Redis Locking**:
   - *Problem*: High-concurrency checkout requests can submit identical idempotency keys milliseconds apart. Using distributed locks (e.g., Redlock) introduces network round-trips, lock acquisition timeouts, clock drift issues, and potential split-brain scenarios during Redis failover.
   - *Decision*: Delegate unique constraint enforcement directly to PostgreSQL via `INSERT ... ON CONFLICT (idempotency_key) DO NOTHING`.
   - *Benefit*: PostgreSQL's write-ahead log (WAL) and row-level locks act as the authoritative single source of truth. Zero lock leasing overhead.

3. **Polling Outbox Relay vs. Change Data Capture (Debezium / Kafka Connect)**:
   - *Problem*: Publishing directly to RabbitMQ inside the payment transaction creates a dual-write vulnerability (transaction rolls back, but event was already published; or broker crashes after commit).
   - *Decision*: Write outbox records into the same ACID transaction. A scheduled relay polls pending records using `SELECT ... FOR UPDATE SKIP LOCKED`.
   - *Trade-off*: Polling introduces a slight sub-second publication delay compared to stream tailing, but avoids the infrastructure overhead, Zookeeper/Kafka dependencies, and operational complexity of running Debezium connectors for a single microservice.

---

## 3. High-Concurrency Reliability & Resilience Mechanisms

### A. Atomic Idempotency & Deduplication

In a flash-sale scenario, aggressive clients or network retries often send duplicate HTTP requests. The microservice ensures that **no customer is double-charged** and that identical requests return deterministic results.

#### 1. Native Database Engine Deduplication
In `PaymentRepository.java` (lines 21–35), payment persistence bypasses naive JPA `save()` checks in favor of a native PostgreSQL upsert query:

```java
@Modifying
@Query(value = """
    INSERT INTO payment_records (
        idempotency_key, payment_id, order_id, customer_email, amount, currency, success, status,
        transaction_id, failure_reason, is_transient_error, processed_at
    ) VALUES (
        :#{#record.idempotencyKey}, :#{#record.paymentId}, :#{#record.orderId},
        :#{#record.customerEmail},
        :#{#record.amount}, :#{#record.currency}, :#{#record.success}, :#{#record.status},
        :#{#record.transactionId}, :#{#record.failureReason}, :#{#record.isTransientError},
        :#{#record.processedAt}
    )
    ON CONFLICT (idempotency_key) DO NOTHING
    """, nativeQuery = true)
int insertIfAbsent(@Param("record") Payment record);
```

#### 2. Winner Replay vs. Payload Tampering Detection
In `PaymentService.java` (lines 76–85, 109–117):
- **Race Condition Handling**: If two concurrent threads execute `insertIfAbsent`, the winning thread receives return value `1` and proceeds to commit the transaction and outbox event. The losing thread receives `0`.
- **Winner Replay (HTTP 200)**: The losing thread queries `findByIdempotencyKey` to read the winner's committed record. If the incoming payload (`paymentId`, `orderId`, `amount`, `currency`) matches the stored record, it safely replays the stored outcome with HTTP 200.
- **Payload Tampering Detection (HTTP 409 Conflict)**: If a client reuses an existing idempotency key with altered parameters (e.g., different order amount or currency), the service detects payload tampering in `replay()` and throws `IdempotencyConflictException`, which maps to **HTTP 409 Conflict**.

```java
private PaymentRecordDto replay(ProcessPaymentRequest request, Payment stored) {
    if (!stored.getPaymentId().equals(request.paymentId())
        || !stored.getOrderId().equals(request.orderId())
        || stored.getAmount().compareTo(request.amount()) != 0
        || !stored.getCurrency().equals(request.currency())) {
        throw new IdempotencyConflictException("Idempotency key already belongs to a different payment");
    }
    return toDto(stored);
}
```

#### 3. Verification Evidence
- `PaymentServiceIntegrationTest.java` (lines 468–481): `concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed` executes two concurrent threads using a synchronized `CountDownLatch` and `CompletableFuture`. Verifies both requests receive HTTP 200, exactly 1 row exists in `payment_records`, and exactly 1 event exists in `payment_outbox`.
- `PaymentServiceIntegrationTest.java` (lines 484–499): `concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment` verifies that the race winner succeeds with HTTP 200, the conflicting request receives HTTP 409, and exactly 1 row is written to the database.

---

### B. Transactional Outbox Pattern & Reliable Messaging

To guarantee **at-least-once message delivery** without distributed 2PC transactions, the service implements the Transactional Outbox pattern.

#### 1. Atomic Database Commitment & Partial Indexing
Payment state and outbox events are inserted within the same `@Transactional` boundary in `PaymentService.java` (lines 54, 76, 91–96). The `payment_outbox` table uses a partial index (`V1__create_payment_schema.sql`, lines 28–30) to ensure high-speed querying even when millions of historical events have already been published:

```sql
CREATE INDEX ix_payment_outbox_pending
    ON payment_outbox (next_attempt_at, created_at)
    WHERE published_at IS NULL;
```

#### 2. Multi-Instance Safe Polling Relay (`SKIP LOCKED`)
Multiple replicas of the payment microservice can run concurrently without double-publishing. In `PaymentOutboxRepository.java` (lines 15–22), due outbox events are fetched using `FOR UPDATE SKIP LOCKED`:

```sql
SELECT * FROM payment_outbox
WHERE published_at IS NULL AND next_attempt_at <= :now
ORDER BY created_at ASC
LIMIT :batchSize
FOR UPDATE SKIP LOCKED
```
Any row currently locked by another service instance is skipped immediately without thread blocking or lock contention.

#### 3. Correlated Publisher Confirms & Mandatory Routing
In `PaymentEventPublisher.java` (lines 39–60), publishing is not fire-and-forget:
- An asynchronous `CorrelationData` object tracks the message.
- The thread blocks up to 5 seconds (`CONFIRM_TIMEOUT_SECONDS`) awaiting `confirm.ack()`. If RabbitMQ rejects the message or times out, an `AmqpException` is thrown.
- `correlation.getReturned()` checks whether RabbitMQ returned the message due to unroutable keys. If returned, the publish is treated as a failure.
- When a publish fails, `PaymentOutboxRelay.java` applies exponential backoff (`delaySeconds = Math.min(300L, 1L << Math.min(attempts - 1, 8))`) and increments `attempts` for future retry.

#### 4. Dead-Letter Queue (DLQ) & Manual Acknowledgments
In `OrderEventConsumer.java` (lines 33, 65–79):
- The RabbitMQ listener operates in manual acknowledgment mode (`ackMode = "MANUAL"`).
- Upon successfully processing an incoming order event into a payment and outbox record, the worker invokes `channel.basicAck(deliveryTag, false)`.
- If an unparseable JSON payload or fatal processing exception occurs, the worker invokes:
  ```java
  channel.basicNack(deliveryTag, false, false); // requeue = false
  ```
  Setting `requeue = false` instructs RabbitMQ not to re-deliver the poison message in a tight loop. Instead, RabbitMQ routes the message directly to the Dead-Letter Queue (`payment.dlq`) via the exchange configuration in `RabbitConfig.java` (`x-dead-letter-exchange` and `x-dead-letter-routing-key`).

#### 5. Verification Drill
- `RabbitMqOutageIntegrationTest.java`: Executes `rabbitmqctl stop_app` to simulate total message broker loss during outbox publishing. Verifies that the outbox relay catches the outage, records errors, applies exponential delay, and upon executing `rabbitmqctl start_app`, recovers and successfully delivers pending events.

---

### C. Resilient Redis Look-Aside Caching & Graceful Fallback

Redis is utilized as a read-through / look-aside cache to protect the primary PostgreSQL database from lookup stampedes during flash sales.

#### 1. Look-Aside Cache Invalidation
- `PaymentService.java` (line 119): `@Cacheable(value = "payments", key = "#idempotencyKey")` caches payment DTOs by idempotency key.
- `PaymentService.java` (line 55): `@CacheEvict(value = "payments", key = "#request.idempotencyKey")` immediately invalidates cached data whenever a new payment operation occurs.

#### 2. Fail-Closed / Graceful Degradation Handler
A common vulnerability in distributed architectures is microservice failure when a secondary caching layer crashes. In `CacheConfig.java` (lines 43–67), the service implements a custom `CacheErrorHandler`:

```java
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
```

If Redis suffers a network timeout or connection breakdown:
- Cache errors are intercepted and logged as warnings (`[payment-service] cache ... failed`).
- **Zero HTTP 500 errors are returned to clients**.
- The execution seamlessly falls through to PostgreSQL to retrieve or write authoritative data.

#### 3. Verification Evidence
- `PaymentCacheRedisTest.java`: 5 unit/slice tests verifying Cache Miss, Cache Hit, Cache Evict, Graceful Fallback, and BOLA customer ownership checks.
- `RedisOutageIntegrationTest.java`: Performs an outage drill using the Testcontainers Docker API:
  ```java
  redis.getDockerClient().pauseContainerCmd(redis.getContainerId()).exec();
  ```
  Verifies that `getPayment` requests continue returning valid data directly from PostgreSQL, Spring Boot Actuator readiness probe reports `DOWN` (503), and once unpaused, the cache recovers transparently.

---

## 4. Defense-in-Depth Security & Access Governance

Security is designed with zero-trust principles, enforcing strict boundaries between public customers and internal staff/administrators.

### 1. Fail-Closed OIDC Resource Server (Production Profile)
When running under `SPRING_PROFILES_ACTIVE=production` (`ProductionSecurityConfig.java`, lines 33–152):
- **Mandatory HTTPS Issuer**: `requireHttpsIssuer` validates that `OIDC_ISSUER_URI` uses the `https://` protocol and rejects any embedded user info, query parameters, or fragments.
- **Audience Validation**: `requireAudience` intercepts every JWT and confirms the `aud` claim matches the configured `OIDC_AUDIENCE`. Missing or mismatched audience claims reject authentication with `JwtValidationException`.
- **Strict CORS Enforcement**: Wildcard origins (`*`) are disallowed. CORS is strictly restricted to explicitly declared domains.
- **Security Response Headers**: Every response enforces `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, Content-Security-Policy (CSP), and HTTP Strict Transport Security (HSTS).

### 2. Stateless Role-Based Access Control (RBAC)
Configured in `SecurityConfig.java` (lines 48–66):
- `POST /api/payments`: Restricted to `STAFF` or `ADMIN`.
- `GET /api/payments/{idempotencyKey}`: Permitted for `CUSTOMER`, `STAFF`, or `ADMIN`.
- `GET /api/payments` (paginated): Restricted to `STAFF` or `ADMIN`.
- `/api/audit/**`: Restricted to `STAFF` or `ADMIN`.
- `/api/auth/**`: Disabled (`denyAll()`) under production profile to prevent in-memory auth abuse.
- Swagger OpenAPI UI: Restricted to `ADMIN` under production profile.

### 3. Broken Object Level Authorization (BOLA / IDOR) Protection
Even with a valid JWT, customers must not inspect other users' payment records:
- In `PaymentController.java` (lines 80–88) and `PaymentService.java` (lines 127–132), when a user with `ROLE_CUSTOMER` requests `/api/payments/{idempotencyKey}`, the service compares the stored `payment.customerEmail` against the authenticated token's `authentication.getName()`.
- If an unprivileged user attempts to view a payment belonging to another customer, access is rejected immediately with **HTTP 403 Forbidden**.

### 4. Token Bucket Rate Limiting
To mitigate brute-force denial-of-service and automated checkout bot attacks, `InMemoryTokenBucketRateLimiter.java` (lines 21–60) enforces a client IP rate limit:
- Capacity: 100 tokens per client IP address.
- Continuous token refill rate: 100 tokens per minute (1.67 tokens/sec).
- When a client exhausts their token allowance, `PaymentController` rejects the request with **HTTP 429 Too Many Requests**:
  `{"error":"RATE_LIMITED","message":"Rate limit exceeded. Try again later."}`.

---

## 5. DevSecOps Automation & Production Readiness

The repository implements a hardened automated delivery pipeline adhering to enterprise security and container best practices.

### 1. Hardened Multi-Stage Docker Build
Defined in `/Dockerfile`:
```dockerfile
# Stage 1: Build
FROM maven:3.9-eclipse-temurin-17 AS build
WORKDIR /workspace
COPY src/payment-service-java/pom.xml ./payment-service-java/pom.xml
COPY src/payment-service-java/src ./payment-service-java/src
RUN --mount=type=cache,target=/root/.m2 \
    mvn -q -f payment-service-java/pom.xml -DskipTests package

# Stage 2: Minimal Distroless-Style JRE Runtime
FROM eclipse-temurin:17-jre
WORKDIR /app
COPY --from=build /workspace/payment-service-java/target/*.jar /app/app.jar
EXPOSE 8080
USER 10001:10001
HEALTHCHECK --interval=10s --timeout=3s --retries=5 --start-period=20s \
  CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["java", "-jar", "/app/app.jar"]
```
- **Non-Root Execution**: Runs as unprivileged UID/GID `10001:10001`, preventing container breakout privileges.
- **Cache Mounts**: Uses Buildx `--mount=type=cache,target=/root/.m2` for accelerated build cycles.
- **Native Health Check**: Built-in container health probe against `/healthz`.

### 2. GitHub Actions CI Pipeline (`.github/workflows/ci.yml`)
The automated CI pipeline executes 5 parallel and sequential quality gates:
1. `build-and-unit`: Compiles with Java 17 and runs all unit tests (`mvn test -Dtest='!*Integration*'`).
2. `integration-tests`: Provisions live PostgreSQL 15, Redis 7, and RabbitMQ 3 service containers and runs full integration verification (`mvn verify`).
3. `sonar-scan`: Collects JaCoCo test coverage and runs SonarCloud static analysis.
4. `docker-build`: Builds the container image, runs a **Trivy vulnerability scan** targeting `HIGH` and `CRITICAL` CVEs (`ignore-unfixed: true`), and generates a **CycloneDX Software Bill of Materials (SBOM)** (`sbom.cdx.json`) archived as an artifact.
5. `validate-manifests`: Parses all Kubernetes manifests with a strict YAML parser; runs `terraform fmt -check`, `terraform init -backend=false`, and `terraform validate` across IaC definitions.

### 3. Strict Secrets Hygiene
- Zero plaintext passwords, API keys, or cloud credentials committed to the repository.
- Parameter substitution enforces environment-variable provisioning: `${RABBITMQ_USER:?Set in .env}`.
- CI workflows source credentials exclusively from GitHub Actions Secrets (`${{ secrets.POSTGRES_USER }}`, etc.).

---

## 6. Verifiable Claims-to-Evidence Matrix

Every capability asserted in this portfolio is grounded in reproducible code, integration tests, or CI pipeline definitions.

| # | Architecture / Resilience Claim | Source Code Reference | Verification Test Suite / Evidence |
|---|---|---|---|
| **1** | **PostgreSQL Engine Deduplication** | `PaymentRepository.java`: lines 21–35 (`ON CONFLICT (idempotency_key) DO NOTHING`) | `PaymentServiceIntegrationTest.java`: lines 468–481 (`concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed`) |
| **2** | **Payload Tampering Detection (409)** | `PaymentService.java`: lines 109–116 (`replay()` throws `IdempotencyConflictException`) | `PaymentServiceIntegrationTest.java`: lines 484–499 (`concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment`) |
| **3** | **Transactional Outbox Consistency** | `PaymentService.java`: lines 54, 76, 91–96 (`@Transactional` with `outboxRepository.save`) | `PaymentServiceIntegrationTest.java`: lines 208–224 (`validOrderEvent_persistsPaymentAndPublishesCompletion`) |
| **4** | **Multi-Instance Safe Polling Relay** | `PaymentOutboxRepository.java`: lines 15–22 (`FOR UPDATE SKIP LOCKED`) | `PaymentOutboxRelay.java` / Testcontainers concurrent poll verification |
| **5** | **RabbitMQ Publisher Confirms & Returns** | `PaymentEventPublisher.java`: lines 39–60 (`correlation.getFuture().get()`, `correlation.getReturned()`) | `RabbitMqOutageIntegrationTest.java`: lines 80–123 (`publishPendingEvents` retry on broker outage) |
| **6** | **Manual ACK & Dead-Letter Queue (DLQ)** | `OrderEventConsumer.java`: lines 33, 65–79 (`basicNack(deliveryTag, false, false)`), `RabbitConfig.java`: line 40 | `PaymentServiceIntegrationTest.java`: lines 288–296 (`malformedOrderEvent_isDeadLettered`) |
| **7** | **Redis Look-Aside Caching (Hit/Miss/Evict)** | `PaymentService.java`: line 55 (`@CacheEvict`), line 119 (`@Cacheable`) | `PaymentCacheRedisTest.java`: lines 208–288 (`testCacheMiss_*`, `testCacheHit_*`, `testCacheEviction_*`) |
| **8** | **Cache Outage Fallback Resilience** | `CacheConfig.java`: lines 43–67 (`CacheErrorHandler` logs warning, no exception thrown) | `RedisOutageIntegrationTest.java`: lines 74–105 (`paymentLookupFallsBackToPostgresDuringRedisOutageAndRecoversAfterward`) |
| **9** | **Fail-Closed OIDC Production Security** | `ProductionSecurityConfig.java`: lines 62–88 (`requireHttpsIssuer`, `requireAudience`) | `ProductionOidcConfigTest.java`: lines 26–65 |
| **10** | **BOLA / IDOR Ownership Protection** | `PaymentController.java`: lines 80–88, `PaymentService.java`: lines 127–132 | `PaymentCacheRedisTest.java`: lines 317–334 (`testBolaOwnershipCheck_deniesAccessWhenNotOwner`) |
| **11** | **Token Bucket Rate Limiting** | `InMemoryTokenBucketRateLimiter.java`: lines 21–60 (100 req/min per IP) | `PaymentController.java`: lines 58–61 (returns HTTP 429) |
| **12** | **Container Hardening & SBOM** | `Dockerfile`: lines 12–14 (`USER 10001:10001`), `.github/workflows/ci.yml`: lines 162–182 | Trivy scan (`HIGH,CRITICAL`), CycloneDX artifact `sbom.cdx.json` |
| **13** | **Index Optimization on Outbox & Schema** | `V1__create_payment_schema.sql`: lines 28–30 (`ix_payment_outbox_pending`) | `QueryOptimizationTest.java`: lines 55–88 (`EXPLAIN ANALYZE` confirms Index Scan on 1,000 rows) |
| **14** | **Clean Codebase (0 Legacy .NET Artifacts)** | Repository root: `src/payment-service-java/` only | File search: 0 `.cs`, `.csproj`, `.sln`, `.slnx` files exist |

---

## 7. Operational Honesty & Architectural Boundaries

A hallmark of senior engineering is transparently defining system boundaries and distinguishing between production-ready architecture and preview/demo cloud infrastructure.

### Production-Grade Capabilities (Implemented & Verified)
- **Application Architecture**: Strict Ports & Adapters separation, modular domain design, clean error contracts.
- **Data Integrity**: Zero dual-write hazard between PostgreSQL and RabbitMQ; zero double-charge hazard under concurrent identical requests.
- **Fail-Closed Security**: Mandatory HTTPS OIDC token verification with audience binding, granular RBAC, and BOLA checks.
- **Graceful Degradation**: Microservice remains operational and continues serving payment lookups when Redis crashes.

### Architectural Boundaries & Explicit Constraints
- **Managed Preview Deployment**: The live cloud deployment is hosted on **Render Free / Starter web services** with **CloudAMQP Little Lemur** (free tier) and managed Render PostgreSQL/Redis (`render.yaml`). It operates as a preview demonstration, not an enterprise multi-region cloud topology.
- **Payment Gateway Simulation**: To ensure reproducible testing without incurring real financial charges, payments default to `SimulatedPaymentProvider` (deterministic cents: `.01` = Success, `.02` = Declined, `.03` = 504 Timeout). While a complete `StripePaymentProvider` HTTP client adapter exists with JSON deserialization, it is gated by `@ConditionalOnProperty(name = "STRIPE_API_KEY")` and is not connected to live banking rails.
- **In-Memory Rate Limiter**: The token bucket rate limiter runs in heap memory (`ConcurrentHashMap`), which is ideal for a single-replica deployment. Multi-replica horizontal autoscaling would require moving the bucket counters into a shared Redis cluster via Lua scripts.
- **Kubernetes & Terraform State**: Kubernetes manifests (`infrastructure/kubernetes/`) and Terraform files (`infrastructure/terraform/`) are fully validated for syntax and schema compliance in CI, but are not actively deployed to an enterprise AKS cluster.

---

## 8. Local Setup & Verification Runbook

### Prerequisites
- **JDK 17** (Eclipse Temurin recommended)
- **Maven 3.9+**
- **Docker Desktop** (required for Testcontainers integration suites)

### 1. Run Unit Tests (No Docker Required)
Executes all unit tests, security assertions, cache slice tests, and mock provider checks:
```bash
cd src/payment-service-java
mvn test -Dtest='!*Integration*'
```
*Expected Result*: `BUILD SUCCESS` (123 tests passed, 0 failures).

### 2. Run Full Integration Suite (Testcontainers)
Spins up isolated PostgreSQL 15, Redis 7, RabbitMQ 3, and MongoDB 7 containers via Testcontainers:
```bash
cd src/payment-service-java
mvn test -Dspring.flyway.enabled=true
```
*Expected Result*: `BUILD SUCCESS` (**173 tests passed, 0 failures, 0 errors** across unit and integration suites).

### 3. Run End-to-End Local CI Gate
Simulates the entire GitHub Actions pipeline locally (compilation, unit testing, container build, user check, and Kubernetes manifest validation):
```bash
./scripts/ci-gate.sh
```
*Expected Output*: `=== Local CI gate PASSED ===`

### 4. Run with Docker Compose
Spin up the complete microservice ecosystem (PostgreSQL, Redis, RabbitMQ, and Payment Service):
```bash
docker compose up -d --build
```
Inspect service health:
```bash
curl -i http://localhost:8080/healthz
# HTTP/1.1 200 OK
```
Trigger a synthetic test payment:
```bash
curl -X POST http://localhost:8080/api/payments \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -d '{
    "paymentId": "550e8400-e29b-41d4-a716-446655440000",
    "orderId": 1001,
    "idempotencyKey": "order-1001",
    "amount": 10.01,
    "currency": "USD"
  }'
```
Replay the identical request to observe zero duplicate processing and deterministic HTTP 200 replay.
