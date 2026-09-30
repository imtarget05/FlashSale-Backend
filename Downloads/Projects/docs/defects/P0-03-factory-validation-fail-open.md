# P0-03 — Factory: a validation failure was silently converted into data acceptance

| Field | Value |
|---|---|
| **ID** | P0-03 |
| **Severity** | P0 / DATA CORRECTNESS |
| **Status** | **FIXED** (plus one further defect found during the fix) |
| **Canonical commit** | see `docs/evidence/e2e/factory-postfix-defect-report.json` |

## Original behaviour

```
raw "abc"  →  clean_dataframe: pd.to_numeric(errors="coerce")  →  NaN
           →  validate_rows: pydantic raises TypeError (NOT ValidationError)
           →  apply_contract_gate: except Exception  →  return df
           →  the row is ACCEPTED and reaches OEE / Yield / scrap-rate math
```

## The dangerous part was not NaN

It was that **a validator exception changed validation from fail-closed to
fail-open**. Two separate mechanisms conspired:

1. `apply_contract_gate` wrapped everything in `except Exception: return df`,
   documented as *"Fails open on purpose ... a validator crash must not look
   like a total data outage."*
2. `validate_rows` caught only `ValidationError`. pydantic raises a bare
   **`TypeError`** when coercing a float `NaN` into an `int` field, so the
   row-level handler never saw it.

Measured before the fix: a row with `Reject_Qty=9999` survived a validator that
raised, and `9999` reached the KPI layer. **A gate that cannot run has not
passed — it has not run.**

## Root cause

`clean_dataframe` coerces an unparseable cell to NaN and keeps the row; pydantic
then fails to coerce `NaN` → `int` and raises `TypeError`; that escapes
`validate_rows`; `apply_contract_gate` treats any exception as "no contract
applies" and returns the frame.

## Fix

Three layers, each addressing a distinct failure mode:

| Layer | Change | Purpose |
|---|---|---|
| `validator.py` | catch `(TypeError, ValueError)` → record a `__coercion__` violation | the NaN-in-int-field case becomes ordinary data, quarantined normally |
| `pipeline.py` | `ContractGateUnavailable` raised instead of `return df` | a validator that cannot run fails the **job**, never accepts rows |
| `schemas.py` | new `FiniteFloat` type on **all** float fields | `ge=0` does **not** exclude infinity |

### The infinity finding (found *during* the fix)

`float("inf") >= 0` is **True**, so a `ge=0` bound admits `+inf`. Verified
before the fix: `Cycle_Time_sec=inf` passed the contract, and
`np.mean([1.0, 2.0, inf])` → `inf`, silently poisoning every downstream KPI
rather than raising. `-inf` was already rejected by `ge=0`, which is what made
the asymmetry easy to miss. `FiniteFloat` now rejects NaN and ±inf on all nine
float fields, including the four unbounded `MachineRow` telemetry fields.

## The invariant

```
VALIDATION_INFRA_FAILURE   MUST NOT   BECOME   DATA_ACCEPTANCE
```

This is stronger than testing NaN alone: it holds for *any* reason the contract
cannot be evaluated.

## Regression tests

`tests/test_contract_gate_fail_closed.py` — 19 tests. Honest RED: **7 failed /
12 passed** → GREEN **19 passed**.

| ID | Case | Pre-fix |
|---|---|---|
| F1 | `"abc"` in a required numeric → quarantined | PASS (guard) |
| F2 | explicit `NaN` → quarantined | PASS (guard) |
| F3 | `None` / `NaN` / `""` / `"abc"` each rejected, not collapsed | PASS (guard) |
| F4 | `±inf` rejected where KPI math needs finiteness | **FAIL** |
| F5 | 100 valid + 3 invalid → exactly 100 processed, 3 quarantined, 0 silent loss | PASS (guard) |
| F6 | validator raises `TypeError`/`AttributeError`/`ImportError`/`RuntimeError` → job fails | **FAIL** |
| F6b | infra failure returns **no** rows | **FAIL** |
| INV | `VALIDATION_INFRA_FAILURE ≠ DATA_ACCEPTANCE` | **FAIL** |
| NC1 | 20 valid rows still pass (no over-correction) | PASS |
| NC2 | genuine violation still quarantines, does **not** raise | PASS |
| NC3 | unknown dataset still skips gracefully | PASS |
| NC4 | missing file still raises `ValueError` | PASS |

### Negative controls that protect the original concern

The old fail-open existed for a real reason — not to discard a whole file of
good data because of one typo. `NC2` asserts that reason is still served: a
genuine per-row violation **quarantines one row and keeps the rest**. The
conclusion is that the original comment was solving the right problem at the
wrong level; the fix removes only the case where *nothing* was validated.

## Evidence

| Measurement | Before | After |
|---|---|---|
| Full suite | 212 passed, 5 skipped | **231 passed, 5 skipped** |
| Canonical audit F3 | **FAIL** | **PASS** (4/4 total) |
| `Target_Qty='abc'` via shipped order | 1 survived, NaN in KPI frame | 0 survived, 1 quarantine artefact |
| `Cycle_Time_sec=inf` | accepted → `mean` = `inf` | rejected |
| Validator raising | row accepted unvalidated | `ContractGateUnavailable`, job fails |
| 458k-row RAW→SILVER→GOLD→MART | 453,630 rows, lineage 2 edges | unchanged |
| Rerun idempotency | byte-identical | unchanged |

Row counts and lineage are unchanged, confirming the fix altered only the
malformed-data path and not the clean pipeline.

## Remaining limitations

1. **`clean_dataframe` still median-fills NaN.** A genuine missing value in a
   *clean* frame is imputed before the gate runs (by design — "contracts REJECT,
   cleaning FILLS"). But `clean_dataframe` runs *after* `apply_contract_gate` in
   the shipped order, so gate-visible NaN is rejected first. The residual risk is
   a NaN introduced by a later stage, which no contract re-checks.
2. **`-inf` vs `+inf` asymmetry is now closed** by `FiniteFloat`, but no test
   asserts `inf` cannot appear in a *published* mart table, only at the gate.
3. **The generated dataset is gitignored** (`data/raw/*`), so the 231-test result
   is not reproducible from a clean clone without running the generator. This is
   recorded rather than papered over.
