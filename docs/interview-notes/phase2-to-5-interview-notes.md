# Interview Notes — Phase 2 to 5 (How to Explain This System)

## 1. The bug story (Phase 2)
> "I built the naive version first, *on purpose*, then proved it broken."
- 50 simultaneous buyers → 50×200 OK, stock moved 10→9. Lost update: read-check-write
  race under READ COMMITTED. Evidence lives in `docs/benchmarks/phase2-*`.
- Talking point: why single-request testing never catches this.

## 2. The DB fix (Phase 3, ADR-002)
- One atomic statement: `UPDATE Products SET stock = stock - @q WHERE Id = @id AND stock >= @q`.
- Rows affected decides the outcome — a database-side compare-and-set.
- Why not `lock` in C#? Doesn't survive horizontal scale. Why not serializable? Retry
  storms; the conditional UPDATE is simpler and holds the row lock for ~1 ms.

## 3. When PostgreSQL is correct but not enough (Phase 4, ADR-003)
- Measured at 200 concurrent: correct, but p95 574 ms and 90% of DB round-trips were
  hopeless buyers. Business target is 5,000 concurrent.
- Bonus finding: pool (100) == server max_connections (100) → operators locked out
  mid-burst. Lesson: size pools below server limits. Fixed with `Maximum Pool Size=80`.

## 4. Redis as a filter, never as truth (Phase 5, ADR-003)
- One Lua script = atomic check+decrement+idempotency ledger. Rejected buyers cost
  **zero** DB round-trips → p95 29 ms (8x better than the DB-only path).
- Failure design: Redis down → degrade to the sync DB path (slower, still correct);
  Redis/DB drift → worker dead-letters with StockDriftException + resync runbook.

## 5. Async + idempotency (Phase 5, ADR-004)
- HTTP contract: 202 Accepted + status polling. Winners wait seconds, losers get a
  29 ms "no".
- Delivery is at-least-once (Service Bus peek locks can redeliver) → the consumer is
  idempotent: unique index on `Orders.IdempotencyKey` makes duplicates no-ops.
- "Exactly-once processing doesn't exist; exactly-once business effects do — via
  idempotency."
- Retries: exponential backoff, 4 attempts, then DLQ. Queue-full → 503 with reservation
  released (honest backpressure).
- Same `OrderProcessor` code powers the in-process dev worker and the standalone
  `Order.Worker` (Service Bus) — one behavior, two topologies; KEDA scales the worker
  on queue depth in AKS (Project 03).

## 6. Numbers to remember
| Phase | Accepted | p95 | DB hits for rejected buyers |
|---|---|---|---|
| 2 naive | 50 (oversold) | 319 ms | 50 |
| 3 atomic | 10 | 245 ms | 40 |
| 4 atomic @200 | 20 | 574 ms | 180 |
| 5 tiered | 10 | **29 ms** | **0** |