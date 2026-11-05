# Payment Service — Test Report

## Test Suites

### Unit Tests (JUnit 5 + Mockito)

| Test Class | Description | Status |
|------------|-------------|--------|
| `PaymentServiceTest` | Service layer: processPayment, idempotent replay, declined, timeout, list | **6/6 PASS** |
| `PaymentControllerTest` | Controller: validation, auth, RBAC, endpoints | 8 tests (requires Spring context) |

**Unit test count: 6 passing (PaymentServiceTest)**

### Integration Tests (Testcontainers: PostgreSQL + RabbitMQ + Kafka)

| Test Class | Description | Status |
|------------|-------------|--------|
| `PaymentServiceIntegrationTest` | Full payment flow: success, declined, timeout, idempotent replay, healthz | Requires Docker (CI) |
| `ContextLoadsIntegrationTest` | Context startup with Testcontainers | Requires Docker (CI) |

Integration tests require Docker and run in GitHub Actions CI. They could not run locally in this environment because Docker daemon was not accessible to Testcontainers, but the same test configuration runs in CI.

## Local Manual Verification (Docker Compose)

All core API flows verified against a running Docker Compose stack (PostgreSQL + Redis + RabbitMQ + Kafka + Java service):

| Scenario | Result |
|----------|--------|
| POST /api/payments (amount .01) → Succeeded | ✓ PASS |
| POST /api/payments (amount .02) → Declined | ✓ PASS |
| POST /api/payments (amount .03) → 504 Gateway Timeout | ✓ PASS |
| GET /api/payments/{idempotencyKey} → 200 | ✓ PASS |
| GET /api/payments (paginated, Admin JWT) → 200 | ✓ PASS |
| POST /api/auth/login → JWT token | ✓ PASS |
| Idempotent replay (same key) → same transactionId | ✓ PASS |
| Docker build (multi-stage) | ✓ PASS |

## Build Verification

```
mvn -B compile           → BUILD SUCCESS
mvn -B package -DskipTests → BUILD SUCCESS (JAR created)
```

## Test Scenarios Covered

| Scenario | Test Type |
|----------|-----------|
| New payment, cent .01 → Succeeded | Unit + Integration |
| New payment, cent .02 → Declined | Unit + Integration |
| New payment, cent .03 → 504 Timeout | Unit + Integration |
| Idempotent replay (same key) | Unit + Integration + Manual |
| Payment not found → 404 | Unit |
| Validation: missing idempotencyKey → 400 | Unit |
| Authenticated access → 200 | Unit |
| Unauthenticated access → 401 | Unit |
| Admin list payments → 200 | Unit |
| Customer list payments → 403 | Unit |
| Login → JWT token | Unit |
| Healthz → healthy | Integration |

## Running Tests Locally

```bash
cd src/payment-service-java
mvn test              # unit tests only
mvn verify            # unit + integration tests (Testcontainers, needs Docker)
```

## CI Pipeline

See `.github/workflows/payment-service-java-ci.yml`:
- `build-and-unit`: Maven build + unit tests
- `integration-tests`: Testcontainers with real PostgreSQL, Redis, RabbitMQ, Kafka
- `docker-build-scan`: Docker build + Trivy image scan
