# ADR-002: Fix Overselling with an Atomic Conditional UPDATE in PostgreSQL

- **Status**: Accepted
- **Date**: 2026-09-19
- **Deciders**: Project owner + engineering agent

## Context
Phase 2 proved the naive read-check-write loses updates under concurrency
(50 accepted orders for 10 units — `docs/benchmarks/phase2-oversell-experiment.md`).
We need correctness first, at the lowest possible complexity.

## Options Considered
1. **Application-level lock (e.g., C# `lock`/`SemaphoreSlim`)** — rejected: only works in a
   single process; breaks the moment we scale to 2+ API instances (which is the whole point
   of a flash-sale deployment).
2. **Distributed lock (Redis `SET NX`)** — rejected for now: adds a new technology before
   the database itself has been given the chance to solve it; keep as a Phase 4 option only
   if PostgreSQL becomes the measured bottleneck.
3. **Serializable isolation level for the transaction** — considered: correct, but retries
   on serialization failures add complexity and can hurt throughput under contention.
4. **Atomic conditional UPDATE (chosen)** — one statement does "decrement only if enough
   stock", inside one transaction with the order insert. Row lock is held for microseconds;
   correctness is guaranteed by the database, not by app code.

## Decision
In `POST /api/orders`, wrap the operation in an explicit transaction and perform:

```sql
UPDATE "Products"
SET    "AvailableStock" = "AvailableStock" - @quantity
WHERE  "Id" = @productId
  AND  "AvailableStock" >= @quantity;
```

- Rows affected `1` → stock secured: insert the `Order` in the same transaction, commit.
- Rows affected `0` → insufficient stock: rollback, return `409 Out of stock`.

The EF-tracked entity is refreshed after the update so the HTTP response reflects reality.

## Consequences
- **Positive**: No oversell is physically possible; no new infrastructure; interview-friendly
  explanation ("compare-and-set at the database").
- **Negative**: All contention lands on a single PostgreSQL row; under much higher load this
  row becomes a hot spot (queuing/latency). That limitation is *expected* and will be
  measured in Phase 4 to decide whether Redis reservation is justified.
- **Verification**: Re-run `load-tests/concurrency/run_experiment.sh` against the fixed API.
  Success = accepted == initial stock, final stock == 0, zero rejections miscounted.