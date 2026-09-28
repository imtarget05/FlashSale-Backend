# Business Impact — FlashSale Backend (High-Concurrency Order Reliability)

Domain facts: .NET 10 Clean Architecture; Redis Lua CAS gatekeeper + PG atomic
conditional UPDATE (authoritative truth) + RabbitMQ at-least-once buffer +
idempotency keys; pool cap 80/100. See `README.md`, `docs/benchmarks/`,
`docs/adr/002-005`.

## Problem (cost of status quo)

Naive sync oversells under race (race conditions, DB timeouts, duplicate
requests on retry). Oversell means refunds, chargebacks, and trust loss during
the highest-revenue minutes of a sale.

## Solution (what the system does)

Tiered path: Redis Lua CAS pre-filter (single-RTT fast-fail) → RabbitMQ buffer
→ worker atomic persist (ack-after-persist) → guarded UPDATEs + idempotency
keys make retries safe; PG remains the truth, Redis is a mirror.

## Impact

| Metric | Before | After | How measured |
|---|---|---|---|
| Oversell (50 buyers / stock 10) | >0 (naive race) | 0 — exactly 10 sales, stock 0 | MEASURED + CI-ENFORCED: `concurrency-harness` job runs `load-tests/concurrency/oversell_demo.py` and fails unless output contains `RESULT: PASS`. Not an estimate. |
| p95 latency tiered vs naive | 574 ms naive | 29 ms tiered | Local single-run observation (`docs/benchmarks/phase4/phase5`), NOT a CI gate — ESTIMATE-grade until re-measured; re-measure before quoting. |
| Redis-Lua cost | 90% wasted DB round-trips to say "no" | single-RTT atomic pre-filter | Design claim; cost saving not metered in currency — treat as ESTIMATE pending load-billing data. |

No currency figure in this file is measured.

## Guardrails / SLO links

- Zero-oversell invariant (SLO `flashsale-zero-oversell`, error budget 0);
  `StaffOrAdmin` endpoint guard; ack-after-persist + DLQ; pool headroom.
- SLOs: `observability/slo.yaml`; benchmarks: `docs/benchmarks/phase2-005-*`.
- .NET harness not runnable here (no `dotnet-script`); documented, not re-implemented.

## Reproduce (existing harnesses — no new code; .NET needs dotnet-script, absent)

```bash
cd FlashSale-Backend
python3 load-tests/concurrency/oversell_demo.py   # expect RESULT: PASS (same as CI)
dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release        # 148 tests
dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj -c Release  # 60 tests (needs PG/Redis/RMQ)
```
