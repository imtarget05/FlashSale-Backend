# Failure / Recovery Matrix

Generated 2026-09-29 18:22 UTC.

Every row is an observed run, not an intention. `NOT_RUN` is kept as a row on purpose: deleting an unverified row is how a matrix starts lying.

| Failure injected | Scenario | Observed result | Status |
|---|---|---|---|
| FlashSale consumer crash | E2E-FS-04 | state untouched while unprocessed, then exactly one order | VERIFIED |
| FlashSale backup/restore | E2E-FS-05 | invariant PASS -> FAIL after corruption -> PASS after restore | VERIFIED |
| Kafka duplicate after restart | E2E-MFG-002 | one business effect, still one after consumer restart | VERIFIED |
| Contract break | E2E-MFG-003 | 4 violation shapes rejected before the consumer | VERIFIED |
| MAIA missing Qdrant | E2E-MAIA | /ready returns 200 with status=degraded and the reason | VERIFIED |
| MAIA no evidence | MAIA-01 | refusal, not an answer | VERIFIED |
| Apex PLC unreachable | APEX-03 | hardware mode refuses; simulation never writes to hardware | VERIFIED (simulated PLC) |
| CreditFlow missing API key | Phase 19 | 401, fail-closed | VERIFIED |
| Factory bad rows | FAC-01 | quarantined and counted | VERIFIED |
| Helpdesk unauthorised role | HD-04 | mutation refused | VERIFIED |
| MiniERP transaction rollback | ERP-03 | no runtime evidence | NOT_RUN |
| DB unavailable (any repo) | - | no runtime evidence | NOT_RUN |

## Observability across the three Azure apps

- **factory**: health 200 -> failure 404 -> recovery 200; 4 metric(s) moved
- **maia**: health 200 -> failure 401 -> recovery 200; 0 metric(s) moved
- **creditflow**: health 200 -> failure 401 -> recovery 200; 1 metric(s) moved

Known gaps: maia

> MAIA exposes no HTTP instrumentation: `api.py` never calls `metrics.inc`, and every counter it publishes belongs to ingestion, Kafka or the RAG path, none of which run in cloud-demo. Recorded as a gap rather than a pass.