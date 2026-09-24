# Current Tasks — ACTIVE GOAL (2026-09-23, LOCAL v2 Platform Evolution)

> **⛔ NOTICE TO ALL AGENTS (2026-09-24): plan V2 là đủ — KHÔNG cần AI/LLM.**
> Scope Ollama/qwen3/AI-assistant/content-generation đã DROP. Không chạy/không fix smoke AI.
> Chi tiết: `plans/plan-20260923-2349-local-v2-platform.md`.
> Focus: kind + payment-service CrashLoop + saga smoke + Kafka/observability/KEDA.

> Focus plan: `plans/plan-20260923-2349-local-v2-platform.md` (Active). Trước đó: cline-helper plan DONE (3B/3C ticked).
> V2 scope: Payment.Service + Checkout.Saga (V2.1 DONE) → Kafka KRaft + Outbox/Inbox (V2.2 DONE) → Tempo+Loki (V2.3 DONE) → Envoy + KEDA (V2.4 DONE) → demo + evidence (V2.5 DONE). Budget: Docker VM 7.75 GiB.
> Status 2026-09-24: V2.1-V2.5 PASS (saga 21/21, Kafka-down no-loss, trace Tempo, KEDA ScalingActive, demo verified). `tasks/current.md` synced với `plans/plan-20260923-2349-local-v2-platform.md`. Không còn Next V2.1→V2.2.

# Prev Goal (2026-09-23, Cline-helper / Antigravity finish — DONE)

> Vai trò: làm phụ cho Cline — Cline agents read-only trừ khi được giao task (per plans/3repo-roadmap-phase4-11.md §Approved).
> Focus plan: `plans/plan-20260923-2239-cline-helper-antigravity-finish.md` (Active).
> Harness state: `.ai/harness/handoff/resume.md` MISSING (repo chưa opt-in `workflow-contract.json`); `tasks/current.md` là source of truth.
> Next: (1) khép tracking gap 3B/3C bên dưới; (2) mở Phase 4 P02 audit gate trong Productionized-LegacyApp; (3) sau gate → Phase 5 DevSecOps → Phase 6 ACR → Phase 7 AKS (P03).

# Current Tasks (Phase 6–9 — containers, IaC, CI/CD)

## Business Automation Platform (spec: 01-FlashSale-Automation)

Phases per the automation spec; each phase is only ticked with evidence.

- [x] Phase 0 — audit: event-ready order path confirmed; gaps = no event model,
      no payment lifecycle, no audit table, no scheduler, no dashboard.
- [x] Phase 1 — event model standardization (commit 25659e5, CI 35807116862):
      8 typed events (order.created, inventory.reserved, payment.completed,
      payment.failed, payment.expired, order.confirmed, order.cancelled,
      inventory.released) with eventId/eventType/occurredAt/correlationId/source;
      RabbitMQ topic exchange `automation.events`; audit-first
      AutomationWorkerHost; `AutomationRuns` table + EF migrations; wired into
      API + Worker roots (provider gate = ADR-005 RabbitMQ-only).
- [x] Phase 2 — order + payment automation (§4/§5), verified locally:
      - publish order.created (worker, real order id) + inventory.reserved (API);
      - orders persist as PendingPayment with PaymentDueAt = accept + TimeoutMinutes
        (messages without a window keep the OLD terminal semantics → v1.0 flow
        and the legacy `processing|completed` wording are untouched);
      - PaymentTimeoutRule (pure) + scan use case: reminder inside grace
        (PaymentReminderCount ≤ MaxPaymentReminders), past grace → guarded
        cancel + DB stock release (same transaction) + Redis mirror;
      - POST /api/orders/{key}/pay (simulated gateway, status-guarded exactly
        once) and POST /internal/automation/payment-timeout-scan (manual trigger,
        TriggerType distinguishes timer vs manual in the audit row).
      - Config (never hard-coded): Automation:Payment:{TimeoutMinutes=15,
        GracePeriodMinutes=15, MaxPaymentReminders=3, ScanIntervalSeconds=60}.
      - Tests: 82 unit + 24 integration green (was 75+19); live smoke 21/21
        (reminder → cancel → stock 4→3, audit rows PaymentTimeout|manual|Success,
        OrderProcessing|api|Success); v1.0 auth/order smoke still 48/48.
      - KNOWN LIMITATIONS: event publish is best-effort (no outbox) — bus outage
        loses events but never the order/audit row; reminder "notification" is
        structured log + audit (email/webhook PLANNED); payment gateway SIMULATED
        (no real processor — never claim otherwise); timeout scan runs in
        Order.Worker + manual endpoint (not in the API timer).
