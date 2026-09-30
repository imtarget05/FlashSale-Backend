# Architecture Evidence

**Scope rule for this document.** Every pattern below was verified by reading
the implementation and running the tests in this repository on 2026-09-30.
Anything not verified is marked `NOT VERIFIED` and is not claimed elsewhere.

Patterns deliberately absent: DDP, FSDP, model/tensor/pipeline sharding, service
mesh, Istio, per-ETL-step containers. Each was considered and rejected — none
solves a measured failure mode here. A rejected pattern is not a roadmap item.

**Test counts were produced in this session**, not copied from prior docs. Where
a prior number differed, the difference is stated.

---

## 1. Circuit breaker and retry — MAIA

| Field | Value |
|---|---|
| **Pattern** | Per-dependency circuit breaker (CLOSED / OPEN / HALF_OPEN) + bounded exponential-backoff retry |
| **Business problem** | A failing dependency (Qdrant, LLM gateway) must not consume request budget or hang the caller. A RAG assistant that hangs is worse than one that declines. |
| **Implementation** | `MAIA/src/maia/loops/resilience.py` |
| **Test / evidence** | `tests/test_resilience.py` + `tests/test_tracing.py` → **30 passed** |
| **Failure mode covered** | Retry storm to a dead dependency; unbounded latency; cascading timeout |
| **Trade-off** | In-memory state per process. At N replicas each keeps its own counter, so the effective threshold is N× the configured one. |
| **Limitation** | Stated in the module docstring: per-process state, needs a shared store before multi-replica. Not implemented. |
| **Status** | **VERIFIED** single-process. Multi-replica safety **NOT VERIFIED**. |

---

## 2. Transactional outbox — MAIA

| Field | Value |
|---|---|
| **Pattern** | Write business state and outgoing message in one transaction; a separate worker drains the outbox |
| **Business problem** | A notification must not be lost if the process dies between committing the business row and publishing |
| **Implementation** | `MAIA/src/maia/outbox_worker.py` (122 lines), `MAIA/src/maia/outbox_scheduler.py` (161 lines) |
| **Test / evidence** | `tests/test_e2e_it_outbox.py` → **2 passed** |
| **Failure mode covered** | Dual-write loss between DB commit and message publish |
| **Trade-off** | At-least-once delivery, so the consumer must dedupe. Chosen deliberately over exactly-once. |
| **Limitation** | Only 2 integration tests. Crash-during-drain durability is not exercised. |
| **Status** | **PARTIAL** — mechanism works; mid-drain crash durability **NOT VERIFIED**. |

---

## 3. Idempotency and dedupe — contracts and FlashSale

| Field | Value |
|---|---|
| **Pattern** | Consumer-side dedupe on a stable event id; producer-side idempotency keys with request-hash comparison |
| **Business problem** | At-least-once means the same event can arrive twice; a double-processed payment or stock decrement is a real financial defect |
| **Implementation** | `manufacturing-contracts/eventbus.py` (persistent dedupe), `registry.json` (per-stream key), `MiniERP/sql/08_traceability_plsql.sql` (`REQUEST_IDEMPOTENCY`, SHA-256 hash, raises `-20013` on mismatch) |
| **Test / evidence** | `manufacturing-contracts/tests/test_eventbus.py` → **15 passed, 1 skipped** |
| **Failure mode covered** | Duplicate delivery producing duplicate side effects; a *modified* payload replayed under a reused key |
| **Trade-off** | Persistent dedupe costs a write per event, chosen over an in-process set that dies with the process and reprocesses everything on restart. |
| **Limitation** | FlashSale's exactly-once claim rests on `IX_Orders_IdempotencyKey`; **not re-run this session**. |
| **Status** | **VERIFIED** for the contracts layer. |


## 4. Azure revision rollout and rollback

| Field | Value |
|---|---|
| **Pattern** | Container Apps revision-per-deploy; a failed candidate revision receives no traffic while the last healthy revision keeps serving |
| **Business problem** | A bad config or image must not take down a running service |
| **Implementation** | `tools/e2e/gate5_revision_rollback.py`; `docs/evidence/e2e/final/gate5-revision-rollback.json` |
| **Test / evidence** | **REAL, executed on Azure** (Consumption, eastasia), **80.09 s**: `az containerapp update` with a bad config, then re-activate the prior revision |
| **Failure mode covered** | A deployment regression reaching end users |
| **Trade-off** | Revision-per-deploy costs extra revision storage and relies on Azure routing to keep the failed revision out of the traffic split |
| **Limitation** | The bad deploy is a **SIMULATED** config fault, not a genuine code regression. Single app (CreditFlow). Not classic blue/green with weighted traffic splitting. |
| **Status** | **VERIFIED** on real infrastructure for the mechanism described |

---

## 5. OpenTelemetry tracing — MAIA (in-process)

