# Current Tasks (Phase 6–9 — containers, IaC, CI/CD)

Phase 1–5 (done, evidence kept for traceability):
- [x] Phase 1: baseline API verified.
- [x] Phase 2: overselling reproduced (50/50 accepted, stock 10→9) — docs/benchmarks/phase2-oversell-experiment.md
- [x] Phase 3: atomic conditional UPDATE fix (10 accepted / 40 rejected / stock 0) — phase3-atomic-update-experiment.md
- [x] Phase 4: high-load evidence (p95 574 ms, pool-headroom incident) → ADR-003
- [x] Phase 5: Redis reservation + async idempotent worker (p95 29 ms, 202/409-dup verified) → ADR-004

Current:
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
