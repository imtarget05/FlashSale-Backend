# P0-02 — ApexInspect: a quality-rejected frame was reported as a clean board

| Field | Value |
|---|---|
| **ID** | P0-02 |
| **Severity** | P0 / CRITICAL DOMAIN CORRECTNESS |
| **Status** | **FIXED** (regression-tested, full suite green) |
| **Canonical commit** | see `docs/evidence/e2e/apex-postfix-defect-report.json` |

## Original behaviour

```
low-quality / rejected frame  →  infer()  →  []
```

## Business risk

`infer()`'s own docstring defines a two-state contract:

| Value | Meaning |
|---|---|
| `[]` | inspected, no defects found — **a PASS** |
| `None` | no verdict could be reached — **an UNKNOWN** |

but the quality-gate branch returned `[]`. So a blurred, black or undersized
camera frame was laundered into a **clean board**, and three consumers acted on
it:

1. `src/ui/app.py` — counted it as `is_defective=False`, wrote a clean telemetry
   row, and **reset `consecutive_defects`**. A camera failing every third frame
   would prevent the 3-consecutive-defect line-stop from ever firing.
2. `src/vision/patching.py` — merged it into an empty aggregate.
3. The HUD — painted a green `STATUS: PASS (NO DEFECTS)` banner.

The false negative was produced *by the failure of the evidence-gathering step
itself*, which is the one thing a quality gate exists to prevent.

## Root cause

A single `return` statement conflated "the detector ran and found nothing" with
"the detector could not run":

```python
if not q["ok"]:
    return self.render_annotations(frame.copy(), [], latency_ms), [], latency_ms
```

## Fix

The quality gate now returns the **existing** no-verdict value (`None`), so no
API changed and every consumer already handling "model unavailable" handles this
correctly. The annotation is rendered with `status="UNKNOWN"` so the HUD cannot
paint a PASS.

Two latent defects were exposed and fixed alongside it:

- **`src/ui/app.py:413`** — the no-verdict branch set `step_inspect = False` but
  did **not** `continue`, so control fell through to `len(detections)`, raising
  `TypeError: object of type 'NoneType' has no len()`. The "graceful" no-verdict
  path was in fact a **crash**. Fixed with `continue`.
- **`src/vision/patching.py`** — `for det in patch_dets` would raise on `None`,
  and an edge tile narrower than `MIN_SIZE` hits that path on every oversized
  board. Tiles are now counted; a clean aggregate is returned only when **every**
  tile was actually inspected, while a positive detection is still honoured
  (a defect seen in an inspected tile is real evidence regardless of its
  neighbours).

## State-machine policy (documented, not assumed)

For `DEFECT, DEFECT, INVALID, DEFECT`:

**An invalid observation PAUSES the sequence.** It neither increments nor
resets the consecutive-defect count.

- Not *clean* → a dropout must not clear an escalating line-stop.
- Not *defect* → a camera fault is not evidence of a defective board.
- *Paused* → the next real observation continues the count, and the gap is
  recorded as an inspection failure so it stays auditable.

## Regression tests

`tests/test_quality_gate_no_verdict.py` — 13 tests, all asserting:

| ID | Case | Pre-fix |
|---|---|---|
| A1 | good frame, no defect → `[]` | PASS (guard) |
| A2 | good frame, defect → detections | PASS (guard) |
| A3 | blurred frame → `None` | **FAIL** |
| A4 | black frame → `None` | **FAIL** |
| A4b | undersized frame → `None` | **FAIL** |
| A5 | corrupt input never yields `[]` | PASS |
| A6 | invalid frame ≠ clean frame (pauses streak) | PASS (policy) |
| A6b/c/d | invalid ≠ increment; clean resets; alternation safe | PASS (policy) |
| NC1 | valid frames still produce real verdicts | PASS (anti-abstain) |
| NC2 | defect detection not lost by over-strict gate | PASS (anti-abstain) |
| HUD | no green PASS banner on an uninspected board | **FAIL** |

Honest RED before the fix: **4 failed, 9 passed**. GREEN after: **13 passed**.

Two pre-existing tests were also fixed — their *fixtures* were invalid frames
(a uniform black canvas and a flat green field have zero Laplacian variance, so
the gate correctly rejects them). The assertions were right; the inputs were not.

## Negative controls

The two most important are `NC1`/`NC2`: they fail if the fix degenerates into
"return `None` for everything". Abstention without discrimination is
indistinguishable from a broken detector — the same lesson as the MAIA
`always-abstain` control.

## Evidence

- Full suite: **210 passed, 2 skipped** (was 150 passed / 1 skipped in the
  pre-fix environment; the current environment has the real ONNX model).
- Canonical audit `A5b` strengthened from a single 1×1 frame to **all four
  rejection reasons**, and now also asserts the converse — that an *accepted*
  frame is never turned into a no-verdict.
- **Latency re-measured** (the return path changed): P50 42.7 ms, P95 47.2 ms,
  P99 48.9 ms. No performance regression is claimed from the pre-fix run
  because the two runs used different frames.

## Remaining limitations

1. **No detection accuracy has ever been measured.** Latency is measured on
   random-noise frames; that bounds compute cost and says nothing about
   detection quality. `A3` confirms the threshold is wired and monotone, which
   is not the same as it being well calibrated.
2. **Flat frames are rejected for `blur`, not `brightness`.** A uniform frame
   has zero Laplacian variance, so the blur check fires first and the
   `BRIGHT_MIN`/`BRIGHT_MAX` bounds are unreachable for exactly the frames they
   were written for. The audit now records the real reason per case rather than
   the one intended, which is how this became visible. The bounds still apply to
   low-variance-but-textured frames.
3. **Streak policy is unit-tested as a pure function**, not through the
   Streamlit session state, so the wiring in `app.py` is verified by reading
   rather than by test.
