# Payment Service Java — Deployment Guide

## Prerequisites

- Docker & Docker Compose
- Java 17 + Maven 3.9+ (for local development)
- PostgreSQL 15, Redis 7, RabbitMQ 3 (provided by docker-compose)

## Local Development

### 1. Clone and configure

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend
cp src/payment-service-java/.env.example src/payment-service-java/.env
# Set JWT_SIGNING_KEY (32+ bytes) and DEMO_AUTH_PASSWORD (16+ characters).
# These are local-only values; keep the .env file untracked.
set -a
. src/payment-service-java/.env
set +a
```

### 2. Start infrastructure

```bash
docker compose -p flashsale-backend up -d postgres redis rabbitmq
```

### 3. Run the Java service

```bash
cd src/payment-service-java
export SPRING_PROFILES_ACTIVE=demo
mvn spring-boot:run
```

The service starts on `http://localhost:8080`. Mock users are available only
with the explicit `demo` profile; the default and production profiles have no
configured identity provider and reject demo logins.

## Docker Deployment

### Using the main docker-compose

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend
docker compose -p flashsale-backend up -d
```

This starts all services including `payment-service-java` on port 8081.

### Using the standalone compose

```bash
cd src/payment-service-java
docker compose -f docker-compose.payment-java.yml up -d
```

### Build the image

```bash
cd src/payment-service-java
docker build -t flashsale/payment-service-java:1.0.0 .
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `POSTGRES_HOST` | `postgres` | PostgreSQL host |
| `POSTGRES_PORT` | `5432` | PostgreSQL port |
| `POSTGRES_DB` | `FlashSaleDb` | Database name |
| `POSTGRES_USER` | `postgres` | Database user |
| `POSTGRES_PASSWORD` | `postgres` | Database password |
| `RABBITMQ_HOST` | `rabbitmq` | RabbitMQ host |
| `RABBITMQ_PORT` | `5672` | RabbitMQ port |
| `RABBITMQ_USER` | `guest` | RabbitMQ user |
| `RABBITMQ_PASSWORD` | `guest` | RabbitMQ password |
| `REDIS_HOST` | `redis` | Redis host |
| `REDIS_PORT` | `6379` | Redis port |
| `JWT_SIGNING_KEY` | (required) | JWT signing key (min 32 bytes) |
| `DEMO_AUTH_PASSWORD` | (required in `demo`) | Password shared by synthetic local demo users (min 16 characters) |
| `JAVA_PAYMENT_PORT` | `8081` | Host port for Java service |

## Health Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Service status |
| `GET /health/live` | Liveness probe |
| `GET /healthz` | Liveness probe (used by Docker healthcheck) |
| `GET /actuator/health/readiness` | Readiness probe including database, Redis, and RabbitMQ |

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/payments` | Public | Process payment |
| `GET` | `/api/payments/{idempotencyKey}` | CUSTOMER/STAFF/ADMIN | Get payment |
| `GET` | `/api/payments` | STAFF/ADMIN | List payments (paginated) |
| `POST` | `/api/auth/login` | Public | Get JWT token |
| `GET` | `/health` | Public | Health check |
| `GET` | `/healthz` | Public | Liveness probe |

## CI/CD

GitHub Actions workflow: `.github/workflows/payment-service-java-ci.yml`

Jobs:
1. `build-and-unit` — Maven build + unit tests
2. `integration-tests` — Testcontainers integration tests
3. `docker-build-scan` — Docker build + Trivy image scan

## Rollback

The Java service is stateless (all state in PostgreSQL). Rollback by deploying the previous Docker image tag. Database schema is not modified by the Java service (DDL validation only).

## Monitoring

- Health endpoints available for load balancer probes.
- Logs via stdout (Docker `docker compose logs payment-service-java`).
- No APM/observability configured in this service (see AKS-SRE-Platform for platform-level monitoring).
