# Current Plan: OpsBridge Platform — Enterprise Integration, Reconciliation & Operations

**Current Project**: OpsBridge (Enterprise Integration, Reconciliation & Operations Platform)  
**Coordinating Project**: FactoryFlow (MiniERP, owned concurrently by Codex)  
**Integration Contract**: `GoodsReceiptConfirmed v1`

---

## 1. Current State

- **G0 Baseline Audit**: Complete. Reused backend foundations from `FlashSale-Backend` (Durable Inbox/Outbox patterns, Clean Architecture, EF Core PostgreSQL schema runner, Testcontainers xUnit harness). Verified 172/172 unit tests and 62/62 integration tests passing.
- **G1 Foundation & Webhook Ingestion**: Complete.
  - Implemented `OpsBridge.slnx` with Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`, `UnitTests`, `ContractTests`, `IntegrationTests`).
  - Defined `GoodsReceiptConfirmed v1` JSON Schema and sample fixtures in `docs/contracts/` and `tests/fixtures/`.
  - Implemented `HmacWebhookVerifier` supporting Svix standard headers (`svix-id`, `svix-timestamp`, `svix-signature`) and OpsBridge custom headers with 300s anti-replay protection and constant-time comparison.
  - Implemented `ReceiveWebhookUseCase` with durable deduplication via `IInboxRepository` and PostgreSQL persistence via `IIngestedEventRepository`.
  - Implemented `MockFactoryFlowClient` for local development and contract testing.
  - Test Suite:
    - `OpsBridge.UnitTests`: 12/12 Passed (144 ms)
    - `OpsBridge.ContractTests`: 6/6 Passed (95 ms)
    - `OpsBridge.IntegrationTests`: 4/4 Passed (711 ms, real PostgreSQL Testcontainer)
    - Total: 22/22 OpsBridge tests passing, zero regression on baseline.

- **G1.5 Correctness & Security Hardening**: Complete.
  - Atomic persistence via `IUnitOfWork` / `EfUnitOfWork` (Inbox, IngestedEvent, OutboxMessage in a single PostgreSQL transaction).
  - Concurrent idempotency handling (PostgreSQL `23505` unique violation catches concurrent webhooks gracefully, returns 200 Duplicate).
  - Official Svix webhook verification (`whsec_` base64 decoding, canonical `${id}.${timestamp}.${body}`, space-separated candidate signatures, constant-time compare).
  - Masked connector secrets in API responses; `MockFactoryFlowClient` guarded against running in `Production`.
- **G2 Reliable Event Distribution & Worker Processing**: Complete.
  - PostgreSQL Transactional Outbox + `OutboxPublisherHostedService` with RabbitMQ Publisher Confirms (`publisherConfirmationsEnabled: true`).
  - Durable RabbitMQ topology: topic exchange `opsbridge.events.topic`, queue `opsbridge.goods-receipts.v1`, DLX `opsbridge.events.dlq.exchange`, DLQ `opsbridge.events.dlq`.
  - Idempotent consumer `GoodsReceiptConsumerHost` with manual acknowledgements (`autoAck: false`), retry with exponential backoff & jitter, DLQ routing after 3 retries.
  - Authorized manual DLQ replay endpoint `POST /api/v1/events/replay/{id}` with audit history in `DeadLetterEvents`.
  - 8 Critical Failure Drills verified via real Testcontainers (PostgreSQL 15 and RabbitMQ 3):
    1. RabbitMQ unavailable when webhook arrives (retained in outbox, 202 Accepted).
    2. API crashes immediately after DB commit (outbox publisher recovers).
    3. Publisher crash before confirm recorded (re-publish deduped by consumer).
    4. Worker crash after DB commit before ACK (broker redelivers, inbox dedupes).
    5. Concurrent duplicate messages (inbox unique index prevents duplicate processing).
    6. Poison message repeatedly fails (routed to DLQ after 3 retries).
    7. Manual replay requested twice (safe idempotent replay).
    8. Unauthorized user replay attempt (RBAC requires Operator/Admin).
  - Full Test Suite:
    - `OpsBridge.UnitTests`: 12/12 Passed
    - `OpsBridge.ContractTests`: 6/6 Passed
    - `OpsBridge.IntegrationTests`: 10/10 Passed (real PostgreSQL & RabbitMQ Testcontainers)
    - Total: 28/28 OpsBridge tests passing with 0 failures.

---

## 2. Next Engineering Objective: Phase G3 — Three-Way Reconciliation Engine

Implement the core business engine: Purchase Order $\rightarrow$ Goods Receipt $\rightarrow$ Supplier Invoice:
- Models for Purchase Order (PO) and Supplier Invoice with line items, quantities, unit prices, UOM.
- Three-way matching engine supporting:
  - Partial deliveries (1 PO to $N$ Goods Receipts).
  - Multiple invoices per PO.
  - Configurable tolerance rules (quantity $\pm\%$ or absolute, price $\pm\%$).
  - Unit of Measure (UOM) conversions.
  - Discrepancy detection (unmatched line, quantity mismatch, price mismatch, duplicate invoice).
  - Reconciliation batch processing and human review/approval workflow.