- [x] Phase 3 — inventory low-stock automation (§6), verified locally:
      - Product.ReorderThreshold (per-product, 0 = use platform default) with
        DB default 5; pure LowStockRule (inclusive boundary, unit-tested);
      - LowStockAlertUseCase scans REAL stock values (SQL + rule re-check),
        creates deduplicated `StockAlerts` rows (partial unique index on
        ProductId WHERE Status='Open'), publishes inventory.low_stock, writes
        one InventoryAutomation audit run per scan;
      - timer LowStockScanHostedService (worker) + manual
        POST /internal/automation/low-stock-scan (trigger_type proves which);
      - Config: Automation:Inventory:{DefaultReorderThreshold=5, ScanIntervalSeconds=60}.
      - Tests: 89 unit + 28 integration green; live smoke 11/11 (healthy → 0,
        stock 5 <= threshold 5 → alert + log + audit, rescan → deduped).
      - LIMITATIONS: notification is structured log + audit row (dashboard/email
        PLANNED, spec §6 "notify dashboard/email"); alert resolution/ack flow
        lands with the dashboard phase; AI recommendation inputs (velocity, lead
        time) NOT implemented — and stock values never come from AI.
- [x] Phase 4 — daily business report (§7), verified locally:
      - DailyReportUseCase aggregates ONLY from PostgreSQL (orders by status,
        revenue on confirmed orders at the stored flash-sale price, failed
        payments, cancellations, top products, low-stock list) and persists
        one row per UTC day (unique ReportDate ⇒ rerun = upsert);
      - ReportWindow helper is UTC-pinned and unit-tested (locale-proof);
      - DailyReportHostedService fires at Automation:Reporting:RunAtHourUtc
        (default 0 = midnight UTC) + manual POST /internal/automation/daily-report
        and GET /internal/automation/daily-report/latest;
      - Config: Automation:Reporting:{RunAtHourUtc=0, ScanIntervalSeconds=300,
        TopProductCount=5}.
      - Tests: 91 unit + 31 integration green; live smoke 12/12 (report from DB:
        orders=11 confirmed=3 cancelled=6, low-stock list correct, upsert single
        row, audit DailyReport|manual|Success).
      - LIMITATIONS: AI summary of the metrics NOT implemented (spec §7 marks it
        optional) — PLANNED; refundCount is always 0 because no refund workflow
        exists (documented, never faked); local revenue reads 0 because the
        seeded product price is 0 in this DB (the integration test asserts the
        150/75 math with a set price).
- [x] Phase 5 — AI content generation + support triage (§8/§9) — DONE 2026-09-23:
      approval state machine (REVIEW_REQUIRED→APPROVED/REJECTED→PUBLISHED,
      publish-before-approve = 409, human approval mandatory §13), grounded
      support triage (facts from PostgreSQL; REFUND ⇒ human review; model never
      invents order status §9). Real qwen3:4b smoke
      scripts/content-support-smoke.sh = **24/24 PASS** (generate216s wall =
      attempt-1 timeout 120s → attempt-2 success, §12 bounded retry; approve/
      publish → Products.Description updated only after human approval).
      Fixed en route: HttpClient.Timeout 150s (use case owns the budget),
      UTC-day boundary in dashboard query (`Kind=Unspecified` local-offset →
      Npgsql 42P10), stock top-up before grounding-order step.
      Evidence: docs/evidence/automation-01/phase5-content-support-triage.md
- [x] Phase 6 — automation dashboard endpoint (§14) — DONE 2026-09-23:
      GET /internal/automation/summary → {runsToday:76, successful:71, failed:4,
      retrying:0, manualReview:0, averageDurationSeconds:49.24,
      topFailingWorkflow:"AiContentGeneration"} (live, UTC-day fixed);
      GET /internal/automation/alerts → open StockAlerts (live, HTTP 200).
      Evidence: docs/evidence/automation-01/phase6-automation-dashboard.md
