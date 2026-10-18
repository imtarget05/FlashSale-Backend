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

---

## 2. Next Engineering Objective: Phase G2

- **RabbitMQ Worker & Event Pipeline**:
  - Configure RabbitMQ topology: exchange `opsbridge.events.inbound`, queue `opsbridge.goods-receipts.v1`, dead-letter exchange `opsbridge.events.dlq`.
  - Implement asynchronous background worker `GoodsReceiptConsumerHost` consuming from RabbitMQ.
  - Implement exponential backoff retry with jitter and dead-letter capture for poisoned payloads.
  - Implement manual replay endpoint `POST /api/v1/events/replay/{eventId}`.
