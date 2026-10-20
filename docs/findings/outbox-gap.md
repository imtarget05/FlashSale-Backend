# Finding: the FlashSale business flow publishes domain events OUTSIDE the write path (no outbox)

- **Status:** OPEN — needs an architecture decision (owner: repo maintainer)
- **Found:** 2026-10-20, during the production-fidelity pass on the `docker-compose` stack
- **Severity:** P1 (design gap, no live incident) — *not* fixed by this change
- **Scope of this note:** analysis only. No domain code was changed.

---

## 1. What the gap is

The **business** flow of the flash-sale app (submit order → worker fulfils → record payment →
checkout saga) publishes its domain events **directly** to the event transport, in a
best-effort `try/catch`, *after* the PostgreSQL write has already been committed. The
`OutboxMessages` table is never written by that flow.

The **ingestion** flow of the OpsBridge bounded context (signed webhook → `ReceiveWebhookUseCase`)
*does* write the outbox inside the same transaction, so ADR-015 is honoured there.

| Producer | Event(s) | Writes `OutboxMessages`? | Publishes |
|---|---|---|---|
| `src/FlashSale.Application/Orders/SubmitOrderUseCase.cs:117` | `inventory.reserved` | ❌ | direct, best-effort (`try/catch` around `eventPublisher.PublishAsync`) |
| `src/FlashSale.Application/Orders/OrderProcessor.cs:58` | `order.created` | ❌ | direct, best-effort (worker side, after `PersistAsync` committed) |
| `src/FlashSale.Application/Automation/RecordPaymentUseCase.cs:139` (`PublishSafeAsync`) | `payment.completed` / `payment.failed` | ❌ | direct, best-effort |
| `src/FlashSale.Application/Saga/CheckoutSagaCoordinator.cs:268` (`PublishSafeAsync`) | `checkout.saga.started` / `checkout.saga.completed` / compensation events | ❌ | direct, best-effort |
| `src/OpsBridge.Application/UseCases/ReceiveWebhookUseCase.cs:157` | `goods.receipt.confirmed` | ✅ (same transaction) | via `OutboxDispatcherHostedService` |

The code says so itself — `SubmitOrderUseCase` comments the publish as
*"Best-effort publish — a bus outage must NOT fail acceptance (the order row is the truth;
**no-outbox is a documented gap**)"*.

### Evidence from the running stack (2026-10-20, `docker compose` project `flashsale-backend`)

Business flow executed for real (order accepted → worker → payment recorded → saga checkout):

```text
POST /api/orders              -> 202 {"status":"processing"}
worker consumes 'orders'      -> Orders.Id=8 Status=PendingPayment
POST /api/orders/{key}/pay    -> 200 {"status":"confirmed"}   (Orders.Status=Confirmed)
POST /api/saga/checkout       -> 200 {"status":"completed"}    (tx_319eaaccb48e4e4ea1e958c529f55355)
```

…and the outbox table was still empty for that traffic:

```text
$ psql -c 'SELECT count(*) FROM "OutboxMessages";'
 count
-------
     1      <-- the single row is a manual POST /api/outbox/enqueue (Program.cs:686) from a
                previous verification, NOT from the business flow
```

The events themselves *did* reach RabbitMQ (the backbone now works — see
`docker-compose.yml`, `Events__Provider=RabbitMQ`): the worker's `AutomationWorkerHost`
consumed `inventory.reserved`, `order.created`, `payment.completed`, `order.confirmed` from the
`automation.events` topic exchange and wrote `AutomationRuns` rows with `TriggerType='domain.event'`,
`Status='Success'`.

**So the transport works; what is missing is atomicity between the database write and the
publish.** The event can still be lost if the process dies between "transaction committed" and
"message published", or if the broker is down during that window — the publish failure is
swallowed and logged as a warning, and nothing replays it.

---

## 2. Two options

### Option A — wrap the publish in the write transaction (classic transactional outbox)

