# Portfolio checkpoint — 7 of 8 acceptance gates VERIFIED

> **This file is a permanent record.** It is not updated when later work
> improves the portfolio. A future MAIA experiment may close Gate 8; that
> result belongs in a *new* checkpoint file, not in an edit to this one.
>
> Frozen from canonical commits listed below. Any claim not traceable to a
> commit and an evidence file in this document does not belong on a CV.

## Canonical statement

> 7 of 8 enterprise acceptance gates are runtime verified. The remaining RAG
> answerability gate is intentionally partial: evaluation demonstrated that
> topical retrieval similarity cannot safely separate answerable from
> unsupported queries without an unacceptable false-refusal trade-off. The
> candidate was therefore not shipped.

This is a stronger position than a fabricated 8/8 PASS, because every clause is
a measurement.

## Canonical commits

| Project | Commit | State at this checkpoint |
|---|---|---|
| FlashSale-Backend | `c6f7b3e` | VERIFIED |
| CreditFlow | `8965cd4` | VERIFIED |
| MiniERP-Manufacturing-Warehouse | `4107e51` | VERIFIED (DB profile NOT_RUN) |
| Enterprise-IT-Helpdesk-Lab | `f320d59` | VERIFIED |
| MAIA | `93f5788` | Gates 1-7 VERIFIED, **Gate 8 PARTIAL** |
| ApexInspect-AI | `6e0475c` | VERIFIED, P0-02 fixed in this commit |
| Factory-Data-Automation | `483a648` | VERIFIED, P0-03 fixed in this commit |
| portfolio (parent) | `de63d6f` | this checkpoint |

## Full test totals

Measured in the post-remediation pass. No project regressed.

| Project | Result | Notes |
|---|---|---|
| FlashSale-Backend | **172 unit + 62 integration, 0 failed** | |
| CreditFlow | **410 passed** | full suite, no `--ignore` |
| MiniERP | **224 passed**, 81 NOT_RUN, LogicAudit **21/21** | 81 blocked by Oracle DSN guard — not failures |
| Enterprise-IT-Helpdesk-Lab | **336 passed, 1 skipped** | matches baseline |
| Factory | **231 passed, 5 skipped** | up from 212 pre-fix |
| ApexInspect-AI | **210 passed, 2 skipped** | up from 197 pre-fix |
| MAIA | full suite run with real Qdrant | Gate 8B determinism + oracle PASS |

## Known defects fixed at this checkpoint

### P0-02 — ApexInspect: rejected frame reported as clean

**Commit `6e0475c`.** A quality-rejected camera frame returned `[]`, which every
consumer reads as "inspected, no defects found". A camera fault therefore
manufactured a false negative *and* cleared the 3-consecutive-defect line-stop
trigger.

Fix: return `None`, carrying explicit UNKNOWN semantics. Valid clean frames still
return `[]`, so this is not an abstain-everything change.

**A second bug was exposed by the fix:** the no-verdict branch in
`src/ui/app.py:413` fell through into `len(None)` and raised `TypeError`. The
"graceful" path was a crash. Same class of latent bug in
`src/vision/patching.py`, where an edge tile under `MIN_SIZE` would also crash —
and would crash on every oversized board.

State-machine policy, decided rather than assumed: an invalid observation
**pauses** the consecutive-defect streak. Treating it as clean clears an
escalating line-stop; treating it as a defect stops the line for the wrong
reason.

Evidence: 13 regression/negative-control tests, honest RED = 4 failed / 9 passed,
GREEN = 13/13. Audit A5b FAIL → PASS.

### P0-03 — Factory: validation failure became data acceptance

**Commit `483a648`.** `apply_contract_gate` caught `Exception` and returned the
input frame, so a validator that could not run silently approved every row.

The real trigger was not NaN as reported. `validate_rows` never caught it because
pydantic raises a bare **`TypeError`** (not `ValidationError`) when coercing
`NaN`→`int`. That escaped the validation handler and landed in the fail-open.

Fix: `ContractGateUnavailable`. Validator infrastructure failure now fails the
job closed. Business validation failure is a separate path with its own
semantics.

**Second bug found while fixing:** `ge=0` does **not** exclude infinity in
pydantic v2. `Cycle_Time_sec=inf` passed the contract and made every downstream
`mean` return `inf`. `-inf` was already rejected, which is exactly what hid the
asymmetry. Added `FiniteFloat`.

The original fail-open was a *deliberate, documented* decision. Its stated
concern — don't discard a file because of one typo — is legitimate and is
preserved by a negative control: one bad row still quarantines rather than
aborting the file. The fix removes only the case where *nothing* was validated.

Evidence: 19 regression/negative-control tests, honest RED = 7 failed / 12
passed, GREEN = 19/19. Full suite 212 → 231. Audit F3 FAIL → PASS, now 4/4.

## Gate 8 failure — MAIA answerability

**The gate that fails: "MAIA abstention routes labeled no-answer queries."**
`B8B1` and `B8B3` FAIL. Aggregate: **14/15 requirements met, GATE 8 PARTIAL.**

Measured: **1 of 9** labeled no-answer queries abstains. Generation is entered 8
times on the real production path.

### Why tuning cannot fix it

```
no-answer   max top_dense = 0.6957
answerable  min top_dense = 0.3140
```

The no-answer maximum is **higher** than the answerable minimum. The two score
distributions overlap, so no `SIMILARITY_THRESHOLD` value separates them. Any
threshold strict enough to reject the hardest no-answer row also rejects
answerable rows. This is an architectural conflation, not a calibration
problem: cosine similarity expresses **relevance**, and the policy needs
**answerability**.

