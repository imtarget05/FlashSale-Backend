# Benchmark: Phase 4 — High-Load Behavior of the Atomic-Update API

- **Date**: 2026-09-19 (local run)
- **Code under test**: Phase-3 endpoint (atomic conditional UPDATE) under 4x higher load.
- **Harness**: `load-tests/concurrency/oversell_demo.py --stock 20 --requests 200`
- **Raw output**: `docs/benchmarks/phase4-highload-raw-output.txt`

## First run — a bonus finding (connection-pool headroom)
The very first 200-concurrent run exposed an operational failure **before** any code
change: PostgreSQL's default `max_connections = 100` was fully consumed by the API's
connection pool (default max pool = 100), leaving *zero* headroom for out-of-band
access — even the audit `psql` could not connect mid-burst. No API errors occurred
(pooled requests simply queued), but operators were locked out exactly when it mattered.

**Fix applied**: cap the API pool at 80 (`Maximum Pool Size=80`) — always strictly below
the server limit so operators/migrations keep connection headroom.

## Second run — measured results (200 concurrent, stock 20)
| Metric | Phase 3 (50 conc.) | Phase 4 (200 conc.) |
|---|---|---|
| Accepted (200) | 10 | **20** |
| Rejected (409) | 40 | **180** |
| Transport failures | 0 | 0 |
| p95 latency | ~245 ms | **~574 ms** |
| Final stock | 0 (exact) | 0 (exact) |
| Correctness | PASS | **PASS** |

## Interpretation
- Correctness scales: 20/20 accepted == stock, 180 honest rejections, stock exactly 0.
- **p95 grew 2.3x when load grew 4x** — super-linear row-lock queuing on the hot product row.
- The bigger issue is *economics*: **180 of 200 requests (90%) hit PostgreSQL for one
  round-trip just to be told "no"**. At the business target (5,000 concurrent buyers),
  that is ~4,500 wasted DB round-trips per sale — saturating the pool and queuing the
  row lock for seconds.

## Decision (see ADR-003)
The database remains the source of truth, but hopeless buyers must be filtered *before*
the DB. Redis provides an atomic, single-round-trip reservation layer — introduced in
Phase 5 together with asynchronous fulfillment.
