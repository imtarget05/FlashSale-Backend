# FlashSale Backend — Java-only (payment service)

| Badge | Value |
|---|---|
| ![Java 17](https://img.shields.io/badge/Java-17-007396?logo=java) | Runtime |
| ![Spring Boot 3.2](https://img.shields.io/badge/Spring%20Boot-3.2.5-6DB33F?logo=spring-boot) | Framework |
| ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15-4169E1?logo=postgresql) | Database |
| ![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis) | Cache / inventory mirror |
| ![RabbitMQ](https://img.shields.io/badge/RabbitMQ-Messaging-FF6600?logo=rabbitmq) | Messaging |
| ![Kafka](https://img.shields.io/badge/Kafka-Event%20stream-231F2D?logo=apache-kafka) | Event stream |
| ![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker) | Containerization |

## Overview

This repository contains a single Java/Spring Boot microservice: **payment-service** (`src/payment-service-java`). It handles payment processing, idempotent order checkout, JWT auth, and publishes order events to Kafka/RabbitMQ. PostgreSQL is the authoritative store; Redis mirrors inventory stock.

The previous C#/.NET solution and its projects (`FlashSale.Domain`, `Order.Api`, `HealthProbe`, OpsBridge, `AzureFlashSale.slnx`) have been removed to keep one stack: **Java-only**.

## Scope and product focus

The primary product is the Java backend. The `frontend/` folder is a thin demo shell for preview testing and smoke checks only; it is not the core business surface of the repo. Backend logic, payment idempotency, security, and API contracts remain the canonical source of truth.

## Module layout

```
src/payment-service-java/
├── pom.xml                  # Spring Boot 3.2.5, Java 17
├── Dockerfile               # multi-stage (maven build → temurin JRE)
├── docker/init-schema.sql   # PostgreSQL schema (payment_records, idempotency keys)
└── src/main/java/com/flashsale/payment/
    ├── controller/          # PaymentController, HealthController
    ├── service/             # PaymentService
    ├── repository/ + entity/# PaymentRepository, Payment (JPA)
    ├── dto/                 # PaymentRecordDto, ProcessPaymentRequest, LoginRequest, AuthResponse
    ├── exception/           # GlobalExceptionHandler, PaymentNotFoundException, IdempotencyConflictException
    ├── security/            # SecurityConfig, JwtAuthFilter, JwtUtil, AuthService, JwtStartupValidator
    ├── config/              # KafkaConfig, RabbitConfig, CacheConfig
    └── event/               # KafkaOrderEventConsumer, KafkaPaymentEventPublisher, OrderEventDTO
```

## Run locally

```bash
# 1) Start infra (PostgreSQL, Redis, RabbitMQ)
docker compose up -d postgres redis rabbitmq

# 2) Build and run the payment service
cd src/payment-service-java
mvn -q -DskipTests package
java -jar target/*.jar

# 3) Health
curl http://localhost:8081/healthz
```

The compose service `payment-service-java` maps port `8081:8080` and waits on `postgres`, `redis`, `rabbitmq` being healthy.

## Tests

Unit tests (no Docker required):

```bash
cd src/payment-service-java
mvn -q test -Dtest='!*Integration*'
```

Integration tests use Testcontainers (PostgreSQL, RabbitMQ, Kafka) and need a running Docker daemon:

```bash
mvn -q test
```

Recorded local result: **16/18 unit tests pass**; the 2 integration-test errors are `Testcontainers` unable to reach a Docker daemon in this environment — not code failures.

## API surface (key endpoints)

| Endpoint | Method | Purpose |
|---|---|---|
| `/healthz` | GET | Liveness / readiness probe |
| `/api/payments` | POST | Process a payment (idempotent) |
| `/api/payments/{id}` | GET | Get payment record |
| `/api/auth/login` | POST | JWT login |
| `/api/orders/events` | POST | Publish/consume order events (Kafka/RabbitMQ) |

Full OpenAPI/Swagger UI is available at `/swagger-ui.html` when running.

## Schema

`docker/init-schema.sql` creates `payment_records` with idempotency-key uniqueness and `outbox`-style tracking. Apply it via the Postgres container entrypoint (compose mounts it to `/docker-entrypoint-initdb.d/`).

## Notes

- No real PSP contract, credential, or behaviour is exercised anywhere; payment outcomes are simulated for local/demo runs.
- JWT signing key must be supplied via `JWT_SIGNING_KEY` / `APP_JWT_SECRET` (never committed).
- Kubernetes / AKS / Azure artifacts were removed with the C# solution; this repo targets local `docker compose` only.
