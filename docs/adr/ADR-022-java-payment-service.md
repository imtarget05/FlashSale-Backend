# ADR-022: Java Spring Boot Payment Service

## Status

Accepted locally; current messaging decision is recorded in ADR-027. Cloud runtime is not verified.

## Context

The FlashSale-Backend repository is primarily a .NET 10 platform (Order API, Order Worker, Payment Service, OpsBridge). A Backend Developer JD requires demonstrated Java Spring Boot experience — REST API, Spring Data JPA, Spring Security (JWT/RBAC), transaction handling, RabbitMQ/Kafka, Redis, Docker, CI/CD.

The existing .NET Payment Service already provides:
- `POST /api/payments` with idempotency key
- `GET /api/payments/{idempotencyKey}`
- PostgreSQL `payment_records` table (idempotency key as PK)
- RabbitMQ event publishing
- Health endpoints

Adding a Java Spring Boot payment service alongside the .NET one provides direct, interview-ready evidence of Java backend capability without replacing or duplicating the .NET service.

## Decision

Add a **Java Spring Boot Payment Service** as a new bounded context in `src/payment-service-java/`, running alongside the existing .NET Payment Service.

### Service Contract

The Java service exposes the same HTTP contract as the .NET service:
- `POST /api/payments` — ProcessPaymentRequest → PaymentRecordDto
- `GET /api/payments/{idempotencyKey}` — lookup by idempotency key
- `GET /api/payments` — paginated list (STAFF/ADMIN only)
- `POST /api/auth/login` — JWT token issuance
- `/health`, `/health/live`, `/healthz`, `/health/ready` — health probes

### Database

- Same PostgreSQL instance (`FlashSaleDb`), same `payment_records` table.
- Java service uses Spring Data JPA with `idempotency_key` as `@Id`.
- DDL is validated (`spring.jpa.hibernate.ddl-auto=validate`), not auto-created — the .NET service owns migrations.

### Messaging

- **RabbitMQ** is the sole Java runtime broker: the service publishes `payment.completed` / `payment.failed` to `payment.events` and consumes `order.created` from `order.events`. Malformed or failed deliveries are dead-lettered; see ADR-027 for the decision and local evidence.
- Kafka is not included in the Java runtime. Adding a second consumer without an explicit routing contract could process the same order through both brokers.

### Database

- **PostgreSQL** (primary, local dev): Same `FlashSaleDb` instance, `payment_records` table owned by Java service.
- **Oracle** (production profile): `application-oracle.properties` with `ojdbc8` driver, `OracleDialect`, activatable via `--spring.profiles.active=oracle`.

### Cache

- Redis (already in docker-compose). Cache Aside pattern for payment lookups (TTL-based, invalidation on status change).

### Security

- Spring Security with JWT (jjwt 0.12.3, HS256).
- In-memory users: CUSTOMER, STAFF, ADMIN roles.
- BCrypt password encoding.
- RBAC on endpoints: CUSTOMER can read own payments; STAFF/ADMIN can list all.

### Deployment

- Docker multi-stage build (Eclipse Temurin 17 JRE).
- Added to main `docker-compose.yml` as `payment-service-java` on port 8081.
- CI/CD via GitHub Actions: build, unit tests, integration tests (Testcontainers), Docker build + Trivy scan.

## Alternatives Considered

| Alternative | Pros | Cons | Rejected Because |
|-------------|------|------|------------------|
| Rewrite .NET Payment Service in Java | Single implementation | Breaks existing .NET CI/CD, loses .NET evidence | Would remove working .NET service; not additive |
| New repo for Java service | Clean separation | Harder to demonstrate integration with existing infra | CV wants one cohesive platform |
| Kafka instead of RabbitMQ | JD mentions Kafka | No active Kafka integration contract; adding it would create a second delivery path | Keep one tested runtime broker; document Kafka as not demonstrated |
| Separate database for Java | Clean isolation | Cross-service complexity; JD wants shared DB evidence | Shared DB with table separation is standard microservice pattern |
| gRPC instead of REST | JD mentions gRPC | Protobuf tooling overhead, team REST maturity | REST sufficient for current scope; gRPC documented as planned |

## Consequences

### Positive
- Direct Java Spring Boot evidence for interviews (REST, JPA, Security, Messaging, Cache, Docker, CI/CD)
- Same infrastructure stack as existing services (PostgreSQL, Redis, RabbitMQ)
- Demonstrates polyglot microservice architecture
- Idempotency, transaction, DLQ patterns implemented in Java
- Oracle profile is configured but requires separate runtime evidence; gRPC remains a documented gap in docs/adr/grpc-support-note.md

### Negative
- Two payment services (.NET + Java) may cause confusion
- Java service shares `payment_records` table — coupling via schema
- Additional Docker image and CI pipeline to maintain

### Risks
- Port conflicts (Java on 8081, .NET on 5002/8080)
- JWT key mismatch between services (mitigated: separate signing keys)
- Table ownership ambiguity (mitigated: ADR documents Java owns only `payment_records`)

## Related

- ADR-013: Authorization model (roles CUSTOMER/STAFF/ADMIN)
- ADR-017: Bounded contexts
- Dockerfile.payment (.NET service)
- Dockerfile (Java service, new)
