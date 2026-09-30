# Test-oracle quality gate — findings and limits

Tool: `tools/test_quality_gate.py`
Self-test: `tools/e2e/test_test_quality_gate.py` → **11/11**

## Why this exists

Four false passes were found in this portfolio's own suites during the P0
remediation:

| # | Shape | Why it is dangerous |
|---|---|---|
| 1 | `assert x or True` | unfalsifiable; passes with the code deleted |
| 2 | empty test body | passes vacuously (left by a bad line-number edit) |
| 3 | a stub asserting itself | the "test" called the thing it tested |
| 4 | a missing function counted as a refusal | `getattr(mod, name, None) is None` scores a safety **PASS** when the implementation is gone |

Cases 2 and 4 are the serious ones: a suite full of them converts an unknown
into an apparent pass.

## The gate was wrong three times before it was right

Recorded because the sequence is the evidence that the self-test has value.

### 1. The `SELF_ASSERTING` rule was dead code

The first version compared the **test's own name** against the calls inside it.
A real test is named `test_x` and calls `x`, so the two never match and the rule
never fired. It looked like a safety rule and reported nothing.

Rewritten to match the *callee expression* plus argument list, which is what
identifies `assert f(x) == f(x)`.

### 2. `SELF_ASSERTING` then produced false positives

Matching on the trailing attribute name reported
`out["Total_Actual"].sum() == production["Actual_Qty"].sum()` as a self-assertion,
because both calls end in `.sum`. That is a **real conservation test** and it was
being flagged. Fixed by comparing the whole callee expression, not the attribute.

### 3. `ASSERT_FREE` produced 26 false positives

Verified by reading the flagged tests:

- `self.assertGreaterEqual(len(RETRIEVAL_QUERIES), 12)` — unittest style
- `pd.testing.assert_frame_equal(a, b)` — pandas helper
- `_assert_passed(_run_probe("pytest"), "pytest")` — local wrapper
- `json.loads(line)` inside a loop — raises on a malformed line

The first two were fixed by widening `ASSERTING_CALLS`. The last two by a
`_delegates_to_helper` exemption, which is a **deliberate loosening** with a real
cost: a test calling an unrelated `check_*` helper would be excused.

**False positives are worse than no gate.** A gate that cries wolf on correct
tests teaches everyone to ignore it, at which point it protects nothing.

## Current results

| Project | files | test functions | findings |
|---|---|---|---|
| ApexInspect-AI | 32 | 208 | 1 ASSERT_FREE |
| Factory | 22 | 209 | 1 ASSERT_FREE |
| CreditFlow | 34 | 247 | 3 ASSERT_FREE |
| MAIA | 73 | 706 | 19 ASSERT_FREE, 3 SELF_ASSERTING |
| **ORA_TRUE** | | | **0** (one real hit, fixed — see below) |
| **ZERO_DENOMINATOR** | | | **0** |

The remaining ASSERT_FREE findings were not individually triaged in this pass.
They are **candidates for review, not confirmed defects**, and are reported as
such deliberately.

## The one confirmed defect

`CreditFlow/tests/test_feature_store.py:351`

```python
assert o.has("u1", "default_count_2y") is False or True  # has() theo now_fn
```

`has()` takes no `as_of` and reads the store's own clock, so it cannot
distinguish "not expired" from "expired". The `or True` made it unfalsifiable.
The TTL expiry invariant was untested at that line.

Fixed in `8c75391` and **verified by mutation**: disabling the TTL branch in
`online_store.py` makes `test_ttl_expired_returns_none` fail. Full CreditFlow
suite after the fix: 410 passed, 5 skipped.

## Known limits — stated, not hidden

1. **The gate cannot catch false pass #4.** A test that does
   `assert getattr(mod, "route_query", None) is None` contains a perfectly real
   assertion, so no structural rule fires, and the test still cannot distinguish
   a deleted implementation from a correct refusal. This needs a per-project
   oracle, not a general shape. The self-test records the case as expected-clean
   rather than pretending coverage.
2. **AST matching is literal.** A tautology built at runtime, or an assert
   inside a comprehension or a fixture, may be missed.
3. **Multi-line and dynamically generated asserts** are out of scope.
4. **Clean is necessary, not sufficient.** Passing this gate does not mean the
   tests are good — only that they are not one of these five specific shapes.

## Usage

```bash
python3 tools/test_quality_gate.py <path> [<path> ...]
python3 tools/test_quality_gate.py --json <path>     # machine-readable
python3 tools/e2e/test_test_quality_gate.py          # verify the gate itself
```

Exit 0 = clean, 1 = findings, 2 = bad invocation.