- [x] Phase 7 — demo scenarios B/C/D + E2E script (§17) — DONE 2026-09-23:
      reproducible scripts committed under scripts/ (payment-automation-smoke =
      §16 E2E timeout→remind→cancel→release→audit; lowstock-alert-smoke;
      daily-report-smoke; content-support-smoke = scenario D + §9 triage;
      ai-assistant-smoke = grounded assistant) + runbook
      docs/interview-demo/automation.md. Regression on final build:
      **134 unit + 40 integration = 174 PASS, build clean, all four smokes green
      against live PostgreSQL/Redis/RabbitMQ (+ Ollama qwen3:4b for AI paths).**

## INTERVIEW RELEASE v1.0 — FREEZE (2026-09-22)

Status block for the interview release checkpoint:

Architecture
- [x] .NET 10 retained — no NestJS rewrite.

Backend
- [x] async order flow (ADR-004/005): API → Redis reservation → RabbitMQ → worker.
- [x] PostgreSQL source of truth + EF migrations.
- [x] Redis Lua CAS reservation (24h idempotency-ledger TTL).
- [x] RabbitMQ worker + idempotent consumer + DLQ (structured stdout log).
- [x] idempotency keys end-to-end.
- [x] concurrency proof: oversell gate 10 accepted / 40 rejected / stock 0.

Auth (merged interview-release/auth)
- [x] JWT access + refresh, rotation, authorization policies.
- [x] authenticated orders with userId threading + GET /orders/me.
- [x] backward-compatible anonymous POST /api/orders.

API
- [x] OpenAPI contract at /openapi/v1.json (Bearer scheme transformer).
- [x] Swagger UI at /swagger (serves the SAME document; Authorize button works).
- [x] Product read API: GET /api/products/{id} (full search deferred — not in v1.0).
- [x] diagnostics: GET /internal/metrics (in-process snapshot).

Quality
- [x] 77/77 local tests (58 unit + 19 integration) on the merge tree.
- [x] post-merge remote CI green — runs 35779230807 (auth merge) + 35780242190
      (release HEAD 39f0bbd, Swagger UI), both FlashSale CI success.
- [x] interview smoke PASS — 48/48 gates, 0 fail (auth_e2e.sh, 2026-09-22):
      openapi Bearer scheme + bearerFormat, /swagger 200 + bundle, register/
      duplicate/short-password, login + non-enumeration, /auth/me, refresh
      rotation + replay 401, access-as-refresh 401, authenticated 202 →
      Completed, GET /orders/me scoped + 401 anonymous, anonymous 202 (ADR-013
      §6), logout 204 + refresh 401, metrics histogram/gauge/route tags.
- [x] tag v1.0-interview pushed as annotated tag `v1.0.0-interview` on this
      CI-green commit; freeze = no feature work on v1.0 until after tag.

AI v2 — spec only, implementation DEFERRED (do NOT claim "Integrated
Qwen/Ollama" in a CV until the backend actually calls the model):
- [x] architecture selected: Ollama + Qwen3:4b (~4B params, Q4_K_M ≈ 2.5 GB)
      via OpenAI-compatible endpoint http://localhost:11434/v1 (client sends a
      dummy key such as `ollama`; the server does not validate it).
- [x] runtime integration (first real inference evidence) — 2026-09-23:
      POST /api/assistant/product → 200 from REAL qwen3:4b (Ollama /v1,
      dummy Bearer key), measured latency 18–70s (qwen3 reasons before
      answering; `think`/`reasoning_effort` are NOT honored by Ollama's
      OpenAI endpoint — per-attempt timeout set to 120s accordingly),
      usage tokens real (e.g. prompt 141 / completion 724). Live smoke
      scripts/ai-assistant-smoke.sh = 19/19 PASS (401 anonymous, 400 empty
      question, real grounded answer with productId resolving via
      /api/products/{id}, Redis rate-limit window, deterministic 429 without
      model call, metrics flashsale.ai.assistant.*, OpenAPI Assistant tag).
      Unit 75 + integration 19 = 94/94. Evidence:
      docs/evidence/ai/assistant-live-smoke.md. CV claim is now honest:
      "Integrated Ollama (Qwen3:4b) grounded Product Assistant (REST)".
- [x] grounded assistant — model output validated against read-model
      candidates; invented/malformed/duplicate productIds are DROPPED and
      counted (never invented), code fences stripped, sloppy array entries
      tolerated — unit-tested in AssistantOutputParserTests.
- [ ] TTS (ElevenLabs — needs a paid API key; not started).