### Measured candidate signals

Four signals, each measured against the real corpus through the real
`HybridRetriever` (not a mock):

| Signal | no-answer mean | answerable mean | Separable? |
|---|---|---|---|
| A — topical overlap | 0.399 | 0.471 | no |
| B — answer-type support | 1.000 | 0.882 | no |
| C — subject coverage | 0.688 | 0.739 | no |
| D — subject + type co-location | **0.000** | 0.346 | no |

Two findings worth keeping:

- **Signal B scores 1.000 on every no-answer row.** No threshold can separate
  them because the policy corpus *always* contains a number. This is proof the
  failure is structural.
- **Signal D reaches 0.000 on all nine no-answer rows** — the first signal to do
  so, and genuine evidence that answerability is not a similarity problem. But at
  the DEV-chosen threshold it accepts only **55.0%** of answerable questions. The
  false refusals concentrate in the "who do I contact" family whose answer spans
  are emails and proper nouns.

**Signal D was not shipped.** A gate refusing 45% of valid questions is not a

## Known limitations

Stated plainly, because each one is a thing a reviewer could otherwise assume.

1. **MAIA no-answer set is 9 rows.** That cannot establish a decision boundary.
   Any future signal measured on 9 rows has a number attached, not a conclusion.
2. **ApexInspect detection accuracy is NOT MEASURED.** Evidence covers runtime,
   latency (P50 42.7 / P95 47.2 / P99 48.9 ms), tail latency, failure semantics,
   audit integrity and PLC behavior. There is no labelled-frame evaluation, so
   **no accuracy figure exists and none may be claimed.**
3. ~~**Factory's 231 tests require a generated dataset** that is gitignored, so
   `git clone → test` is not yet reproducible from zero state.~~
   **RESOLVED after this checkpoint** (see "P7" below): the fixture is now
   generated, checksummed, manifest-verified in CI, and the full suite passes
   from a clean checkout.
4. **MiniERP's 81 integration tests cannot run** without an Oracle DSN. The
   safety guard is working as designed; they are NOT_RUN, not failed.
5. **Gate 6 retains 2 NOT_RUN checks** because Container Apps Log Analytics was
   unavailable at the time.
6. **Azure evidence is internally inconsistent.**
   `docs/evidence/e2e/azure-live-state.json` records
   `"control_plane_fqdn": null` for all three apps (and `"recorded_fqdn": null`
   for Factory), yet all three FQDNs were observed live and answering HTTP 200.
   The evidence file contradicts observed reality and must be reconciled before
   citation.
7. **Four false-pass defects were found in the test suites themselves** during
   this remediation: an `or True` tautology, an empty test body left by a bad
   edit, a stub that proved itself, and a missing function counted as a
   successful refusal. Test-oracle quality is now a first-class concern, not an
   assumption.

## CV claims removed or weakened

Full detail in `docs/cv-claim-map.json`. Summary at this checkpoint:
**16 claims — 9 KEEP, 4 WEAKEN, 3 DELETE.**

### Deleted outright (3)

| Claim | Why |
|---|---|
| ApexInspect "99% accuracy PCB defect detection" | No accuracy measurement exists anywhere in the repo. Latency on random-noise frames bounds compute cost and says nothing about detection quality. |
| MAIA "hallucination-safe RAG assistant that prevents answering without sufficient evidence" | The **opposite** is measured: 8 of 9 no-answer queries are authorized. |
| Portfolio "production-grade microservices on Kubernetes with auto-scaling" | No AKS, no ACR, no VNet. Three Azure Container Apps on a consumption plan. Trivially disproved by asking to see the cluster. |

### Weakened (4)

ApexInspect inference latency, Factory platform scale, Helpdesk SLA management,
FlashSale secret rotation (16 of 18 observability checks passed; 2 not run
because Log Analytics was unavailable).

### Added at this checkpoint (3 KEEP)

- **ApexInspect** — explicit no-verdict handling for low-quality frames,
  including that the graceful path was itself a crash.
- **Factory** — fail-open → fail-closed, preserving the partial-file policy
  that the original fail-open was legitimately trying to protect.
- **MAIA** — the four-signal negative result. Claimable *because* it is measured
  and because it changed a decision. Explicitly not claimable: that the system
  abstains correctly.

## Preconditions before MAIA answerability work resumes

Production answerability logic stays frozen until all five hold:

1. A larger no-answer set exists.
2. Answerability labels are validated against the canonical corpus.
3. A DEV/HOLDOUT split is locked and versioned by hash.
4. A candidate signal is measured on DEV only.
5. The safety/usability trade-off is reported as **both raw rates**, not
   collapsed into one weighted score.

The sequence is then `experiment → evidence → decision`, never
`new idea → patch production → hope tests pass`.

## Related documents

- `docs/ACCEPTANCE-HISTORY.md` — the failing checkpoints (abstention NOT_RUN,
  then FAIL at 1/9) and why they were kept
- `docs/defects/` — per-defect P0 write-ups
- `docs/portfolio-matrix.json` — machine-readable matrix
- `docs/gate8-ai-ml-eval.json` — Gate 8 aggregate with the failing requirement

working assistant. Adopting it because it maximises the no-answer safety metric
is precisely the threshold-fitting this gate exists to prevent, and it still
would not reach 8/8. A fifth variant (an "entity" question type for contact
lookups) measured 0.483 acceptance — worse — and was removed.

Production is unchanged. `gate8b_thresholds.yaml` still holds the similarity-only
gate.

