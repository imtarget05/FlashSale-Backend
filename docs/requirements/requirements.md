# Business & Technical Requirements — Commerce + OpsBridge

> **Scope**: OpsBridge bounded context (reconciliation, DLQ replay, RBAC, outbox/inbox) + FlashSale core (no-oversell, saga checkout, auth).
> **Commit**: `1ecd302` (branch `feature/g1-opsbridge-foundation`)
> **Generated from**: E1/E2/E3 traceability analysis (62 requirements across 5 domains)

---

## Domain 1: No-Oversell & Concurrent Ordering (FlashSale Core)

| Req ID | Business Rule | Acceptance Criteria | Priority |
|--------|---------------|---------------------|----------|
| **R1.1** | **Inventory Conservation**: Total sold ≤ initial stock. Stock never negative. | Given initial stock=10, 50 concurrent requests → final_stock≥0 AND sold≤10 AND initial=final+sold | **P0** |
| **R1.2** | **Idempotent Order Processing**: Same idempotency key → exactly 1 order persisted. | Given key `dup-key`, 5 calls → 1 order, stock decremented once | **P0** |
| **R1.3** | **Fast Reservation Tier**: Redis Lua CAS rejects hopeless buyers before DB; duplicate keys deduped at reservation. | Stock=10 in Redis, 50 requests → returns Reserved/SoldOut/Duplicate without DB round-trip | **P0** |
| **R1.4** | **Backpressure Shedding**: Queue full → reservation released; outcome distinct from Accepted/OutOfStock. | Queue=5000, 6000 submissions → 5000 accepted, 1000 `SystemBusy`, reservation released | **P1** |
| **R1.5** | **Graceful Redis Degradation**: Redis unavailable → synchronous DB path without overselling. | RedisException → `Unavailable` → `OrderProcessor.ProcessAsync` (atomic DB) | **P1** |
| **R1.6** | **Stock Drift Detection**: Redis success + DB 0 rows → `StockDriftException` → DLQ → runbook. | Drift detected, no duplicate order, state converges via `POST /internal/resync-stock` | **P1** |
| **R1.7** | **Saga Idempotency**: Duplicate idempotency key → existing saga result; no re-execution. | Key `saga-idempotent-005` → same `SagaId`/`OrderId`; Payment=1, OrderRepo=1 | **P1** |

---

## Domain 2: Transactional Outbox/Inbox & Idempotency (OpsBridge)

| Req ID | Business Rule | Acceptance Criteria | Priority |
|--------|---------------|---------------------|----------|
| **R2.1** | **Atomic Webhook Ingestion**: Single PostgreSQL transaction for IngestedEvent + InboxMessage + OutboxMessage. | HTTP 202; exactly 1 row each table with matching EventId; all committed or none | **P0** |
| **R2.2** | **Webhook Deduplication**: Duplicate EventId → 200 Duplicate; no business effect. | 10 concurrent identical → 1 accepted + 9 duplicate; 1 row each table | **P0** |
| **R2.3** | **Payload Conflict Detection**: Same EventId, different payload → `EventPayloadConflictException`. | EventId X with payload A → processed; same EventId with payload B → conflict, no mutation | **P0** |
| **R2.4** | **Publisher Confirms + Backoff**: Outbox published only on broker ACK; failed = `Failed` status + exponential backoff. | `Published` only on confirm; failure → `RetryCount`++, backoff 2s/4s/8s/16s/32s/60s cap | **P0** |
| **R2.5** | **Consumer Idempotency via Inbox**: Redelivered message → BasicAck immediately, no state change. | 1st delivery → Completed; 2nd redelivery → ConsumerProcessingStates count = 1 | **P0** |
| **R2.6** | **Poison Message → DLQ**: Max retries exhausted → `DeadLetteredAt` set; `DeadLetterEvents` row with Reason. | 5 attempts → DeadLetteredAt, DLQ entry, Reason recorded | **P0** |
| **R2.7** | **Idempotent Manual Replay**: Replay once → `ReplayQueued`; twice → `AlreadyReplayed`; 12 concurrent → 1 `ReplayQueued` + 11 `AlreadyReplayed`. | Exactly 1 OutboxMessage; DLQ `IsReplayed=true`, `ReplayedBy` set | **P0** |
| **R2.8** | **Broker Down Resilience**: RabbitMQ down → HTTP 202; Outbox `Pending`; no fake ACK. | Webhook 202, outbox pending, no data loss | **P1** |
| **R2.9** | **Crash Recovery**: API crash after DB commit → OutboxPublisher picks up on restart. | Pending message → publisher polls, confirms, marks `Published` | **P1** |
| **R2.10** | **Inbox Deduplication at Consumer**: 10 concurrent identical → 1 InboxMessage, 1 ConsumerProcessingState. | Composite key MessageId+ConsumerGroup; DbUpdateException caught for idempotent duplicate | **P1** |