Phase 1–5 (done, evidence kept for traceability):
- [x] Phase 1: baseline API verified.
- [x] Phase 2: overselling reproduced (50/50 accepted, stock 10→9) — docs/benchmarks/phase2-oversell-experiment.md
- [x] Phase 3: atomic conditional UPDATE fix (10 accepted / 40 rejected / stock 0) — phase3-atomic-update-experiment.md
- [x] Phase 4: high-load evidence (p95 574 ms, pool-headroom incident) → ADR-003
- [x] Phase 5: Redis reservation + async idempotent worker (p95 29 ms, 202/409-dup verified) → ADR-004

Current:
- [x] Task 1: messaging provider strategy (ADR-005) — `IOrderQueue` →
      InMemory/RabbitMQ/ServiceBus; `AddOrderQueue` in Api+Worker; 14 tests green.
- [x] Task 2: EF migrations + Testcontainers integration (ADR-006) —
      `InitialCreate`, `MigrateAsync` startup, 10 integration tests against real
      Postgres/Redis/RabbitMQ; invalid-quantity fix; `terraform validate` green.
- [x] Task 3: container hardening (ADR-007) — non-root `app` (uid 1654),
      `.dockerignore`, HealthProbe, /health/live+/health/ready, compose health
      ordering, Kafka experimental profile, dlq-data volume, runbook verified.
- [x] Phase 3A: DB backup + clean restore drill — pg_dump -Fc (5,198B, sha256
      OK), pg_restore into NEW `FlashSaleRestoreDrill`, data/index/migrations
      verified, app smoke green on restored DB (202→completed, stock 98→97),
      original DB untouched, measured restore ~1s, RPO manual-only (target not
      guaranteed). Evidence: docs/evidence/backup/restore-drill-001.md.
- [x] Phase 3B: off-site backup + restore — upload Entra-only lên
      `stflashsalebackup/postgres-backups`, quarantine local (coi như mất),
      download + checksum pass, restore vào `FlashSaleRestore3BFinal`
      (stock 99→98, download 10s / restore 5s / tổng ~20s); hardening firewall
      Deny + IP allowlist, lifecycle 30d/90d, Content-MD5 + SHA-256 round-trip.
      Evidence: docs/evidence/backup/restore-drill-002-offsite.md.
- [x] Phase 3C: integrated recovery — runbook docs/runbooks/disaster-recovery.md
      end-to-end (RabbitMQ definitions backup 789B → wipe → rebuild + order thật;
      Redis FLUSHDB → resync từ PG truth; TF state migrate remote + locking proof;
      `run-full-recovery.sh` DOWNLOAD 6s / RESTORE 0s / VERIFY 1s).
      Evidence: docs/evidence/backup/restore-drill-003-integrated.md.
- [x] Docker Compose: order-worker service + healthchecks + depends_on conditions.
- [x] Dockerfile.worker for the standalone worker image.
- [x] Terraform **blueprint** for the full Azure stack (ACR, PostgreSQL, Redis,
      Service Bus, Container Apps, Log Analytics) + dev/prod tfvars;
      `terraform validate` green.
      **NOT APPLIED — no such resources exist in the subscription.** Verified live
      2026-09-21 (`az resource list`): the only P01-owned Azure resources are
      `acrflashsalep6` + `id-flashsale-github-release` (+ the Phase 3B backup
      storage account), all created by `infrastructure/terraform/release/` and
      `.../backup-storage/`. `infrastructure/terraform/main.tf` (the app stack)
      has never been applied and has no state file.
      ACTIVE cloud ownership: **P03 `AKS-SRE-Platform` owns shared Azure runtime
      infrastructure** (resource group, AKS, ACR integration). P01 owns
      application code, images and workload manifests.
- [x] CI: build + unit/architecture tests + concurrency harness + Trivy gates;
      CD: **ACR push of immutable SHA tags + GitOps overlay pin** (ADR-011).
      There is **no Container Apps deployment** — no such workflow or job exists;
      the compute target is AKS via GitOps (Phase 7C), which supersedes the
      original Container Apps plan in AGENTS.md.
- [x] Interview notes for Phase 2–5 decisions (docs/interview-notes/).
- [x] **Clean Architecture refactor** (folder-level): Domain / Application (ports) /
      Infrastructure (adapters) / Api + Worker composition roots; 7 tests incl.
      dependency-rule guards. See AGENTS.md "CLEAN ARCHITECTURE RULE".

