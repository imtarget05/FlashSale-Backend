# Interview Notes — Phase 6–9 (Automation, Outbox, Saga, Auth)

These notes cover the business automation platform, transactional outbox, checkout saga, and authentication features added after the initial concurrency fix.

---

## 1. Business Automation Platform (Phases 0–7)

> "The business asked: can the system run itself overnight without anyone watching?"

**Problem**: Flash sales generate cascading actions — payment reminders, inventory restocking, daily reports — that were all manual.

**Solution**: An automation platform with a typed event model, audit trail, and modular workflows.

### How to explain it:

**Event model** (Phase 1):
- 8 domain events: `order.created`, `inventory.reserved`, `payment.completed`, `payment.failed`, `payment.expired`, `order.confirmed`, `order.cancelled`, `inventory.released`
- Each event has: `eventId`, `eventType`, `occurredAt`, `correlationId`, `source`
- Why correlationId? So you can trace a single order through all 8 events in a dashboard.

**Payment timeout automation** (Phase 2):
- Problem: buyers who place orders but never pay create phantom stock.
- Solution: `PaymentTimeoutRule` (pure domain logic, unit-testable) + `PaymentTimeoutScanHostedService` (runs every 60s).
- Flow: PendingPayment → 15min grace → reminder logged + audit row → past grace → cancel + DB stock release + Redis mirror (atomic transaction).
- Talking point: "The domain rule is a pure function. It doesn't know about databases or timers. The hosted service is the adapter."

**Inventory low-stock automation** (Phase 3):
- `LowStockRule` (pure): `stock <= threshold` (inclusive boundary, unit-tested).
- `LowStockAlertUseCase`: scans real stock values, creates deduplicated `StockAlerts` rows via partial unique index.
- Talking point: deduplication via database constraint, not application-level locking.

**Daily business reports** (Phase 4):
- `DailyReportUseCase`: aggregates from PostgreSQL only (no AI, no estimates).
- `ReportWindow` helper: UTC-pinned (locale-proof).
- One row per UTC day — upsert semantics: re-running a report = idempotent.

### Numbers to remember:
| Phase | Tests |
|---|---|
| After Phase 2 | 82 unit + 24 integration = 106 PASS |
| After Phase 3 | 89 unit + 28 integration = 117 PASS |
| After Phase 4 | 91 unit + 31 integration = 122 PASS |
| Final (Phase 7) | 134 unit + 40 integration = **174 PASS** |

---

## 2. Transactional Outbox (Phase: Outbox/Inbox)

> "The naive approach: publish to RabbitMQ inside the HTTP handler. What happens if the broker is down at the commit moment?"

**Problem**: At-least-once event delivery with no outbox = message loss on broker failure.

**Solution**: Transactional outbox pattern.

### How to explain it:

```
1. Business transaction (order placed) → writes to Orders + OutboxMessages in ONE transaction
2. OutboxPublisher (background) polls OutboxMessages WHERE Status=Pending
3. Publishes to RabbitMQ, marks Status=Delivered
4. If delivery fails → exponential backoff (MaxAttempts, backoff multiplier in config)
5. If MaxAttempts exceeded → DLQ entry (OutboxMessages.DlqReason)
```

**Claim lease pattern** (concurrency safety):
```sql
-- Claim up to N messages atomically
UPDATE OutboxMessages SET ClaimedAt = NOW(), ClaimedBy = 'instance-id'
WHERE Id IN (
  SELECT Id FROM OutboxMessages WHERE Status = 'Pending' AND ClaimedAt IS NULL
  LIMIT 10 FOR UPDATE SKIP LOCKED
)
```
- `SKIP LOCKED`: multiple worker instances don't compete — each claims its own batch.
- Talking point: "This is a database-native concurrent producer pattern. No external lock manager needed."

**Inbox (idempotent consumer)**:
- `InboxMessages` table: `SELECT 1 WHERE MessageId = @id` before processing.
- If found → skip (dup message from broker redelivery).
- Insert after processing → committed atomically with the business outcome.

### Key interview points:
1. "The outbox guarantees at-least-once delivery from the DB perspective. The inbox eliminates duplicates at the consumer."
2. "Together they achieve exactly-once business effects without exactly-once delivery (which doesn't exist)."
3. "The backoff config is externalised — no magic constants in code."

---

## 3. Checkout Saga (Phase: CheckoutSagas)

> "A distributed transaction that can compensate if any step fails."

**Problem**: Placing an order requires: reserve inventory → create order → capture payment → confirm. Any step can fail. Without saga, you get orphaned reservations.

**Solution**: `CheckoutSaga` with explicit state machine + compensation.

### States:
```
Started → ReservationConfirmed → PaymentCaptured → Completed
                                                  ↘
                                       [failure]  → Compensating → Cancelled
```

### Compensation logic:
- `PaymentFailed` → trigger `ReleaseReservation` + mark saga `Cancelled`
- `PaymentTimeout` → same compensation path
- Each compensation step is idempotent (saga state check before action)

### How to explain it:

1. "The saga is a state machine persisted to the `CheckoutSagas` table. If the process crashes mid-flight, the recovery worker picks it up from its last known state."
2. "Compensation is not rollback — the DB changes are committed. We undo effects with inverse operations (release inventory, refund payment)."
3. "The `MANUAL_INTERVENTION_REQUIRED` escape hatch: if compensation itself fails, an operator is alerted rather than the system looping forever."

---

## 4. Authentication (JWT + Refresh Rotation)

> "Why not sessions? Flash sales are stateless and horizontally scaled."

**Design**:
- Short-lived access token (15min JWT)
- Long-lived refresh token (7-day, stored in DB with rotation)
- Rotation: old refresh token is invalidated on use → replay attack prevention

### How to explain it:

1. "Refresh token rotation means each refresh produces a new token. If a stolen token is replayed, the legitimate user's next refresh invalidates the attacker's token."
2. "PBKDF2 password hashing (not bcrypt) — .NET built-in, auditable, no external dependency."
3. "Anonymous order path is preserved for backward compatibility (ADR-013 §6). Auth is additive, not breaking."

### Auth policy model:
```csharp
// Endpoints depend on policies, not on concrete auth mechanics
[Authorize(Policy = AuthPolicies.OrderOwner)]  // scoped GET /orders/me
[Authorize(Policy = AuthPolicies.InternalApi)] // internal automation endpoints
```

---


## 6. Numbers to Remember

| Feature | Metric |
|---|---|
| Tests (final) | 166 unit + 40 integration = **206 PASS** |
| Auth smoke | 48/48 gates |
| Outbox claim lease | SKIP LOCKED, N at a time |
| Saga compensation paths | 4 failure paths |
| AI assistant latency | 18–70s (qwen3 reasoning model) |
| Rate limit window | Redis sliding window, Redis-atomic |
