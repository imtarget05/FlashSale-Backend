# FlashSale Backend — Java-only (payment service)

| Badge | Value |
|---|---|
| ![Java 17](https://img.shields.io/badge/Java-17-007396?logo=java) | Runtime |
| ![Spring Boot 4](https://img.shields.io/badge/Spring%20Boot-4.0.9-6DB33F?logo=spring-boot) | Framework |
| ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15-4169E1?logo=postgresql) | Database |
| ![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis) | Cache / inventory mirror |
| ![RabbitMQ](https://img.shields.io/badge/RabbitMQ-Messaging-FF6600?logo=rabbitmq) | Messaging |
| ![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker) | Containerization |

## Overview

This repository contains a single Java/Spring Boot microservice: **payment-service** (`src/payment-service-java`). It handles simulated payment processing, idempotency, JWT auth, and RabbitMQ order events. PostgreSQL is the authoritative store; Redis backs the payment cache.

The previous C#/.NET solution and its projects (`FlashSale.Domain`, `Order.Api`, `HealthProbe`, OpsBridge, `AzureFlashSale.slnx`) have been removed to keep one stack: **Java-only**.

## Scope and product focus

The primary product is the Java backend. The `frontend/` folder is a thin demo shell for preview testing and smoke checks only; it is not the core business surface of the repo. Backend logic, payment idempotency, security, and API contracts remain the canonical source of truth.

## Module layout

```
src/payment-service-java/
├── pom.xml                  # Spring Boot 4.0.9, Java 17
├── Dockerfile               # multi-stage (maven build → temurin JRE)
├── docker/init-schema.sql   # PostgreSQL schema (payment_records, idempotency keys)
├── src/main/proto/          # Protocol Buffers gRPC contract
└── src/main/java/com/flashsale/payment/
    ├── controller/          # PaymentController, HealthController
    ├── service/             # PaymentService
    ├── repository/ + entity/# PaymentRepository, Payment (JPA)
    ├── dto/                 # PaymentRecordDto, ProcessPaymentRequest, LoginRequest, AuthResponse
    ├── exception/           # GlobalExceptionHandler, PaymentNotFoundException, IdempotencyConflictException
    ├── security/            # REST/JWT security and gRPC JWT interceptor
    ├── grpc/                # gRPC service adapter and TLS-only optional server
    ├── config/              # RabbitConfig, CacheConfig
    └── event/               # OrderEventConsumer, PaymentEventPublisher, OrderEventDTO
```

## Run locally

```bash
# Create a local env file and per-run demo credentials; do not commit .env.
cp .env.example .env
export JWT_SIGNING_KEY="$(openssl rand -base64 48)"
export DEMO_AUTH_PASSWORD="$(openssl rand -base64 24)"

# Start the Java service and its PostgreSQL, Redis, and RabbitMQ dependencies.
docker compose up --build -d

# Liveness and dependency readiness.
curl http://localhost:8081/healthz
curl http://localhost:8081/actuator/health/readiness
```

The Compose service maps `8081:8080` and waits for PostgreSQL, Redis, and
RabbitMQ to become healthy. Set the local database and RabbitMQ credentials in
`.env`; the service has no checked-in credential fallback.

## Optional gRPC API

The unary gRPC contract is in `src/payment-service-java/src/main/proto/payment.proto`.
`ProcessPayment` delegates to the same idempotent application service as REST
and requires a valid JWT with Staff or Admin role. `CheckHealth` is public.
The listener is disabled by default; enabling it requires TLS files and binds
port 9090:

```bash
GRPC_SERVER_ENABLED=true \
GRPC_TLS_CERT_CHAIN=/path/to/server.crt \
GRPC_TLS_PRIVATE_KEY=/path/to/server.key \
mvn -f src/payment-service-java/pom.xml spring-boot:run
```

Do not expose gRPC until certificates, client trust, and network policy are
configured. The AKS sample intentionally leaves the listener disabled and
does not publish port 9090. This is a code-level demo, not evidence of a
production gRPC deployment. The command assumes the same database, broker, and
JWT environment variables used by the local service are already configured.

## Optional MongoDB audit read model

The optional `mongodb` profile projects payment events into MongoDB while
PostgreSQL remains the authoritative payment store. Payment events are written
to the PostgreSQL outbox, relayed through RabbitMQ, and idempotently stored by
idempotency key. This projection is eventually consistent; a recently
processed payment may briefly return `404` until the event is consumed.

Start the local demo with the MongoDB overlay:

```bash
docker compose -f docker-compose.yml -f docker-compose.mongodb.yml up --build -d
```

The staff/admin-only `GET /api/audit/payments/{idempotencyKey}` endpoint reads
the MongoDB projection. If MongoDB cannot store a message, RabbitMQ routes it
to the dedicated `payment.mongo-audit.dlq` for operator recovery. This is a
non-production portfolio demonstration, not a financial ledger or a banking
compliance certification.

After starting the MongoDB overlay, run the local API smoke flow with the same
demo password used by Compose:

```bash
src/payment-service-java/scripts/smoke-local.sh
```

The script checks login, success, same-key replay, conflicting replay (409),
decline, timeout, and the asynchronously projected MongoDB audit event. It
does not deploy to cloud or substitute for the RabbitMQ DLQ integration tests.

## Tests

Unit tests (no Docker required):

```bash
cd src/payment-service-java
mvn -q test -Dtest='!*Integration*'
```

Integration tests use Testcontainers (PostgreSQL, Redis, RabbitMQ) and need a running Docker daemon:

```bash
mvn -q test
```

The Maven suite includes Testcontainers-backed PostgreSQL, Redis, and RabbitMQ
integration tests; see the latest exact-commit CI run for current counts and
results.

## Azure Pipelines CI

`azure-pipelines.yml` runs Java 17 `mvn clean verify` (including Testcontainers
tests) and a local Docker image build for `main`, `preview`, and pull requests.
It requires no cloud credentials and does not push images or deploy. Connect
this YAML file as an Azure DevOps pipeline to run it; this repository cannot
prove that a remote Azure DevOps run has passed.

## API surface (key endpoints)

| Endpoint | Method | Purpose |
|---|---|---|
| `/healthz` | GET | Liveness probe |
| `/actuator/health/readiness` | GET | Dependency readiness probe |
| `/api/payments` | POST | Process a payment (idempotent) |
| `/api/payments/{idempotencyKey}` | GET | Get payment record |
| `/api/auth/login` | POST | JWT login |

OpenAPI/Swagger UI is public at `/swagger-ui.html` only in the demo profile.
In the default, `production`, and `prod` profiles, `/swagger-ui/**` and
`/v3/api-docs/**` require Admin. The service does not yet have a production
identity provider, so this rule is a fail-closed boundary rather than evidence
of production login support.

## Schema

`docker/init-schema.sql` creates `payment_records` with an idempotency-key primary key. Apply it via the Postgres container entrypoint (compose mounts it to `/docker-entrypoint-initdb.d/`).

## Notes

- No real PSP contract, credential, or behaviour is exercised anywhere; payment outcomes are simulated for local/demo runs.
- Local mock identities exist only in the explicit `demo` profile and require
  `DEMO_AUTH_PASSWORD`; never enable this profile in production.
- `JWT_SIGNING_KEY` and `DEMO_AUTH_PASSWORD` are required for local demo compose
  and must not be committed.
- RabbitMQ is the only implemented broker in this demo. Kafka is not included in
  the runtime or tested here and should not be claimed as hands-on experience.
- The Java module includes a non-production AKS validation manifest and runbook
  at [AKS_DEPLOYMENT.md](src/payment-service-java/AKS_DEPLOYMENT.md); it is not a
  production deployment claim.
- Interview preparation should use the Java-scoped
  [evidence-based notes](docs/portfolio/flashsale-java-interview-notes.md),
  not claims from unrelated repositories.