## P01 OFFLINE READY FOR 7C (2026-09-22, commit 48d2300 + docs 176167e/a798b97)

Offline gates ALL GREEN (all runnable without a cluster):

- [x] Fail-fast messaging (ADR-005 corrected): no silent InMemory fallback;
      `MessagingConfigurationException`; explicit `InMemory` refused in
      Production environment. 32/32 unit tests incl. DI-level registration
      guards (`MessagingRegistrationGuardTests`).
- [x] Offline validators: `validate-manifests.sh` (rendered-overlay gate),
      `validate-objects.py` (no-kubeconfig cross-reference),
      `test-validate-manifests.py` (19/19 mutations caught — every assertion
      proven able to fail), `capacity-inventory.py` (per-namespace request
      table: flash-sale-prod = 1150m / 2.25Gi replica-scaled).
- [x] Migration contract: `MigrationRunner` + `--migrate` entrypoint (Postgres
      only) + `base/migration/job.yaml`. DESIGN ONLY — Job not applied.
- [x] CI: `manifest-validation` job added to ci.yml (runs the same offline
      gate on GitHub runners).

LIVE GATES STILL PENDING (these block calling Phase 7C "done"):

- [x] Push to origin + GitHub Actions run green (incl. manifest-validation).
      EVIDENCE (run 35738200956, commit 65221cc): ci-gate success;
      concurrency-harness success (fix-ci commit finally opted in
      Messaging__Provider=InMemory + hardened health wait);
      manifest-validation success (10s, rendered overlay deployable);
      release-push + gitops-update success. One prior FAIL was the harness
      job (35737398203: API died on MessagingConfigurationException because the
      old code relied on silent InMemory; failure surfaced misleadingly as
      'relation "Products" does not exist' — root cause documented in the
      concurrency-harness step comment). mutation/negative suite remains
      LOCAL-only (19/19 caught, scripts/test-validate-manifests.py) — CI runs
      the gate, not the meta-tests. Rendered SHAs at push:
      order-api + order-worker = 087731cd0ed6d2aeb5892dd3a8fd6f6da7c0339c
      (ACR_LOGIN_SERVER repo var confirmed = acrflashsalep6.azurecr.io).
- [ ] Server-side dry-run against the real AKS cluster (client render alone
      cannot see admission webhooks / quota).
- [ ] Pods Ready (API x2 + worker + migration Job Completed).
- [ ] 202 → Completed E2E: POST /api/orders returns 202 AND the order actually
      completes via RabbitMQ + worker (the original 7C silent-InMemory bug).
- [ ] Real PostgreSQL/RabbitMQ/Redis runtime evidence (not offline mocks).

Next (Phase 10 + hardening):
- [ ] Wire OpenTelemetry → Azure Monitor (App Insights) exporter.
- [ ] Key Vault CSI driver instead of Kubernetes secrets.
- [ ] Chaos experiment: kill the worker mid-drain to demonstrate at-least-once redelivery.

Roadmap change 2026-09-21 (mandatory, sequenced AFTER AKS/GitOps/observability baseline):
- [ ] Phase 9 decomposition target (monorepo, separate runtimes): Order / Inventory /
      Payment (`FakePaymentProvider`, deterministic `...01/02/03` fixtures) /
      Checkout.Saga / Fulfillment.Worker; one PG instance, per-service schemas,
      no cross-service table reads. Full spec:
      `../AKS-SRE-Platform/plans/2026-09-21-microservices-mandatory-roadmap-9-to-18.md`.
      NOT STARTED — opens only after platform Phase 8 green.

*Ownership boundary (clarified 2026-09-21, replaces the old blanket "do not work
on Terraform/Azure" freeze which contradicted the Phase 7 roadmap):
Do **not** provision P01-owned standalone Azure runtime infrastructure — no
PostgreSQL Flexible Server, Redis Cache, Service Bus namespace or Container Apps
environment from this repo. P03 `AKS-SRE-Platform` owns the shared Azure/AKS
runtime (resource group, cluster, ACR integration, gateway, GitOps).
P01 **does** own: application code, container images, Kubernetes manifests,
overlay pins, and the runtime secret contract. Concurrency behaviour, Redis
semantics and the CI/CD pipeline are likewise settled and should not be
re-opened without a new ADR.*
