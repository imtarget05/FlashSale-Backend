# Current Tasks (Phase 6–9 — containers, IaC, CI/CD)

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
- [x] post-merge remote CI green — run 35779230807 (FlashSale CI).
- [ ] interview smoke PASS (checklist: openapi, swagger, register, login,
      authorize, product read, authenticated 202 → Completed, /orders/me,
      anonymous 202, metrics).
- [ ] tag v1.0-interview pushed; freeze = no feature work until after tag.

AI v2 — spec only, implementation DEFERRED (do NOT claim "Integrated
Qwen/Ollama" in a CV until the backend actually calls the model):
- [x] architecture selected: Ollama + Qwen3:4b (~4B params, Q4_K_M ≈ 2.5 GB)
      via OpenAI-compatible endpoint http://localhost:11434/v1 (client sends a
      dummy key such as `ollama`; the server does not validate it).
- [ ] runtime integration (first real inference evidence).
- [ ] grounded assistant.
- [ ] TTS.

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
