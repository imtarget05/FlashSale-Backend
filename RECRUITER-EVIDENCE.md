# Recruiter Evidence — OpsBridge (.NET 10 Flash-Sale + OpsBridge)

> **Current checkpoint (2026-10-21):** This short form contains historical
> counts and CI wording below. The old harness could print `RESULT: PASS` after
> a dry-run resync with only 3/50 or 0/50 sales. The uncommitted fix now gates
> exact 10/40 fill and passed one disposable local run; remote CI has not been
> rerun. See [the commit-pinned audit](docs/evidence/portfolio-audit-2026-10-21.md).

> **Repository note.** This file lives in `imtarget05/OpsBridge`, which currently
> **shares its full commit history** with `imtarget05/FlashSale-Backend` — the
> FlashSale order-reliability context and the OpsBridge event-driven operations
> context are both in this tree **by design** (ADR-017 bounded contexts,
> ADR-018 migration and legacy baseline). The **FlashSale-only** lineage stays at
> <https://github.com/imtarget05/FlashSale-Backend>; the OpsBridge lineage
> continues here. The long-form audit with exact file/line citations is
> [`docs/RECRUITER-EVIDENCE.md`](docs/RECRUITER-EVIDENCE.md), and the
> repository-wide evidence index is GitHub issue #1.

Problem: flash-sale oversell + double-charge khi 50 req đua 10 suất hàng.
Architecture: Clean Arch `Domain <- Application <- Infrastructure <- Api/Worker` (`AzureFlashSale.slnx`); Postgres source-of-truth + Redis Lua CAS pre-filter + RabbitMQ/ServiceBus queue + Outbox/Inbox + Checkout Saga bù trừ.
OpsBridge (same tree, own bounded context): `OpsBridge.slnx` — ingestion, procurement snapshots, reconciliation (allocations/discrepancies), staff review, DLQ replay audit; it never writes FlashSale order state directly (ADR-017).
Evidence:
- `src/Order.Api/Program.cs` — 25 Minimal API routes, `/health/live|ready`, `/metrics` Prometheus
- `src/FlashSale.Infrastructure/Redis/RedisStockGateway.cs` — Lua `ReserveScript`
- `src/FlashSale.Application/Orders/SubmitOrderUseCase.cs` — flow Redis→enqueue→fallback
- `tests/UnitTests/` 22 files + `tests/IntegrationTests/` Testcontainers + `load-tests/k6/flash-sale.js` + `load-tests/concurrency/oversell_demo.py` (RESULT:PASS)
- `observability/` Prometheus/Grafana/SLO + OTEL tracing + K8s `infrastructure/kubernetes/`
- `.github/workflows/ci.yml` — build, unit, integration, concurrency-harness, Trivy, ACR push
- OpsBridge: `tests/OpsBridge.UnitTests|ContractTests|IntegrationTests` — 41 / 7 / 22 recorded in `docs/evidence/ci-verification.md` (commit `1ecd302`)
Demo: `docker compose up` → `k6 run load-tests/k6/flash-sale.js` → Grafana `project-flashsale.json`, oversell = 0.

---

## Verified / Not verified

**Verified** (each row points at an artefact a reviewer can open):

| Claim | Evidence |
|---|---|
| Zero-oversell invariant — 50 concurrent buyers / 10 units → 10 orders, 40 × 409, final stock 0 — is **enforced on every push** | `.github/workflows/ci.yml` → job `concurrency-harness` fails unless the harness prints `RESULT: PASS` |
| FlashSale unit + integration suites green (counts as measured in the audit above) | `docs/RECRUITER-EVIDENCE.md` (the audit that ran them) |
| OpsBridge unit / contract / integration suites green | `docs/evidence/ci-verification.md` (commit `1ecd302`: 41 / 7 / 22 = 70 / 70) |
| Business-path events are written through a **transactional outbox**, not a best-effort publish | `docs/adr/019-transactional-outbox.md`, `docs/findings/outbox-gap.md` |
| Clean-Architecture dependency direction is an **executable** rule, not a convention | `tests/UnitTests/ArchitectureTests.cs` |
| Local docker compose stack healthy (6 containers, 2026-10-21) | `docs/known-issues.md` |
| Outbox/Inbox live behaviour (broker outage → DLQ → replay, 5× duplicate → 1 inbox row), saga 21/21, 3 backup/restore drills | `docs/evidence/v2/outbox-inbox-2026-09-24.md`, `docs/evidence/saga/`, `docs/evidence/backup/` |
| Optimistic-concurrency `Version` token — concurrent updates with conflicting versions throw `DbUpdateConcurrencyException`, zero lost updates | `tests/UnitTests/CheckoutSagaConcurrencyTests.cs`, `tests/IntegrationTests/CheckoutSagaConcurrencyTests.cs` (PostgreSQL Testcontainers CI verified) |
| Outbox orphan reservation compensation & reconciler repair | `tests/UnitTests/SubmitOrderCompensationFailureTests.cs` (`WhenQueueRejectsAndCompensationOutboxFails_RedisIsReleased_AndReconcilerCanRepair`) |

**Not verified — do not claim these**:

