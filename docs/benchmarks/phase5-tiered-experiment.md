# Benchmark: Phase 5 — Tiered Architecture (Redis Fast-Fail + Async Fulfillment)

- **Date**: 2026-09-19 (local run)
- **Code under test**: `POST /api/orders` with Redis reservation (Lua CAS) → bounded
  in-memory queue → idempotent worker (atomic DB transaction). Service Bus swaps in
  for the local channel in Azure (same contract).
- **Harness**: identical to Phase 3/4 for comparability (10 stock, 50 concurrent).
- **Raw output**: `docs/benchmarks/phase5-tiered-raw-output.txt`

## Scenario (identical to Phase 2/3)
| Parameter | Value |
|---|---|
| Initial stock (reset + resync) | 10 |
| Concurrent purchase requests | 50 (simultaneous) |
| Quantity per order | 1 |

## Results (actual)
| Metric | Phase 2 (naive) | Phase 3 (atomic SQL) | Phase 5 (tiered) |
|---|---|---|---|
| Accepted | 50 (oversold!) | 10 | **10** |
| Rejected (409) | 0 | 40 | **40** |
| p95 latency | ~319 ms | ~245 ms | **~29 ms** |
| DB round-trips for rejected buyers | 50 | 40 | **0** |
| Final stock | 9 (broken) | 0 | **0** |
| Orders persisted | 50 | 10 | **10** |
| Correctness | FAIL | PASS | **PASS** |

## Why p95 dropped ~8x
Rejected buyers no longer touch PostgreSQL at all — Redis answers their reservation in
one atomic Lua script (check + decrement + idempotency ledger). Only the 10 winners
generate any database work (the worker's atomic transaction). The hot row lock now
serves exactly the traffic that owns stock.

## Correctness model (verified)
- Idempotency: repeating the same `Idempotency-Key` → `202` once, then `409 Duplicate`
  (Redis ledger filters fast; the DB unique index is the authoritative backstop).
- Conservation: `final_stock (0) == initial (10) − accepted (10)` under full concurrency.
- Async fulfillment: worker persists each reservation exactly once (settle-audit agrees).

## Honest notes
- 29 ms p95 reflects the *reservation* path; winners wait for async fulfillment
  (status: `processing` → `completed`). The HTTP contract now says "accepted" — that is
  the correct semantic for flash sales (confirm fast, fulfill asynchronously).
- Local benchmark runs a single API process; in Azure the queue (Service Bus) and worker
  scale independently (KEDA on queue depth — see 03-AKS-SRE-Platform).