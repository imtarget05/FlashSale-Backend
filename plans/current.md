# Current Plan: Phase 6–9 — Containerization, Azure Deployment, IaC, CI/CD

**Current State**:
Phases 1–5 complete with measured evidence:
- Naive overselling reproduced (50/50 accepted, stock 10→9).
- Fixed with an atomic conditional UPDATE (10/40, stock exactly 0).
- Redis fast-fail reservation tier + asynchronous idempotent fulfillment added;
  p95 of the hot path dropped from 574 ms → 29 ms. Idempotency (202/409-duplicate)
  and async fulfillment (status polling) verified end-to-end locally.

**Current Objective**:
1. Phase 6 — finalize Docker Compose (API + worker + postgres + redis, healthchecks).
2. Phase 7/8 — Terraform modules for Azure (Container Apps, PostgreSQL, Redis,
   Service Bus, Key Vault, monitoring) + deploy workflow wiring.
3. Phase 9 — CI (build + concurrency harness smoke) and CD (deploy to Container Apps).

**Goal**:
A deployable, reproducible system the user can push to Azure and demo end-to-end,
then AKS/GitOps hardening (Phase 10–12) with 02/03 projects.
