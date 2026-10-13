# Phase 1 Report — Standardize & Harden (2026-10-08)

Source: Phase 0 seeds §5. All changes verified green (see Verify).

## 1A. Shared ADR pack — DONE
- `docs/adr/` canonical copies of 4 byte-identical ADRs (md5 verified).
- Rule: edit pack first, sync to 3 projects; deltas as addenda.

## 1B. Fail-closed deploy gates — DONE
- `SupportDesk/.github/workflows/deploy-azure.yml`: `::warning` skip →
  `::error` + `exit 1` (mirrors CreditFlow wording). All 3 Azure paths now
  fail loudly without AZURE_CREDENTIALS.

## 1C. Readiness-poll port — DONE
- SmartDoc `cd.yml`: new `verify-readiness` job (STAGE 2, needs
  build-and-push, main-only) polls live /api/actuator/health 10x15s via
  Render API; release renumbered STAGE 3. CreditFlow already covered
  (smoke_production.py via deploy-azure); SupportDesk already covered
  (deploy.yml 20-min live wait + /api/health).

## 1D. Local-first pattern — DOCUMENTED
- `docs/LOCAL_FIRST_PATTERN.md`: matrix (LLM/embed/vector/DB/ledger/
  workflow/queue/secrets) + 4 rules (single definitions source, honest-trade
  docstring, CI runs local path, log active backend). No code change needed:
  all 3 already comply (CreditFlow SQLite ledger + Ollama; SmartDoc local
  compose; SupportDesk in-process runner/index).

## 1E. Bake-drift fix — DONE
- SmartDoc `cd.yml` GHCR build-arg + `pages.yml` Pages env now both read
  `vars.SMARTDOC_API_URL` (fallback = current canonical backend URL).
  Single variable → image and Pages can never drift.

## 1F. Eval-regression — NO-OP (already present)
- SupportDesk CI has `--check` gate; SmartDoc CI has eval-grader +
  llm-judge-mock gating; CreditFlow has test_eval.py offline.
  Decision: no duplication; pattern recorded in LOCAL_FIRST doc.

## Verify (2026-10-08)
- CreditFlow `.venv` pytest test_eval+health_contract+check_artifacts: 10 passed.
- SupportDesk backend `.venv` test_main.py: 7 passed; eval_suite --check:
  exit 0, "No regression vs baseline" (92 tickets, schema 100%).
- SmartDoc eval/tests/test_grader.py (system python): 26 passed.
- YAML parse OK x3 (cd.yml, pages.yml, deploy-azure.yml).

## Phase 2 seeds
- Live smoke evidence (all 3 canonical endpoints, read-only).
- Shared reusable workflow for Render-poll (3 copies of same curl loop).
- SmartDoc SSO completion (Keycloak URL) or formal deferral ADR.