In each producing use case, insert an `OutboxMessages` row **inside the same
`AppDbContext` transaction** as the state change, and let the existing
`OutboxDispatcherHostedService` publish it (it already claims batches with
`FOR UPDATE SKIP LOCKED`, retries with backoff, dead-letters and marks rows processed on
acknowledgement — `src/FlashSale.Infrastructure/Outbox/OutboxDispatcherHostedService.cs`,
`src/FlashSale.Application/Outbox/OutboxDispatcherUseCase.cs`).

- **Pros:** zero message loss; matches ADR-015 and the k8s/`OpsBridge` ingestion path; the
  machinery already exists, so this is wiring, not new infrastructure.
- **Cons / work:** the use cases are not currently transaction-scoped in one place (each
  repository call commits on its own), so the transaction boundary has to be introduced;
  the dispatcher publishes a generic `OutboxDomainEventWrapper`, so the consumer must accept
  both typed direct events and wrapped ones; the saga has a multi-step + compensation
  semantics that must be modelled carefully (a partially-outboxed saga is worse than none).

### Option B — keep the direct publish, add consumer-side idempotency + reconciliation

Leave the producers as they are and make the *loss* detectable and bounded:

- dedupe on the consumer with the existing `InboxMessages` table (idempotent handler),
- add a reconciliation job (compare `Orders.Status`/`AutomationRuns` against the events that
  should have existed) that re-emits missing events,
- alert on the `publish failed` warning that today is only logged.

- **Pros:** no change to the write path; no extra latency; the producers stay simple.
- **Cons:** the gap is *not closed* — a crash between commit and publish still loses the
  event, and reconciliation only repairs it after the fact. Duplicates are possible unless
  the consumer is idempotent.

---

## 3. Risks that apply to both options

- **At-least-once, not exactly-once.** Any outbox (or redelivery) scheme can deliver the same
  event more than once. The consumer must be idempotent; the `Inbox` table is the intended
  mechanism but is currently only used by the OpsBridge ingestion path.
- **Duplicate side effects.** `AutomationRuns` rows and order transitions would be applied
  twice unless the consumer dedupes on `EventId`/`CorrelationId`.
- **Delivery latency.** Outbox dispatch is polled (default ~1 s), so events land after the
  DB write, not with it. Acceptable for automation triggers; it would matter if any
  user-facing flow depended on the event being immediate.
- **Table growth.** `OutboxMessages` needs retention/archival once the business flow writes
  to it (ADR-015 already lists this as a trade-off).
- **Backpressure coupling.** Today a broker outage cannot fail an order acceptance. With an
  outbox, the write still succeeds (rows queue up) — but the queue depth becomes a new
  operational signal that has to be monitored.

---

## 4. Recommendation

**Option A**, scoped in two steps:

1. **Step 1 (low risk, big win):** wire `SubmitOrderUseCase` and `RecordPaymentUseCase` (and
   `OrderProcessor` on the worker) to write the outbox row in the same transaction, reusing the
   existing `OutboxMessages` + dispatcher infrastructure. Add inbox dedup on the consumer so the
   at-least-once delivery is safe.
2. **Step 2 (defer):** the checkout saga. Its compensation path must be modelled first
   (a compensated saga must publish `checkout.saga.compensated`, not a resurrected
   `checkout.saga.completed`), so it should not be swept into step 1.

Option B is only worth it if the business flow is deliberately kept "best-effort" — and then
ADR-015 should be amended to say so explicitly, because today the ADR reads as if the outbox
covers the whole system.

**This is a decision, not an implementation task** — it changes the transaction boundary of the
core write path, and it needs the maintainer's call on scope (step 1 only vs. step 1+2) and on
whether the ADR is amended.

---

## 5. Side observation (not part of this gap)

With the event backbone now real, two saga events arrive at the consumer with **no handler**
and are dead-lettered:

```text
Unreadable automation event (type=checkout.saga.started) — dead-lettering.
Unreadable automation event (type=checkout.saga.completed) — dead-lettering.
```

That is a consumer-coverage gap (the `AutomationEventProcessor` does not know the saga event
types), not a transport problem. It was invisible while the backbone was in-memory. Worth a
follow-up decision; not changed here.
