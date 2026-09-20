# Benchmark: Phase 3 — Atomic Conditional UPDATE (Fix Verification)

- **Date**: 2026-09-19 (local run)
- **Code under test**: `src/Order.Api/Program.cs` Phase-3 endpoint — explicit transaction +
  atomic `UPDATE ... WHERE AvailableStock >= @qty` (ADR-002).
- **Harness**: identical to Phase 2 (`load-tests/concurrency/run_experiment.sh`) for a
  fair before/after comparison.
- **Raw output**: `docs/benchmarks/phase3-atomic-update-raw-output.txt`.

## Scenario (identical to Phase 2)
| Parameter | Value |
|---|---|
| Initial stock (reset before run) | 10 |
| Concurrent purchase requests | 50 (simultaneous) |
| Quantity per order | 1 |

## Results (actual)
| Metric | Phase 2 (naive) | Phase 3 (atomic UPDATE) |
|---|---|---|
| HTTP 200 accepted | 50 | **10** |
| HTTP 409 rejected | 0 | **40** |
| Orders persisted | 50 | **10** |
| Final stock in DB | 9 (broken) | **0 (exact)** |
| Expected stock if correct | 0 | 0 |
| p95 latency | ~319 ms | ~245 ms |
| Overselling | YES (5x over) | **NO** |

## Interpretation
- Exactly 10 buyers won stock; 40 received a fast, honest `409 Out of stock`.
- `final_stock (0) == initial_stock (10) − accepted_units (10)` — the inventory
  conservation law now holds under full concurrency.
- p95 did not regress (245 ms vs 319 ms): serializing 50 short transactions on one row is
  cheap; each transaction holds the row lock for ~1 ms of work.

## Honest Limitations (feeds Phase 4/5 decisions)
- All contention converges on **one PostgreSQL row** — fine at this scale, but the row is a
  hot spot. If load tests show lock queuing dominating latency (thousands of in-flight
  requests), a Redis reservation layer becomes *justified by measurement*, not fashion.
- No idempotency yet: a client retry of the same purchase still creates a second order.
  Addressed in Phase 5 with an idempotency key.
- Rejections are honest but "wasted" — Phase 5 will reject *before* hitting the database
  using a reservation/queue design, protecting the DB from hopeless traffic.