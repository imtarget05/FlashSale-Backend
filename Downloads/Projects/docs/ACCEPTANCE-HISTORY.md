# Acceptance history — P0 defect remediation

This file is a **timeline**, not a status report. It is deliberately not
rewritten when a later run improves, because the failing checkpoints are the
evidence for why the current design exists.

## Timeline

| # | Checkpoint | Gate 8 | Trigger |
|---|---|---|---|
| 1 | `7/8` — abstention **NOT_RUN** | PARTIAL | the abstention gate had never been executed; nobody knew whether it passed or failed |
| 2 | `7/8` — abstention **FAIL** (1/9 abstained) | PARTIAL | gate implemented and run honestly against the production path. **Strongest truthful checkpoint at the time**: a measured failure is worth more than an unrun check |
| 3 | `7/8` — post-defect-fix | **PARTIAL (unchanged)** | P0-02 and P0-03 fixed; P0-01 investigated, four signals measured, none shipped |

## What changed in step 3

| Defect | Before | After |
|---|---|---|
| **P0-02** ApexInspect — rejected frame reported as clean board | `[]` returned | `None` (no-verdict). 210 tests pass. Audit A5b PASS |
| **P0-03** Factory — validation failure became data acceptance | fail-open, `return df` | fail-closed, `ContractGateUnavailable`. 231 tests pass. Audit F3 PASS |
| **P0-01** MAIA — answerability | 8/9 no-answer queries authorized | **unchanged** — four candidate signals measured, none shipped |

## Why MAIA did not improve

This is the substantive result of step 3, and it is deliberately not dressed
up as progress.

The root cause was correctly identified as an architecture conflation:
`RELEVANCE` (cosine similarity) is used to decide `ANSWERABILITY`. Four
replacement signals were then measured against the real corpus through the real
`HybridRetriever`:

| Signal | no-answer mean | answerable mean | Separable? |
|---|---|---|---|
| A — topical overlap | 0.399 | 0.471 | no |
| B — answer-type support | 1.000 | 0.882 | no |
| C — subject coverage | 0.688 | 0.739 | no |
| D — subject + type co-location | **0.000** | 0.346 | no |

Signal D is the first to reach 0.000 on all nine no-answer rows — a real
finding, and direct evidence that answerability is not a similarity problem.
But at the DEV-chosen threshold it accepts only **55.0%** of answerable
questions, and the false refusals concentrate in the "who do I contact" family
whose answer spans are emails and proper nouns.

**D was therefore not shipped.** A gate that refuses 45% of answerable
questions is not a working assistant. Adopting it because it maximises the
no-answer safety metric would be exactly the threshold-fitting this gate
exists to prevent, and it still would not reach 8/8.

A fifth variant (an "entity" question type for contact lookups) was implemented,
measured at 0.483 acceptance — **worse** — and removed. It is retained in
`MAIA/docs/eval/proto_answerability_signals.py` as a comment recording the
attempt.

## What is *not* claimed

- Gate 8 is **PARTIAL**, 14/15. It is not 8/8.
- No production threshold was changed. `gate8b_thresholds.yaml` still holds the
  similarity-only gate, and `B8B1` / `B8B3` still FAIL.
- No CV or LinkedIn claim may state that MAIA prevents hallucinations, is
  grounded, or abstains correctly. The measured position is 1/9.

## Next step, if pursued

A **real answerability model** (NLI entailment or extractive QA over the
retrieved chunks), evaluated on the same harness with the same negative
controls. The lexical layer has been measured and is insufficient; that
negative result is the useful input to the next attempt.

The dataset is also thin: **9 no-answer rows** cannot establish a decision
boundary. Any future signal needs a larger no-answer set before its numbers
mean anything.
