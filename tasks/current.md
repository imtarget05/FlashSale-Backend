# Current Tasks (Phase 6–9 — containers, IaC, CI/CD)

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
- [x] Terraform full Azure stack (ACR, PostgreSQL, Redis, Service Bus, Container Apps,
      Log Analytics) + dev/prod tfvars; `terraform validate` green.
- [x] CI: build + unit/architecture tests + concurrency harness; CD: ACR push + Container Apps.
- [x] Interview notes for Phase 2–5 decisions (docs/interview-notes/).
- [x] **Clean Architecture refactor** (folder-level): Domain / Application (ports) /
      Infrastructure (adapters) / Api + Worker composition roots; 7 tests incl.
      dependency-rule guards. See AGENTS.md "CLEAN ARCHITECTURE RULE".

Next (Phase 10 + hardening):
- [ ] Wire OpenTelemetry → Azure Monitor (App Insights) exporter.
- [ ] Key Vault CSI driver instead of Kubernetes secrets.
- [ ] Chaos experiment: kill the worker mid-drain to demonstrate at-least-once redelivery.

*Note: Do not work on concurrency, Redis, Terraform, Azure, or GitHub Actions.*
