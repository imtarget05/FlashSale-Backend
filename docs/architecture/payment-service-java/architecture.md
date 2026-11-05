# Payment Service Java — Architecture

## Overview

The Java Spring Boot payment service is a bounded context within the FlashSale
microservices platform. It owns payment processing, idempotency, and payment
event publishing. It shares PostgreSQL, Redis, and RabbitMQ with the existing
.NET services but owns only its own `payment_records` table.

## Component Diagram

```mermaid
flowchart LR
    Client([Client / Mobile / Web])
    API[Order API .NET]
    Java[Payment Service Java<br/>Spring Boot 3.2]
    PG[(PostgreSQL<br/>FlashSaleDb)]
    Redis[(Redis<br/>Cache)]
    RMQ[(RabbitMQ<br/>Message Broker)]

    Client -->|HTTP /api/payments| Java
    Client -->|HTTP /api/orders| API
    API -->|HTTP payment calls| Java
    Java -->|Read/Write| PG
    Java -->|Cache Aside| Redis
    Java -->|Publish payment.completed| RMQ
    Java -->|Publish payment.failed| RMQ
    Java -->|Consume order.created| RMQ
    RMQ -->|DLQ| DLQ[(Dead Letter Queue)]

    subgraph "Java Payment Service Bounded Context"
        direction TB
        Controller[REST Controller]
        Service[Payment Service<br/>@Transactional]
        Repository[JPA Repository]
        Security[Spring Security<br/>JWT + RBAC]
        Publisher[RabbitTemplate<br/>Event Publisher]
        Consumer[RabbitListener<br/>Order Event Consumer]
        Cache[RedisTemplate<br/>Cache Aside]

        Controller --> Service
        Service --> Repository
        Service --> Publisher
        Service --> Consumer
        Service --> Cache
        Security -.-> Controller
    end
```

## Service Boundaries

| Boundary | Responsibility |
|----------|---------------|
| **Payment Service Java** | Payment processing, idempotency, event publishing, JWT auth |
| **Order API (.NET)** | Order lifecycle, calls payment service via HTTP |
| **Order Worker (.NET)** | Async order processing, RabbitMQ consumer |

## Data Flow — Payment Processing

```
Client → POST /api/payments → Java Controller
  → Java Service (@Transactional)
    → Check idempotency key in PostgreSQL
    → If exists: return stored result
    → If new: simulate payment (cent-based rules)
    → Save to PostgreSQL (payment_records table)
    → Return result (200 OK or 504 Gateway Timeout)
    → Publish payment.completed / payment.failed to RabbitMQ
```

## Data Flow — Event Consumption

```
RabbitMQ order.created → Java RabbitListener
  → Log event (idempotency handled at payment level)
  → No direct payment mutation from events
```

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Language | Java | 17 |
| Framework | Spring Boot | 3.2.5 |
| Data Access | Spring Data JPA | — |
| Database | PostgreSQL | 15 |
| Cache | Redis | 7 |
| Messaging | RabbitMQ | 3 |
| Security | Spring Security + JWT (jjwt) | 6 / 0.12.3 |
| API Docs | OpenAPI / Swagger (springdoc) | 2.2.0 |
| Testing | JUnit 5, Mockito, Testcontainers | — |
| Container | Docker | multi-stage |
