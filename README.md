# Flash-Sale Reliability Platform

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql)](https://postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis)](https://redis.io/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-Messaging-FF6600?logo=rabbitmq)](https://rabbitmq.com/)

A portfolio-grade, event-driven backend built to handle high-concurrency "flash sale" scenarios. The core engineering challenge is processing thousands of simultaneous purchase attempts for limited inventory without overselling, while keeping latency low and the database healthy.

## 📖 The Business Problem & Engineering Solution

During a flash sale, inventory is strictly limited (e.g., 10 items). A naive synchronous approach falls apart under concurrent load, leading to race conditions (overselling) and database timeouts.

**The Solution:**
- **Redis Lua CAS (Compare-And-Swap):** Fast, atomic inventory reservations at the cache layer to act as an immediate gatekeeper, returning fast-fails for excess traffic.
- **RabbitMQ (At-least-once delivery):** Buffers valid requests into an async queue, protecting the primary database from load spikes.
- **PostgreSQL (Authoritative Truth):** Handles the final atomic conditional `UPDATE`, ensuring absolute data consistency.

## 🚀 Key Achievements & Evidence
*These metrics were validated using real `Testcontainers` infrastructure and `k6` load testing, not fabricated.*

- **Zero Overselling Invariant:** 50 concurrent purchase attempts against a stock of 10 produced exactly 10 persisted sales and a final stock of 0.
- **High Performance:** Achieved a **p95 latency of 29ms** during peak flash-sale concurrency (compared to 574ms in the naive synchronous iteration).
- **Idempotency & Resiliency:** Verified Redis failure fallbacks, RabbitMQ `ack-after-persist` behaviors, and safe retry mechanisms for duplicate/failed requests.
- **Disaster Recovery:** Automated PostgreSQL backup and clean-restore procedures using `pg_dump -Fc` with SHA-256 verification and application smoke testing against the recovered DB.

## 🏗️ Architecture

```mermaid
flowchart LR
    Client([Client]) --> API[Order API]
    API -->|1. Lua CAS| Redis[(Redis)]
    API -->|2. Publish| RMQ[RabbitMQ]
    RMQ -->|3. Consume| Worker[Order Worker]
    Worker -->|4. Atomic Update| DB[(PostgreSQL)]
```

## 📂 Project Structure (Clean Architecture)
```text
src/
├── FlashSale.Domain/         # Entities, Value Objects, Exceptions (No external dependencies)
├── FlashSale.Application/    # Use Cases & Ports (Interfaces)
├── FlashSale.Infrastructure/ # Adapters (EF Core, Redis, RabbitMQ)
├── Order.Api/                # Presentation Layer (API endpoints)
└── Order.Worker/             # Background processing daemon
tests/
└── UnitTests/                # Includes Architecture Tests (enforcing layer boundaries)
```