| Claim | Status |
|---|---|
| A real payment service provider | **Never exercised.** The local payment service is a deterministic simulation (`sim_tx_…`); no PSP contract, credential or external behavior exists in this repository (ADR-017). |
| The Kubernetes `prod` overlay as a running system | **Configuration only** — never applied to a real AKS cluster (`docs/adr/020`, `docs/evidence/kubernetes/phase7c-readiness.md`). |
| Latency numbers (p95 29 ms / 574 ms) | **Local single runs, not CI gates.** Re-measure before quoting; only the zero-oversell invariant is CI-enforced. |
| Production database shape | **None.** The schema is verified *from empty* against local PostgreSQL — no production dataset, volume or query-plan evidence. |
| Pod-level liveness/readiness for the worker | **Absent by decision** — container-level healthcheck only (`docs/known-issues.md`, `infrastructure/kubernetes/base/worker/deployment.yaml`). Worker has no inbound HTTP listener. |
| `/api/v1/auth/login` as authentication | **A local demo persona selector**, mapped only in Development/Test; no external identity provider (ADR-017). |

> This is an **index of evidence, not a production-readiness claim**. Nothing in
> this repository is production-ready, and no statement above should be
> presented as one.

---

## FlashSale-Backend (Java payment service) — portfolio evidence

> **Scope:** `src/payment-service-java` (Spring Boot 4.0.9, Java 17). Backend-only portfolio surface; `frontend/` is a thin demo shell.

**Problem:** Xử lý thanh toán flash sale với idempotency key, tránh double-charge khi yêu cầu trùng, mô phỏng kết quả thành công / từ chối / hết thời gian cổng thanh toán.

**Architecture:** Ports & Adapters — `PaymentProvider` port isolates PSP integration; `SimulatedPaymentProvider` is deterministic local demo (not a real PSP). PostgreSQL source of truth with `ON CONFLICT DO NOTHING` idempotency; Redis cache; RabbitMQ transactional outbox + DLQ; JWT auth with OIDC fail-closed production profile.

**Key files:**
- `src/main/java/com/flashsale/payment/provider/PaymentProvider.java` — PSP port interface
- `src/main/java/com/flashsale/payment/provider/SimulatedPaymentProvider.java` — local simulation adapter
- `src/main/java/com/flashsale/payment/service/PaymentService.java` — idempotent payment processing, outbox
- `src/main/java/com/flashsale/payment/repository/PaymentRepository.java` — `insertIfAbsent` with `ON CONFLICT DO NOTHING`
- `src/main/java/com/flashsale/payment/security/SecurityConfig.java` + `ProductionSecurityConfig.java` — role-based auth, OIDC fail-closed
- `src/main/java/com/flashsale/payment/outbox/PaymentOutboxRelay.java` — scheduled outbox publisher with retry/backoff
- `src/main/java/com/flashsale/payment/event/OrderEventConsumer.java` — RabbitMQ consumer with manual ACK
- `src/test/java/com/flashsale/payment/unit/PaymentServiceTest.java` — unit tests (Mockito)
- `src/test/java/com/flashsale/payment/integration/PaymentServiceIntegrationTest.java` — Testcontainers PG/RabbitMQ/Redis/Mongo
- `src/test/java/com/flashsale/payment/security/ProductionOidcConfigTest.java` — OIDC issuer/audience validation
- `.github/workflows/ci.yml` — Java CI (compile, unit, integration, Docker build, k8s validation)
- `scripts/ci-gate.sh` — local CI gate (no cloud credentials)
- `k8s/overlays/{local,production}` — kustomize overlays; `scripts/render-manifests.sh`

**Verified / Not verified:**

| Claim | Evidence |
|-------|----------|
| Idempotent payment — same key + same payload → same result, one DB row, one outbox event | `PaymentServiceIntegrationTest.concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed`, `processPayment_idempotentReplay_returnsSameResult` |
| Idempotency conflict — same key + different payload → HTTP 409 | `PaymentServiceIntegrationTest.concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment` |
| Declined / timeout simulation — .02 → Declined, .03 → 504 TimedOut | `PaymentServiceIntegrationTest.processPayment_declined_whenCentsIsTwo`, `processPayment_transientTimeout_whenCentsIsThree` |
| OIDC fail-closed — production profile rejects missing/invalid issuer or audience | `ProductionOidcConfigTest` |
| JWT validation — expired/tampered/missing token → 401 | `AuthSecurityIntegrationTest` |
| Role-based access — CUSTOMER cannot POST /api/payments (403), STAFF/ADMIN can (200) | `AuthSecurityIntegrationTest.customerRolePostPayment_returns403`, `staffCanPostPayment_returns200` |
| Transactional outbox — events persisted to `payment_outbox`, published via RabbitMQ with confirms | `PaymentOutboxRelay`, `PaymentEventPublisher`, `PaymentServiceIntegrationTest.validOrderEvent_persistsPaymentAndPublishesCompletion` |
| DLQ — malformed order events routed to `payment.dlq` | `PaymentServiceIntegrationTest.malformedOrderEvent_isDeadLettered` |
| Local CI gate — compile + unit tests + Docker build + k8s YAML validation | `scripts/ci-gate.sh` |

**Not verified — do not claim these:**
| Claim | Status |
|-------|--------|
| A real payment service provider | **Never exercised.** `SimulatedPaymentProvider` is deterministic local demo only. |
| Production OIDC identity provider | **Not integrated.** `ProductionSecurityConfig` requires `OIDC_ISSUER_URI` / `OIDC_AUDIENCE` but no real IdP is configured. |
| Live Render / AKS deployment | **Configuration only.** k8s manifests exist; never applied to a real cluster. |
| Production-scale data | **None.** Schema verified from empty against local PostgreSQL only. |

