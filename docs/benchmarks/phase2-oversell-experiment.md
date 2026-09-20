# Benchmark: Phase 2 — Overselling Experiment (Naive API)

- **Date**: 2026-09-19 (local run)
- **Environment**: macOS, .NET 10 minimal API, PostgreSQL 15 (docker `postgres:15-alpine`), API and DB both on localhost.
- **Code under test**: `src/Order.Api/Program.cs` Phase-1 naive endpoint (read → check → decrement in memory → `SaveChanges`).
- **Harness**: `load-tests/concurrency/oversell_demo.py` (Python stdlib; threads released simultaneously by a barrier).
- **Raw output**: `docs/benchmarks/phase2-oversell-raw-output.txt`.

## Scenario
| Parameter | Value |
|---|---|
| Initial stock (reset before run) | 10 |
| Concurrent purchase requests | 50 (all fired at the same instant) |
| Quantity per order | 1 |
| Expected accepted orders | 10 (stock limit), 40 rejections |

## Results (actual)
| Metric | Value |
|---|---|
| HTTP 200 accepted | **50 / 50** |
| HTTP 4xx rejected | 0 |
| Transport failures | 0 |
| p95 latency | ~319 ms |
| Orders persisted | 50 |
| Final stock in DB | **9** (10 − 1) |
| Expected stock if correct | −40 i.e. only 10 orders should have succeeded |

## Interpretation
- Every one of the 50 concurrent buyers received `200 OK — Order placed successfully`.
- The database only decremented stock **once** (10 → 9): 49 "lost updates".
- The API sold **50 units of a 10-unit inventory** — a critical business failure (overselling).
- Root cause: classic **read-check-write race**. All 50 requests read `AvailableStock = 10`,
  each passed the `stock >= quantity` check, then wrote `9` back. No transaction isolation,
  no row lock, no atomic compare-and-set.

## Business impact
Customers who were promised an order cannot all be fulfilled — the retailer must cancel
orders, refund, and absorb reputational damage. This is exactly the failure the business
requirement "No Overselling (Strict Inventory Control)" prohibits.

## Baseline performance note
p95 ≈ 319 ms under only 50 concurrent requests is also poor; the naive design holds no
backpressure. Phase 4+ will measure whether Redis-based reservation improves p95 — numbers
above are the honest baseline we improve against.

**Next**: Phase 3 fixes the decrement atomically in PostgreSQL and re-runs this exact
experiment — success criterion: `accepted == 10`, `final stock == 0`.
