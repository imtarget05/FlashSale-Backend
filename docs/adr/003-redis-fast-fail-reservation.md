# ADR-003: Add a Redis Fast-Fail Reservation Tier in Front of PostgreSQL

- **Status**: Accepted
- **Date**: 2026-09-19
- **Deciders**: Project owner + engineering agent

## Context (measured, not assumed)
Phase 4 measured the atomic-update API at 200 concurrent buyers:
- Correctness held (20 accepted / 180 rejected / stock exactly 0).
- But p95 grew super-linearly (245 ms → 574 ms) from hot-row lock queuing, and
  **90% of requests spent a PostgreSQL round-trip just to be rejected**.
- The business requirement is **5,000 concurrent purchase attempts within seconds**
  (`docs/requirements/requirements.md`) — extrapolating honestly, DB-only rejection
  would saturate the connection pool and queue the row lock for seconds.

## Options Considered
1. **Keep DB-only** — correct, but rejection cost scales with hype traffic; the
   connection pool becomes the first casualty (already reproduced once — see the
   pool-headroom incident in `docs/benchmarks/phase4-highload-experiment.md`).
2. **Cache stock reads only** — helps dashboards, not the write path; buyers would
   still stampede the atomic UPDATE.
3. **Redis reservation tier (chosen)** — atomic Lua check-and-decrement with an
   idempotency ledger, one round-trip, rejects hopeless buyers before the DB.

## Decision
Introduce Redis as a **fast-fail filter, never the source of truth**:
- `stock:{productId}` hash holds the hot counter, mirrored from PostgreSQL
  (startup + resync endpoint `POST /internal/resync-stock/{id}`).
- `reservation:{productId}` set records idempotency keys (duplicate detection).
- Reservation is one atomic Lua script: check qty → decrement → record key.
- On queue-full (backpressure) the API releases the reservation (Redis-only
  compensation; the DB was never touched).
- **Graceful degradation**: if Redis is unreachable, the API logs a warning and falls
  back to the Phase-3 synchronous DB path. Redis being down can slow us, never
  oversell us.
- **Drift is an incident, not a bug**: if the worker's authoritative atomic UPDATE
  disagrees with Redis (e.g. manual stock edit), the message is dead-lettered with
  `StockDriftException` and the runbook resync endpoint reconciles.

## Consequences
- **Positive**: rejected-buyer DB round-trips drop to ~0; p95 of the hot path measured
  29 ms vs 574 ms (phase5 benchmark). Duplicate protection moves in front of the DB.
- **Negative**: one more moving part to operate; drift risk requires the resync runbook
  and drift DLQ alerting (documented in the worker's dead-letter log).
- **Verification**: same harness, three-way comparison in
  `docs/benchmarks/phase5-tiered-experiment.md`.