---

## Domain 3: DLQ, Retry & Replay (OpsBridge)

| Req ID | Business Rule | Acceptance Criteria | Priority |
|--------|---------------|---------------------|----------|
| **R3.1** | **Exponential Backoff**: 2^attempt seconds, capped at 60s. | attempt=1→2s, 2→4s, 5→32s, ≥6→60s | **P0** |
| **R3.2** | **Max Retry Limit**: 5 (outbox) or 3 (RabbitMQ consumer) → dead-letter. | RetryCount≥max → `DeadLetteredAt`, DLQ entry | **P0** |
| **R3.3** | **DLQ Audit Trail**: Every DLQ entry stores EventId, EventType, Topic, Payload, Reason, RetryCount, FailedAt, IsReplayed, ReplayedAt, ReplayedBy. | DLQ entry has all fields; `IsReplayed=false` initially | **P0** |
| **R3.4** | **Idempotent Manual Replay**: Replay twice → 1 `ReplayQueued` + 1 `AlreadyReplayed`; 1 OutboxMessage. | 12 concurrent → 1+11, exactly 1 outbox | **P0** |
| **R3.5** | **Replay Creates Pending Outbox**: Replayed event → OutboxMessage `Status=Pending` in same transaction as `IsReplayed=true`. | OutboxMessage inserted in same tx | **P0** |
| **R3.6** | **At-Least-Once + Idempotent Consumer**: Duplicate deliveries ACKed without reprocessing. | Inbox dedupe → `ConsumerProcessingStates` count = 1 | **P0** |
| **R3.7** | **Dispatcher Skips Poison, Continues Batch**: [OK, POISON, OK] → 2 published, 1 Failed. | Batch not blocked by single failure | **P0** |
| **R3.8** | **Explicit Dead-Letter State**: `DeadLetteredAt` is source of truth, not inferred from RetryCount. | RetryCount=5, DeadLetteredAt=null → `IsDeadLettered=false` | **P0** |
| **R3.9** | **StockDriftException → Immediate DLQ**: Drift is business error, not transport; no retry delay. | **GAP** — no test exists | **P1** |
| **R3.10** | **x-retry-count Header**: Republished messages carry incremented retry count. | `x-retry-count` = previous + 1 | **P1** |

---

## Domain 4: Three-Way Matching & Reconciliation (OpsBridge)

| Req ID | Business Rule | Acceptance Criteria | Priority |
|--------|---------------|---------------------|----------|
| **R4.1** | **Perfect Three-Way Match**: PO/GR/Invoice qty/price match → `Matched`, allocations committed. | PO(100@20k), GR(100), Inv(100@20k) → `Matched`, 1 allocation 100 PCS | **P0** |
| **R4.2** | **Partial GR → QuantityMismatch**: Invoiced > received → `Discrepancy`, no allocations. | PO(100), GR(60), Inv(100) → `Discrepancy`, variance=40 | **P0** |
| **R4.3** | **Multiple GR Cumulative Allocation**: Multiple receipts sum to invoiced → `Matched`, allocations across all. | GR1(60)+GR2(30)+GR3(10) → 3 allocations (60,30,10) | **P0** |
| **R4.4** | **Partial Invoicing Non-Overlapping**: Sequential invoices consume available receipt qty; over-invoicing blocked. | Inv1(60)→Matched, Inv2(40)→Matched, Inv3(10)→Discrepancy | **P0** |
| **R4.5** | **Price Tolerance (±1% default)**: Invoice price within tolerance → `Matched`; outside → `PriceMismatch`. | PO@20k, Inv@20.2k (+1%) → Matched; Inv@20.3k (+1.5%) → Discrepancy | **P0** |
| **R4.6** | **Quantity Tolerance (abs/%)**: Configurable absolute/percentage variance allowance. | GR(98), Inv(100), Abs=2 → Matched | **P0** |
| **R4.7** | **UOM Conversion with Mapping Rules**: Invoice in BOX (1 BOX=10 PCS) → normalized → `Matched`. | 10 BOX @200k/BOX → 100 PCS @20k/PCS | **P0** |
| **R4.8** | **Pre-Validation Discrepancies**: Missing PO/GR/Currency/Supplier → specific discrepancy types. | MissingPO, MissingGR, CurrencyMismatch, SupplierMismatch | **P0** |
| **R4.9** | **SKU Alias Mapping**: Invoice SKU mapped to PO SKU via active `MappingRuleType.SkuAlias`. | **GAP** — no test exists | **P1** |
| **R4.10** | **Concurrency-Safe Allocation**: Two concurrent reconciliations for same GR → exactly 1 succeeds, other `QuantityMismatch`; allocated ≤ received. | PO(100), GR(100), two Inv(70) → 1 Matched + 1 Discrepancy, allocated=70 | **P0** |
| **R4.11** | **Reference Scenario Rerun**: PO-1001 (100), GR1(60)+GR2(30) → Discrepancy(10); add GR3(10) → rerun → Matched(60,30,10). | Cumulative receipts match on rerun | **P0** |
| **R4.12** | **Discrepancy Review RBAC**: Only FinanceReviewer can approve/reject; Auditor/Operator → 403. | Auditor 403, FinanceReviewer 200, audit trail | **P0** |
| **R4.13** | **ApproveWithTolerance**: Commits allocations, transitions to `ApprovedWithVariance`. | Invoice=ApprovedWithVariance, GR Allocated=full, Available=0 | **P0** |
| **R4.14** | **Reject Keeps Receipts Available**: Rejected → Invoice=Rejected, GR Allocated=0, Available=full. | No allocations committed | **P0** |
| **R4.15** | **Duplicate Invoice Detection**: Same InvoiceNumber+SupplierCode → existing record returned. | Second ingest → repository returns existing | **P1** |
| **R4.16** | **Already-Matched Immutable**: Invoice Status=Matched → cannot reconcile again. | Throws `DomainException` "already matched and allocated" | **P1** |
| **R4.17** | **Invalid UOM Without Rule → UomConversionError**: Missing mapping rule is explicit error. | PO(PCS), Inv(PALLET), no rule → Discrepancy Type=UomConversionError | **P1** |

