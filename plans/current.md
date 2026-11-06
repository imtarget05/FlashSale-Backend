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

- **G2.5 Pre-G3 Correctness Gate**: Complete.
  - Decoupled webhook inbox from downstream consumer execution via `ConsumerProcessingState`.
  - Same `EventId` with same payload returns 200 Duplicate; same `EventId` with conflicting payload throws `EventPayloadConflictException` (mapped to HTTP 409 Conflict).
  - Consumer commits business effects (`GoodsReceipt` domain models) and processing state in a single PostgreSQL transaction before issuing `BasicAckAsync`.

- **G3 Enterprise Three-Way Reconciliation Engine**: Complete.
  - Domain models: `PurchaseOrder`, `GoodsReceipt`, `SupplierInvoice`, `MappingRule`, `ReconciliationBatch`, `ReconciliationResult`, `ReconciliationAllocation`, `Discrepancy`, `ReviewDecision`, `AuditEvent`.
  - Deterministic 3-Way Matching Engine:
    - Line-by-line matching with SKU aliases and UOM conversions (e.g. 1 BOX = 10 PCS).
    - Cumulative non-overlapping receipt allocation tracking (`AllocatedQuantity` on `GoodsReceiptLine`).
    - Configurable tolerance policies for price ($\pm\%$) and quantity ($\pm\%$ or absolute).
    - Single-currency validation (VND vs VND) and separation of tax/discounts from unit price matching.
    - Reference Scenario (PO-1001, GR1, GR2 $\rightarrow$ Discrepancy $\rightarrow$ GR3 $\rightarrow$ Rerun $\rightarrow$ Matched) fully validated.
    - Concurrency-safe atomic conditional update (`TryAllocateQuantityAsync`) at database row level preventing double-spend / over-invoicing.
    - Discrepancy management and human review workflow with RBAC (`FinanceReviewer`, `Operator`, `Admin`) and audit logging.

- **G3.5 Correctness Hardening**: Complete.
  - Contract compatibility for both `purchaseOrderNumber` and `purchaseOrderNo` (Postel's Law).
  - Allocation release on rejection (`ReleaseAllocationQuantityAsync`) and conditional commitment on variance approval.
  - Guarded against re-reconciling already `Matched` or `ApprovedWithVariance` invoices.
  - Supplier identity and currency strict matching.

- **G4 Enterprise React + TypeScript Operations Dashboard**: Complete.
  - Initialized `src/OpsBridge.Web` (React 19 + TypeScript + Vite + Tailwind CSS v4 + TanStack Query v5 + React Router v7).
  - Zero mock buttons: All 8 screens connected to real backend APIs in `OpsBridge.Api`.
  - Multi-persona auth with instant role switching (`FinanceReviewer`, `Operator`, `Admin`, `Auditor`).
  - 8 operational screens:
    1. Operations Overview
    2. Purchase Orders
    3. Goods Receipts
    4. Supplier Invoices
    5. 3-Way Matching Workspace
    6. Discrepancy Investigation & Review Panel
    7. Integration Operations & DLQ
    8. Audit Timeline
  - Full Test Suite:
    - `OpsBridge.UnitTests`: 33/33 Passed
    - `OpsBridge.ContractTests`: 7/7 Passed
    - `OpsBridge.IntegrationTests`: 17/17 Passed (real PostgreSQL 15 & RabbitMQ 3 Testcontainers)
    - Total: **57/57 OpsBridge tests passing** with 0 failures.
    - Frontend bundle: `npm run build` succeeds cleanly in 258ms with 0 type errors.

---

## 2. Next Engineering Objective: Phase G5 — Observability, SRE & Production Readiness

1. OpenTelemetry distributed tracing across HTTP webhook $\rightarrow$ Outbox $\rightarrow$ RabbitMQ $\rightarrow$ Consumer $\rightarrow$ Reconciliation Engine.
2. Prometheus metrics & Grafana operational dashboards:
   - Webhook ingestion rate & latency.
   - Message processing latency & consumer lag.
   - Discrepancy rates & auto-match percentage.
   - DLQ size & replay success rate.
3. Health check probes (`/healthz/liveness`, `/healthz/readiness`) with deep checks on PostgreSQL & RabbitMQ.
4. Structured logging with correlation IDs (`traceparent`, `CorrelationId`) end-to-end.
