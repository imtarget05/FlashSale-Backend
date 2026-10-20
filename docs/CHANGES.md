# OpsBridge Change Log

Tracks notable changes, schema evolution, and breaking changes across OpsBridge releases.

---

## v1.0.x — Foundation

### 2026-10-19 — Migration restoration (023, 024)

- **Recreated `023_*` (aliases) migration** — idempotent, adds `aliases` column to `inv_items` if missing
- **Recreated `024_*` (add_branch_id_to_cashbox) migration** — adds `branch_id UUID` to `cash_boxes` with FK to `org_branches`
- **Production `alembic_version`**: restored to reflect current applied state after re-creation
- **Fix**:Migration 024 was lost from git history; now restorable

### 2026-10-19 — Phase 0 report

- Baseline gates recorded in `archive/00_PHASE0_REPORT.md`
- Findings: migration 024 file missing, migration 023 aliases column not present in production

---

## v1.0.x — Observability & QA

- Added observability module: metrics, tracing, structured logging
- E1/E2/E3 documentation: evidence, requirements, architecture
- QA artifact: test results, trace matrix, gate reports

---

## Prior (pre-2026-10-19)

- Maintenance commits unrelated to Phase 0