| Field | Value |
|---|---|
| **Pattern** | Spans emitted in-process and exported over OTLP |
| **Business problem** | A 7-stage RAG pipeline is undebuggable without per-stage timing |
| **Implementation** | `MAIA/src/maia/tracing.py`, gated by `PIPELINE_TRACE` / `OTEL_ENABLED` in `config.py` |
| **Test / evidence** | Part of the 30 passed above; full suite **929 passed, 3 skipped** |
| **Failure mode covered** | Unattributable latency regressions across pipeline stages |
| **Trade-off** | NoOp when disabled, keeping the test suite hermetic and collector-free |
| **Limitation** | **In-process instrumentation — not a sidecar, not a service mesh.** No collector, no Envoy proxy, no mesh. Live collector export **NOT VERIFIED** this session. |
| **Status** | **VERIFIED** as code + tests. Live export **NOT VERIFIED**. |

---

## 6. Medallion data pipeline — Factory

| Field | Value |
|---|---|

## 7. Edge-oriented visual inspection — ApexInspect

| Field | Value |
|---|---|
| **Pattern** | Local inference with frame-quality gating, trigger logic, PLC boundary, tamper-evident audit |
| **Business problem** | A rejected inspection must be recorded as *unverifiable*, not as a pass. Industrial inspection must fail loudly rather than guess. |
| **Implementation** | `src/vision/{detector,quality,patching,stream}.py` (ONNX Runtime, `resolve_model_path()`, degraded-mode flag); PLC bridge in `src/backend/service.py` |
| **Test / evidence** | Full suite **211 passed, 1 skipped** |
| **Failure mode covered** | A corrupted/blank frame read as a clean frame (`len(None)` crash and false-clean aggregation, fixed at `6e0475c`) |
| **Trade-off** | Quality gating trades recall for trustworthy verdicts: a frame failing QC yields no verdict rather than a guess |
| **Limitation** | **Detection accuracy is NOT measured on a labeled validation set.** |
| **Status** | Runtime behaviour **VERIFIED**. Model quality **NOT MEASURED**. |

### Precise public wording

> Runtime-verified visual inspection pipeline using ONNX Runtime, frame-quality
> gating, trigger logic, PLC integration and tamper-evident auditing, with
> measured inference latency and explicit no-verdict handling for unusable
> frames. Detection accuracy has not been measured on a labeled validation
> dataset.

### Claims that are NOT supported

- "reduces latency X% versus cloud inference" — no cloud-vs-edge benchmark exists
- "reduces bandwidth X%" — no bandwidth measurement exists
- "production-grade accuracy" — not measured

The architecture genuinely runs local inference. That is a fact about the
design. It is **not** evidence that edge deployment is *better* than cloud here,
because no comparison was run.

### One nuance on accuracy — partial evidence exists, and it is unflattering

`models/MODEL_CARD.md` records a real QC observation from the Kaggle
`akhatova/pcb-defects` dataset:

> QC trên 15 ứng viên board: 2 bị loại (13%) do model bỏ sót/báo nhầm. Con số này
> là recall thực, không được che đi.

A **13% miss rate on a 15-board sample** has been observed and deliberately not
hidden. 15 boards cannot support precision/recall/F1/mAP claims, but this is
stronger than a flat "not measured", and it points the wrong way. Recorded here
so it cannot be quietly dropped when a real evaluation is eventually run.

---

## Test verification log (2026-09-30)

| Repo | Command | Result |
|---|---|---|
| ApexInspect-AI | `pytest tests/` | **211 passed, 1 skipped** |
| Factory | `pytest tests/` | **231 passed, 5 skipped** |
| MAIA | `pytest tests/` | **929 passed, 3 skipped** |
| MAIA resilience+tracing | `pytest tests/test_resilience.py tests/test_tracing.py` | **30 passed** |
| MAIA outbox | `pytest tests/test_e2e_it_outbox.py` | **2 passed** |
| manufacturing-contracts | `pytest tests/test_eventbus.py` | **15 passed, 1 skipped** |

Discrepancies against previously recorded evidence, stated rather than hidden:

- Apex **211** here vs **210** in the checkpoint. The extra test runs because this
  environment has `onnxruntime` installed, exercising a path skipped where the
  engine is absent.
- MAIA **929** here vs **842** in `docs/evidence/e2e/maia/`. The gap is
  environmental; the suite had not been re-baselined until this run.

Both are recorded rather than harmonised, because a test count that only matches
the previous document is a test count nobody checked.

| **Pattern** | RAW → SILVER → GOLD → MART with lineage and quarantine |
| **Business problem** | Bad source data must be quarantined per row without blocking the batch, and must never reach KPI computation |
| **Implementation** | `app/etl/{medallion,gold,pipeline,quarantine,kpi_engine,polars_etl}.py`; `app/data_contracts/{validator,schemas}.py` |
| **Test / evidence** | Full suite **231 passed, 5 skipped** (matches checkpoint) |
| **Failure mode covered** | NaN/±Inf contaminating KPIs; a validation *infrastructure* failure silently degrading into a data-acceptance pass (**fail-open**, fixed at `483a648`) |
| **Trade-off** | Row-level fail-soft ingestion plus fail-closed validation. Both intentional, both now explicit. |
| **Limitation** | Airflow and Spark are **not** used; orchestration is plain Python, processing via Polars. |
| **Status** | **VERIFIED** |


