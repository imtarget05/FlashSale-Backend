# Flash-Sale Reliability Platform

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql)](https://postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis)](https://redis.io/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-Messaging-FF6600?logo=rabbitmq)](https://rabbitmq.com/)

A portfolio-grade, event-driven backend built to handle high-concurrency "flash sale" scenarios. The core engineering challenge is processing thousands of simultaneous purchase attempts for limited inventory without overselling, while keeping latency low and the database healthy.

> **Role:** Backend Engineer (C# / .NET 10, PostgreSQL, Redis, RabbitMQ).
> **One-liner for HR:** I built an order system where 50 buyers race for 10 items — exactly 10 sales succeed, 40 get honest fast-fails, p95 29ms. All numbers from real Testcontainers + k6 runs, not slides.
> **For Tech Lead:** Clean Architecture (Domain <- Application <- Infrastructure <- Api/Worker) with dependency-direction guards in `tests/UnitTests/ArchitectureTests.cs`. Evidence in `docs/benchmarks/` + `docs/adr/001-005`.

## 🧠 Skills Demonstrated (scan in 30s)

| Area | What I did | Where to verify |
|---|---|---|
| Concurrency control | Redis Lua CAS gatekeeper + PG atomic conditional `UPDATE` | `docs/adr/002-003-*`, `docs/benchmarks/phase2-005-*` |
| Async / resilience | RabbitMQ at-least-once, ack-after-persist, DLQ, idempotency keys | `docs/adr/004-005-*`, `src/Order.Worker/` |
| Data integrity | Zero-oversell invariant: 50 req / stock 10 → 10 sales, stock 0 | `docs/benchmarks/phase2-oversell-experiment.md` |
| Performance | p95 29ms tiered vs 574ms naive sync; pool cap 80/100 to keep operator headroom | `docs/benchmarks/phase4-highload-experiment.md`, `phase5-tiered-experiment.md` |
| Operability | pg_dump -Fc backup + SHA-256 + restore smoke test | `docs/adr/008-009-*` |
| Architecture discipline | Port + adapter for every infra addition; thin `Program.cs` | `src/FlashSale.Application/`, `tests/UnitTests/ArchitectureTests.cs` |

## 🔍 Problem → Decision → Tradeoff (how I think)

1. **Naive sync oversells under race** → reproduced first (`docs/benchmarks/phase2-*`), then fixed with atomic conditional UPDATE. Tradeoff: hot-row lock queues (p95 574ms at 200 conc.) — accepted as correctness baseline.
2. **90% wasted DB round-trips just to say "no"** → Redis Lua CAS pre-filter (single RTT, atomic). Tradeoff: cache/DB divergence risk — mitigated by PG as authoritative truth + Redis mirror on cancel path.
3. **Traffic spikes kill DB** → RabbitMQ buffer + worker persist + ack-after-persist. Tradeoff: at-least-once duplicates — mitigated by idempotency keys + guarded UPDATEs.
4. **Pool saturation locks out operators** → cap API pool 80 < server 100. Small fix, big SRE lesson: always leave headroom.

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

## 🤖 Automation Platform (Business Automation Spec)

Event-driven automation layered on the same order path — no core transactional
logic was moved, and the pre-automation HTTP contract is unchanged.

```mermaid
flowchart TD
    O[Order accepted] -->|correlationId| E1{{order.created}}
    O --> E2{{inventory.reserved}}
    E1 --> W[Automation worker<br/>audit-first]
    E2 --> W
    W --> R[(automation_runs)]
    P[Payment window] -->|due!| S[Payment timeout scan]
    S -->|inside grace| N{{reminder + audit}}
    S -->|past grace| C[Cancel order<br/>+ release stock]
    C --> E3{{payment.expired}}
    C --> E4{{order.cancelled}}
    C --> E5{{inventory.released}}
    PAY[POST /api/orders/:key/pay] -->|completed| E6{{payment.completed}}
    PAY --> E6b{{order.confirmed}}
    PAY -->|failed| E7{{payment.failed}}
```

### Implemented workflows

| Workflow | Trigger | Condition | Action | Failure path | Retry | Audit | Human approval |
|---|---|---|---|---|---|---|---|
| Order events (spec §4) | order accept + worker persist | always | publish `order.created` / `inventory.reserved` | best-effort publish, order stays valid | queue retry ×4 → DLQ | `automation_runs` (worker) | no |
| Payment timeout (spec §5) | timer (`PaymentTimeoutHostedService`) or `POST /internal/automation/payment-timeout-scan` | `PendingPayment` & past due → remind; past due+grace → cancel | reminder counter; cancel + DB stock release + Redis mirror | guarded UPDATE ⇒ no-op on replay; publish best-effort | bounded scan batch (200) | 1 run per scan (`Success`/`Failed`) | no |
| Payment recording (spec §4) | `POST /api/orders/{key}/pay` | order is `PendingPayment` | `completed` ⇒ `Confirmed`; `failed` ⇒ stays pending | second call ⇒ 409 (status guard) | n/a (idempotent) | 1 run per call | simulated gateway — no real processor |
| Low-stock alert (spec §6) | timer (`LowStockScanHostedService`) or `POST /internal/automation/low-stock-scan` | `AvailableStock <= ReorderThreshold` (inclusive) | create deduplicated Open `StockAlerts` row + publish `inventory.low_stock` | rule re-checked in-process; duplicate insert rejected by partial unique index ⇒ reported as deduped | periodic rescan | 1 run per scan (`InventoryAutomation`) | no — advisory only, stock numbers never come from AI |
| Daily report (spec §7) | timer (`DailyReportHostedService`, `RunAtHourUtc`) or `POST /internal/automation/daily-report` | every report day (UTC window) | aggregate orders/revenue/failed payments/cancellations/top products/low stock from PostgreSQL → persist one `DailyReports` row (rerun upserts) | run recorded `Failed`, row untouched | rerun recomputes the day | 1 run per generation (`DailyReport`) | no — all numbers are DB aggregates; AI summary NOT implemented (PLANNED) |
| Automation dashboard (spec §14) | `GET /internal/automation/summary` + `GET /internal/automation/alerts` | on demand | runs today/success/failed/retrying/manual-review/avg duration/top failing workflow from `AutomationRuns`; open `StockAlerts` list | empty day → zeroed summary | n/a (read) | reads `AutomationRuns` | no |

### Configuration (never hard-coded — spec §5)

| Key | Default | Meaning |
|---|---|---|
| `Automation:Payment:TimeoutMinutes` | 15 | payment window per order |
| `Automation:Payment:GracePeriodMinutes` | 15 | extra time before cancellation |
| `Automation:Payment:MaxPaymentReminders` | 3 | reminder cap per order |
| `Automation:Payment:ScanIntervalSeconds` | 60 | timer scan cadence (worker) |
| `Automation:Inventory:DefaultReorderThreshold` | 5 | fallback reorder point (product value wins when > 0) |
| `Automation:Inventory:ScanIntervalSeconds` | 60 | low-stock scan cadence (worker) |
| `Automation:Reporting:RunAtHourUtc` | 0 | UTC hour the daily report fires |
| `Automation:Reporting:ScanIntervalSeconds` | 300 | how often the scheduler checks the hour |
| `Automation:Reporting:TopProductCount` | 5 | rows in the top-products ranking |

### Demo — scenario A (timeout → remind → cancel → release → audit)

```bash
# 1) place an order, then walk it through the payment lifecycle
curl -X POST localhost:5099/api/orders -H 'Idempotency-Key: demo-1' \
     -H 'Content-Type: application/json' -d '{"productId":1,"quantity":1}'
curl -X POST localhost:5099/api/orders/demo-1/pay \
     -H 'Content-Type: application/json' -d '{"outcome":"completed"}'   # → confirmed
curl -X POST localhost:5099/api/orders/demo-1/pay \
     -H 'Content-Type: application/json' -d '{"outcome":"completed"}'   # → 409 (guarded)

# 2) run the abandoned-payment scan (same use case the timer runs)
curl -X POST localhost:5099/internal/automation/payment-timeout-scan
#    grace elapsed ⇒ {"scanned":N,"reminded":0,"cancelled":N}, stock restored

# 3) audit trail
docker exec <postgres> psql -U postgres -d FlashSaleDb -c \
  'SELECT "WorkflowName","TriggerType","Status","ResultSummary" FROM "AutomationRuns" ORDER BY "Id" DESC LIMIT 5;'
```

*Verified locally: 21/21 live smoke checks (`/tmp`-style script), 82 unit +
24 integration tests green, and the v1.0 auth/order smoke (48 checks) still
passes — the legacy `GET /api/orders/{key}` wording (`processing|completed`)
was deliberately left untouched.*

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