---

## Domain 5: RBAC & Authorization Boundaries (FlashSale + OpsBridge)

| Req ID | Business Rule | Acceptance Criteria | Priority |
|--------|---------------|---------------------|----------|
| **R5.1** | **Role Set Definition**: FlashSale: Customer/Staff/Admin; OpsBridge: Admin/Operator/Auditor/FinanceReviewer. | Exact roles defined; `IsValid()` rejects unknown | **P0** |
| **R5.2** | **Registration Hardening**: Self-registration always `CUSTOMER`; role in body ignored. | `"role":"ADMIN"` → returns `CUSTOMER` | **P0** |
| **R5.3** | **Bootstrap Admin (First-Admin Only)**: `POST /api/auth/bootstrap` with `X-Bootstrap-Token`; 404 if unset, 403 wrong token. | Creates ADMIN once; existing email → 409 | **P0** |
| **R5.4** | **FlashSale Policies**: `StaffOrAdmin` (Staff/Admin), `Authenticated` (any). | Registered via `AddFlashSalePolicies()` | **P0** |
| **R5.5** | **OpsBridge Policies**: `AdminOnly`, `OperatorOrAdmin`, `AuditorOrAbove`, `Review` (Admin+FinanceReviewer). | Registered via `AddOpsBridgePolicies()` | **P0** |
| **R5.6** | **FlashSale Deny-by-Default**: Every route `RequireAuthorization()` or in `PublicRoutes` allowlist with justification. | Static source scan enforces | **P0** |
| **R5.7** | **OpsBridge Deny-by-Default**: Every OpsBridge route `RequireAuthorization()` or in `PublicRoutes` (login, healthz, webhook). | Static source scan enforces | **P0** |
| **R5.8** | **Staff/Admin Endpoints Protected**: `/internal/**`, `/api/outbox/**`, `/api/inbox/**`, `/internal/resync-stock` require `StaffOrAdmin`. | Anonymous 401, Customer 403, Staff/Admin 2xx | **P0** |
| **R5.9** | **Customer Ownership Enforcement**: `GET /api/orders/{key}`, `POST /api/orders/{key}/pay` require ownership match; Staff/Admin bypass. | Wrong customer 403, Owner 200, Staff 200 | **P0** |
| **R5.10** | **OpsBridge Role Scopes**: Connectors read=Auditor+, write=Operator+; Reconciliation create=Operator+, read=Auditor+, run=Operator+, review=Review; Ingestion read=Auditor+; Webhook=HMAC only. | Role matrix enforced per endpoint | **P0** |
| **R5.11** | **JWT Validation Hardening**: ValidateIssuer/Audience/Lifetime/SigningKey, ClockSkew=30s; Production rejects default/short keys. | Missing key → startup throw; <32 bytes → throw | **P0** |
| **R5.12** | **Refresh Token Rotation + Replay Protection**: Compare-and-swap on TokenVersion; logout bumps version; access token survives ≤15min. | Refresh rotates; replay 401; concurrent exactly 1 wins; logout invalidates refresh | **P0** |
| **R5.13** | **Account Enumeration Resistance**: Unknown email/wrong password → identical error; register duplicate email → 409 case-insensitive. | Same 401/409 messages | **P0** |
| **R5.14** | **Prometheus Metrics Auth Separation**: `/metrics` public (no `auth.*`); `/internal/metrics` staff-only (includes `flashsale.auth.*`). | Anonymous `/metrics` 200 no auth.*; Staff `/internal/metrics` 200 with auth.* | **P0** |
| **R5.15** | **Anonymous Checkout Preserved**: `POST /api/orders` no auth (ADR-013); authenticated callers get `UserId` attached. | No token → 202; With token → 202 + UserId | **P0** |
| **R5.16** | **Saga Checkout Authenticated (Not Staff)**: `POST /api/saga/checkout` requires auth but not `StaffOrAdmin`. | Anonymous 401, Customer passes | **P0** |
| **R5.17** | **Webhook HMAC Auth (Not Bearer)**: `/api/v1/webhooks/factoryflow` uses Svix/HMAC headers, not bearer. | Missing/invalid signature 401; Valid 202 | **P0** |
| **R5.18** | **Health Endpoints Public**: `/health/live`, `/healthz`, `/health/ready` (FlashSale), `/healthz` (OpsBridge) no auth. | 200 without Authorization header | **P0** |

