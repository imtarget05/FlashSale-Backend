# OpsBridge — Phase 0 Report (Baseline Gates & Findings)

**Date:** 10/19/2026
**Branch:** `feature/g1-opsbridge-foundation` @ `2919b97` (dirty)
**Workload:** OpsBridge foundation, observability, E1/E2/E3 docs, QA artifacts

---

## 1. Baseline Gates (Checked)

| # | Gate | Status | Evidence |
|---|------|--------|----------|
| G1 | OpsBridge git branch `feature/g1-opsbridge-foundation` | ✅ | `2919b97` Committed |
| G2 | OpsBridge worktree clean? | ❌ | 9 modified + 14 new files |
| G3 | README/coverage? | ✅ | docs present |
| G4 | Database migration state | ✅ | `alembic_version` = `024_add_branch_id_to_cashbox` |
| G5 | Migration chain 001–022 | ✅ | 22 migration files in `backend/alembic/versions/` |
| G6 | Migration 023 (aliases) | ❌ | File missing from `versions/` |
| G7 | Migration 024 file | ❌ | File missing from `versions/` |
| G8 | `cost_price` in `inv_items` | ✅ | Present on production DB (`inv_items` column) |
| G9 | `branch_id` in `cash_boxes` | ✅ | Present on production DB |
| G10 | `alembic_version` snapshot | ✅ | `024_add_branch_id_to_cashbox` |

---

## 2. Findings

### 2.1 Migration 021 — `cost_price` backfill chain

- Migration `021_receipt_invoice_and_lots.py` step 3 conditionally adds `cost_price` to `inv_items`
- Step 5 (backfill) queries `i.cost_price` from `inv_items`
- On a **fresh** DB, step 3 adds `cost_price` before step 5 runs → chain works
- On the **production** DB, `cost_price` already exists → branch 024 was applied and worked
- **No fix needed in migration 021**

### 2.2 Migration 024 — `024_add_branch_id_to_cashbox`

- **File missing** from `backend/alembic/versions/`
- Production `alembic_version` = `024_add_branch_id_to_cashbox` → migration was applied
- `cash_boxes.branch_id` column exists on production
- **Fix:** recreate the missing migration file with idempotent upgrade (check column existence before adding)

### 2.3 Migration 023 — aliases

- File `023_*.py` not in `versions/` directory
- Production DB has no `alias` column on `inv_items` (13 cols: id, sku, name, unit, tracked_serial, active, created_at, updated_at, category, packaging, cost_price, selling_price, min_stock)
- **Not critical** — no alias-related production data

### 2.4 OpsBridge worktree

- 9 modified + 14 new files (observability, E1/E2/E3 docs, QA artifacts)
- Worktree dirty — must be committed or stashed before phase continuation

---

## 3. Next Actions

1. Recreate migration 023 and 024 files (idempotent upgrades)
2. Patch `alembic_version` to match new head after restoring 024
3. Commit migration restoration under `backend/alembic/versions/`
4. Verify migration chain on fresh test DB

---

## 4. Sign-off

Preliminary review only — no code changes made.