---

## Summary

| Domain | P0 | P1 | Total | PASS | PARTIAL | GAP |
|--------|----|----|-------|------|---------|-----|
| 1. No-Oversell & Concurrent Ordering | 7 | 0 | 7 | 7 | 0 | 0 |
| 2. Transactional Outbox/Inbox & Idempotency | 8 | 2 | 10 | 10 | 0 | 0 |
| 3. DLQ, Retry & Replay | 8 | 2 | 10 | 3 | 6 | 1 |
| 4. Three-Way Matching & Reconciliation | 14 | 3 | 17 | 9 | 4 | 1 |
| 5. RBAC & Authorization Boundaries | 18 | 0 | 18 | 18 | 0 | 0 |
| **Overall** | **55** | **7** | **62** | **47** | **10** | **2** |

- **PASS**: Unit tests pass locally + code enforcement verified
- **PARTIAL**: Integration tests written, require Testcontainers (Postgres/RabbitMQ) — will PASS when `opsbridge-tests` runs in GitHub Actions
- **GAP**: Missing test code — must be added before AI-1

---

## Non-Functional Requirements

| NFR ID | Requirement | Target | Verification |
|--------|-------------|--------|--------------|
| **NFR-C01** | No overselling | 0 violations | Concurrency harness: stock 10 → 7, 0 oversell |
| **NFR-C02** | Checkout independent of AI | Works when AI down | FlashSale path has no AI dependency |
| **NFR-C03** | RTO ≤ 2h, RPO ≤ 15 min | Restore drill | `docs/business/recovery-targets.md` — **GAP**: manual only, no drill |
| **NFR-C04** | Checkout p95 < 500ms | Benchmark | `docs/benchmarks/` — **UNVERIFIED** (doc only, not test gate) |
| **NFR-C05** | Zero unauthorized operations | Deny-by-default | `OpsBridgeAuthorizationGuardTests` + `OpsBridgeAuthBoundaryTests` |
| **NFR-C06** | Zero duplicate business records | Replay/consumer idempotent | `FailureDrill6` (12→1+11, 1 outbox) |
| **NFR-C07** | AI failure isolation | AI error doesn't block core | BankOps/AI domain — N/A for OpsBridge |

---

## Invariants (Business Rules Proven)

| Invariant | Description | Test Evidence | Status |
|-----------|-------------|---------------|--------|
| **INV-01** | No double effect replay | `FailureDrill5`, `FailureDrill6` | PASS |
| **INV-02** | No overselling | Concurrency harness stock 10→7 | PASS |
| **INV-03** | Route deny-by-default + 3 public allowlist | `OpsBridgeAuthorizationGuardTests` | PASS |
| **INV-04** | Outbox + business in 1 PostgreSQL transaction | `FailureDrill1`, `EfUnitOfWork` | PASS |
| **INV-05** | Idempotent webhook ingestion | `PostWebhook_ConcurrentIdenticalWebhooks` (10→1) | PASS |
| **INV-06** | Refresh token rotation + replay protection | `Refresh_ConcurrentRotations_ExactlyOneWins` | PASS |