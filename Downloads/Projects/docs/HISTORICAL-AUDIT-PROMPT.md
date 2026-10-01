> # ⛔ HISTORICAL — DO NOT EXECUTE
>
> **This file is a historical record, not a runnable prompt.**
>
> Its philosophy is still correct and still binding on how work gets done:
> never turn `PARTIAL`/`FAILED` into a pass via skips, lowered thresholds,
> deleted tests, or config files read in place of runtime evidence; and
> diagram only what actually exists — no Kubernetes, service mesh or cloud
> component that is not deployed.
>
> **Its content is obsolete.** The canonical identities below are all
> superseded. Do not re-run this against the current repos.
>
> | Repo | SHA in this file | Canonical now (2026-10-01) |
> |---|---|---|
> | MAIA | `98874a9` | `68e7d15` — CI 13/13 green (run `36804255801`), `1008 passed / 3 skipped / 3 xfailed / 0 failed`, pyright 0 errors |
> | Helpdesk | `b5d1bfc` | frozen; see `docs/evidence/final/portfolio-completion-audit.json` |
> | Factory | `068a89a` | frozen; canonical test evidence `be96306`, deployed `be4ace3`, Azure rev `ca-factory-api--0000002` |
>
> The priority order in this file (Helpdesk → Factory → MAIA) came from audit
> scores of 72/85/86% and no longer reflects reality. The live work is
> `docs/_prompt_phase_f_maia_azure_architecture.md`.
>
> ---

# MASTER EXECUTION — AUDIT-FIRST CLOSEOUT OF 3 FLAGSHIP REPOS

_Historical. Originally the master prompt; superseded 2026-10-01._
_Established: 2026-09-30. Workspace: /Users/mainguyenbinhtan/Downloads/Projects_

---

## 0. CANONICAL PATHS  (overrides all prior assumptions)

```
MAIA     -> /Users/mainguyenbinhtan/Downloads/Projects/MAIA
            HEAD 98874a9fb8b920a1924e4e58e11454aa4c5239e4  branch main  CLEAN
            venv -> /Users/mainguyenbinhtan/Downloads/Projects/.e2e-envs/maia
            test cfg -> pyproject.toml ([tool.pytest.ini_options], pythonpath=src)

HELPDESK -> /Users/mainguyenbinhtan/Downloads/Projects/Enterprise-IT-Helpdesk-Lab
            HEAD b5d1bfcf3619d22d7ee28a7c25b22440eea1ad34  branch main  CLEAN
            venv -> .venv-helpdesk/bin/python   (python3.14, already provisioned)
            test cfg -> NONE at root. Suite is explicit path list:
              internal-portal/python-portal/tests/
              internal-portal/python-portal/test_isolation.py
              llm-gateway/tests/

FACTORY  -> /Users/mainguyenbinhtan/Downloads/Projects/Factory-Data-Automation-v-AI-Reporting-Platform
            HEAD 068a89ac5fad9e19635b202bf75bc20a51216e5b  branch main  CLEAN
            venv -> .venv-factory/bin/python    (python3.14, already provisioned)
            test cfg -> pytest.ini (testpaths = tests)
```

NOTE: the Factory directory name contains `-v-`. Do not shorten it.

HARD RULE — MAIA-clean IS DEAD:
  /Users/mainguyenbinhtan/Downloads/Projects/MAIA-clean has NO .git directory.
  It is a stale venv/artifact copy, not a repository.
  Do not read source from it. Do not run its venv. Do not treat it as canonical.
  If any doc or script references MAIA-clean, that reference is STALE -> log as P1.

## 1. PRIORITY ORDER  (revised — evidence-based, weakest asset first)

From /Users/.../Projects/docs/PORTFOLIO-COMPLETION-AUDIT-v2.md:

```
  Enterprise-IT-Helpdesk-Lab   72%  CV=PARTIAL LI=PARTIAL IV=YES  DONE=False
  Factory-Data-...-Platform    85%  STRONG PORTFOLIO
  MAIA                         86%  STRONG PORTFOLIO, Azure LIVE
```

So DEVELOPMENT priority is:

```
  1. Enterprise-IT-Helpdesk-Lab     <- largest real gap, weakest CV claim
  2. Factory-Data-Automation-v-AI-Reporting-Platform
  3. MAIA                            <- PROTECT. Verify, do not refactor.
```

This order exists ONLY to close the largest gaps first.
It does NOT determine CV order. See section 8.

MAIA IS THE STRONGEST ASSET AND THE ONE MOST LIKELY TO BE DAMAGED
BY UNNECESSARY CHANGE. For MAIA your mandate is narrow:
  - re-verify the suite is still green
  - re-verify the known open items
  - refresh runtime/Azure evidence
  - do NOT restructure. do NOT rename. do NOT "improve" answerability
    code that already has passing tests. do NOT rewrite eval/.

If MAIA tests are green and no new regression or stale claim exists:


## 2. NON-NEGOTIABLE EVIDENCE RULES

Evidence status vocabulary: VERIFIED / PARTIAL / FAILED / NOT_RUN

Never convert PARTIAL/FAILED into PASS by:
  - weakening an assertion
  - adding a skip
  - narrowing the dataset
  - deleting a test
  - lowering a threshold after seeing results
  - marking a doc claim as true because a config file exists

Runtime evidence means: the endpoint was actually called and the actual
response body, HTTP status, latency and timestamp were recorded.
A screenshot of a config file is not runtime evidence.

Do not run `git reset`, `git stash`, `git clean`, history rewrite, or
delete branches. Worktrees are currently CLEAN on all three repos.
If a worktree becomes dirty, classify every change before touching it.

## 3. PHASE A — BASELINE RECONCILIATION  (all 3 repos, before any work)

The audit document is STALE. Prove it, don't assume it.

A1. For each of the 3 repos record:
      git rev-parse HEAD
      git status --porcelain
      git log -1 --format='%H %cI %s'

A2. Run each suite, capture exact:
      passed / failed / skipped / errors / xfailed / duration / SHA / command / python version

    MAIA    : <.e2e-envs/maia>/bin/python -m pytest tests/ -q -p no:cacheprovider
    HELPDESK: .venv-helpdesk/bin/python -m pytest \
                internal-portal/python-portal/tests/ \
                internal-portal/python-portal/test_isolation.py \
                llm-gateway/tests/ -q
    FACTORY : .venv-factory/bin/python -m pytest tests/ -q

    Note the audit's own baseline was:
      MAIA     929 passed / 0 failed / 3 skipped
      HELPDESK 137 passed / 0 failed
    If your numbers differ from these, the DIFFERENCE ITSELF is a P1
    deliverable. Explain why before you continue.

A3. Build a stale-claim register. Known suspects to check, do not assume:
    - audit cites Helpdesk @ f320d59a002d ; real HEAD is b5d1bfcf3619
    - audit cites Factory  @ 93b3dd37b62a ; real HEAD is 068a89ac5fad
    - audit cites MAIA     @ 93f5788c270e ; real HEAD is 98874a9fb8b9
    - Helpdesk README badge claims "Tests 329/330 Passing"
    - Helpdesk README badge claims "API Smoke 68/68 Passing"
      -> but the measured suite is 137 tests. Determine whether 329/330 and
         68/68 refer to a DIFFERENT runner (e.g. a Node/JS suite under
         internal-portal/, or bench-portal.js). If no reproducible command
         produces those numbers, the badge is UNSUPPORTED and must be
         corrected or removed. This is a P1.
    - any README badge or claim whose number cannot be produced by a
      documented command

A4. Report Phase A as a table:
      repo | audited SHA | actual SHA | audit test count | actual test count | delta | verdict

## 4. PHASE B — ENTERPRISE-IT-HELPDESK-LAB  (primary effort)

Known open items from the audit, in priority order:
  P2 [helpdesk-rbac-dedup] RBAC tests + alert dedup, or document CAPABILITY GAP
  CV=PARTIAL, LI=PARTIAL, DONE=False

B1. RBAC
    Inventory the roles that ACTUALLY exist in the code. Do not invent an
    RBAC model the app does not have.
    Build a negative authorization matrix and turn each row into a test:
      anonymous  -> protected action        -> rejected
      role A     -> admin-only mutation     -> rejected
      role B     -> admin-only mutation     -> accepted
      role A     -> own-scope read          -> accepted
      cross-tenant / cross-user object access -> rejected
    MUTATION TEST (mandatory, proves the tests have teeth):
      bypass the role check, re-run, the RBAC tests MUST FAIL.
      Record the actual failure output, then revert.
    If the app genuinely has no role model, do NOT bolt one on for CV
    value. Instead write a CAPABILITY-GAP section in LIMITATIONS and
    drop the RBAC claim from CV. A documented gap beats a fake feature.

B2. ALERT DEDUPE
    Determine from code whether monitoring/alert ingestion creates tickets.
    If yes: define the dedupe key and window that the code ACTUALLY uses,
    then test that repeated identical alerts do not create a ticket storm.
    If the feature does not exist: document as CAPABILITY GAP.
    Do not invent a dedupe feature to fill a matrix.

B3. SLA SEMANTICS
    Read the real SLA implementation. It must be unambiguous whether SLA
    is 24/7 or business-calendar.
    If 24/7: state it explicitly in README and add a test that a ticket
    breaching during non-working hours still breaches on wall clock.
    If business-hours: the code must handle working hours, weekends,
    timezone, and pause/reopen. Test each.
    Do not add a business-calendar engine for sophistication.

B4. POWERSHELL SAFETY
    Scripts present: Backup-ADConfiguration.ps1, Backup-HelpdeskData.ps1,
    Restore-HelpdeskData.ps1, New-CompanyUser.ps1, Disable-CompanyUser.ps1,
    Export-ITAssetAudit.ps1, Test-NetworkHealth.ps1, Test-PrintScanHealth.ps1
    Audit for:
      - raw user input concatenated into a command string
      - argument validation / injection via parameter binding
      - unsafe or unvalidated path input
      - missing user / missing object handling
      - idempotent rerun
      - ShouldProcess / -WhatIf support where a mutation occurs
    Prove: no HTTP-reachable code path executes arbitrary shell from
    user-supplied string. Add a test or a static check that fails if
    someone reintroduces it.

B5. RESTORE DRILL
    Synthetic test data ONLY. Never touch real backup artifacts.
      backup -> capture baseline hash/state
             -> mutate or delete
             -> restore
             -> verify restored state byte-for-byte
    Record backup duration, restore duration, RTO-style dev measurement.
    LABEL IT EXPLICITLY: development restore drill, NOT a production
    disaster-recovery guarantee.

B6. AI WORKFLOW
    Only if the existing architecture already supports it. If it does not,
    mark INTENTIONALLY OUT OF SCOPE in LIMITATIONS.
    Do NOT bolt on a generic chatbot. If you do wire an AI-assisted
    troubleshooting flow, it must return a source/citation, and you must
    test behavior when the AI dependency is unavailable.
    If you integrate MAIA, keep the service boundary explicit and test
    both directions of unavailability.

B7. OBSERVABILITY
    Runtime-verify, do not assume:
      health endpoint
      ticket operation log
      automation execution log
      failure log
      audit entry
      request/correlation ID where supported
    Record actual responses.

B8. README BADGE CORRECTION
    Resolve the 329/330 and 68/68 claims from A3. Either produce the
    exact reproducible command that yields the number, or correct the
    badge to the number you can actually reproduce.

B9. HELPDESK EXIT CRITERIA
    - full suite green, or every non-green row classified
    - RBAC: tested with a mutation test, OR documented as CAPABILITY GAP
    - alert dedupe: tested, OR documented as CAPABILITY GAP
    - SLA semantics: unambiguous in README + covered by tests
    - PowerShell safety: VERIFIED
    - restore drill: VERIFIED with recorded timings
    - no unsupported badge or claim in README
    - LIMITATIONS current
    Then commit and report.

    Then STOP and report. Do not begin fixes until Phase A is reported.

  MAIA = APPLY_READY

Do NOT automatically claim FULLY_DONE if Gate 8 answerability remains PARTIAL.
A documented PARTIAL answerability limitation is acceptable for job application
as long as public claims do not exceed the evidence.

## 5. PHASE C — FACTORY-DATA-AUTOMATION-v-AI-REPORTING-PLATFORM

Baseline 85%, Azure-live, strong portfolio. Goal: close gaps, protect
what works. Repo already has a Makefile:
    make setup / make data / make test / make lint
Use the Makefile as the canonical entry point; do not invent a parallel one.

C1. REPRODUCIBILITY
    From a clean clone: install, `make data`, `make test`.
    The sample-data generator MUST be deterministic. Record:
      seed, row count, output manifest, checksums.
    If any fallback to smaller/synthetic data exists, it must be explicit
    in logs and status output. Silent fallback is a P1.

C2. DATA QUALITY  (this is the core correctness claim — do it properly)
    Test the validator against:
      NaN, +Inf, -Inf, None, empty string, non-numeric string,
      extreme finite values, invalid enum/category, partially malformed batch
    Invariants that must hold:
      - invalid numeric values NEVER silently enter KPI math
      - a validator implementation failure is NOT equivalent to valid data
      - row-level fail-soft semantics are preserved
      - one bad row does NOT discard an entire file, unless an explicit
        documented threshold/policy requires it
    NEGATIVE MUTATION (mandatory): allow NaN/+Inf to flow downstream,
    re-run, the data-quality tests MUST FAIL. Record output, then revert.
    Note: this is the exact bug class a manufacturing interviewer probes.

C3. AUTHN / AUTHZ
    Runtime evidence required, not config reading. Test:
      no credential, invalid credential, valid reader,
      restricted mutation, authorized action.
    Do not invent an RBAC model beyond what the application needs.

C4. PIPELINE E2E
    Run RAW -> SILVER -> GOLD -> MART end to end. Verify:
      row accounting, quarantine, lineage, catalog, idempotent rerun,
      output invariants.
    Idempotency: run twice, prove the second run does not double-count.

C5. AIRFLOW
    Verify the DAG parses, runs normally, retries a task, propagates
    failure, and is safe to re-run. If backfill is supported, test it;
    if not, document the limitation.

C6. LATE / OUT-OF-ORDER DATA
    First determine from the input model whether the system can even
    receive late or out-of-order data. If yes, test a meaningful scenario.
    If no, document it as a limitation. Do NOT invent streaming complexity
    to appear modern.

C7. OBSERVABILITY + ALERT
    Runtime-prove request metrics, pipeline metrics, a
    validation/quarantine metric, one failure metric.
    The repo has a real alert system (Reject>5%, Inventory<200,
    Downtime>30min). ACTUALLY TRIGGER one representative alert and
    record the observed output.

C8. AZURE
    Sequentially verify the live deployment. Record URL, HTTP status,
    latency, timestamp, response hash, revision/image digest.
    Test a healthy request, one business endpoint, one failure path,
    one recovery. Cold-start timeout is not automatically an outage.

C9. FACTORY EXIT CRITERIA
    full suite green or every red classified
    deterministic fixture generation proven
    data-quality invariants VERIFIED with a negative mutation
    pipeline E2E + idempotency VERIFIED
    AuthN/Z runtime evidence
    observability runtime evidence + one real alert triggered
    Azure re-verified
    Then commit and report.

## 6. PHASE D — MAIA  (protect, verify, refresh)

MAIA is already the strongest asset (86%, Azure live).
DO NOT refactor. DO NOT restructure. DO NOT rewrite answerability or
eval code that already passes. Any change here must be justified by a
concrete failing test or a concrete stale claim.

D1. Re-run the suite. Confirm still green at the current SHA.

D2. Inventory what ALREADY EXISTS before writing anything:
      eval/ directory
      gate8b_abstention.py
      gate8b_rag_quality.py
      gate8b_thresholds.yaml
      docs/eval/
    If the answerability dataset already exists, your job is to VERIFY
    its integrity, not to rebuild it. Report: row count, split method,
    DEV/HOLDOUT separation, whether paraphrases of the same fact leaked
    across the split, dataset checksum.

D3. Close the two open items from the audit:
    P1 [maia-citation-flake] — the citation test flaked across SHAs.
        Find the root cause. If it is a test-isolation bug, fix the test.
        If it is a product race, fix the product and prove it with a
        repeated-run soak (e.g. 10 consecutive runs, all green).
        Do not paper over it with a retry decorator.
    P2 [maia-commit-hygiene] — commit .gitignore plus k8s/ leftovers;
        consider documenting min_replicas=1.

D4. Re-verify the answerability claim end to end, and report the real
    numbers, not a single accuracy figure:
      unsupported authorization rate
      false refusal rate
      answerable acceptance rate
      no-answer abstention rate
      the 2x2 confusion matrix
    Sanity gate: a configuration that achieves 0% unsafe answers by
    refusing 45% of valid questions is REJECTED, not celebrated.
    If the current system is anywhere near that, say so plainly.

D5. Re-verify security negatively, with mutation proofs:
      cross-tenant retrieval blocked
      wrong-tenant exact answer does not leak
      prompt injection cannot escalate tool permissions
      missing/invalid auth rejected
      MUTATION: disable the tenant filter -> security suite MUST FAIL.
               Record output, then revert.

D6. Re-verify runtime observability by actually calling:
      /health  /ready  /metrics  /metrics/ai
    Show at least one normal, one retrieval, one agent/tool, one failure
    metric, and one trace/correlation id.

D7. Re-verify Azure live: URL, HTTP, latency, timestamp, response hash,
    revision digest. /health and /docs.

D8. If everything above is green and no stale claim exists, report
    "MAIA: APPLY_READY" and stop. That is the expected result.
    Only report FULLY_DONE if no material P0/P1 gap remains in scope.

## 7. PHASE E — CROSS-PROJECT CLOSEOUT

E1. Regenerate /Users/.../Projects/docs/PORTFOLIO-COMPLETION-AUDIT-v2.md
    from ACTUAL measured state. Its current SHAs are stale.
    Do not hand-edit numbers. Fix the generator if the generator is wrong.

E2. Create PORTFOLIO-FLAGSHIP-MATRIX.md with, per repo:
      canonical SHA, measured test counts, E2E status, security status,
      failure/recovery status, observability status, deployment status,
      known limitations, CV readiness, interview readiness.
    Do NOT invent a universal enterprise score unless the rubric was
    defined BEFORE you measured. A made-up 100-point score is worse
    than no score.

E3. Create CV-EVIDENCE-MAP.md. For every proposed CV bullet:
      claim | implementation file path | test that proves it |
      runtime evidence | canonical SHA | approved public wording
    Anything you cannot fill all six columns: REMOVE or WEAKEN it.
    This document is the deliverable that matters most. A bullet with a
    broken evidence chain is a liability in an interview, not an asset.

E4. README STANDARD — top section of each README must show, in order:
      Problem / Architecture / Verified capabilities / Live demo /
      How to run / How to test / Known limitations
    A recruiter must not scroll 500 lines to understand the project.
    Every numeric badge must be reproducible from a documented command.

E5. ARCHITECTURE DOCS — diagram only what exists. No DDP, no model
    sharding, no Istio, no service mesh, no fake Kubernetes, no fake
    cloud architecture. If it is not in the repo, it does not go in the
    diagram.

E6. INTERVIEW CASEBOOK — at least 3 deep cases per project, each with:
      Problem / Business impact / Initial assumption / How it failed /
      Evidence / Root cause / Fix or decision / Trade-off accepted /
      What breaks at 10x scale
    Suggested cases:
      MAIA    : retrieval relevance != answerability
                cross-tenant leakage
                restart + provider-failure behaviour
      HELPDESK: RBAC / SLA semantics
                PowerShell injection surface
                backup-restore incident workflow
      FACTORY : the NaN / +Inf KPI corruption bug
                pipeline idempotency under re-run
                quarantine correctness and row accounting

E7. FINAL REPORT
      Per repo: APPLY_READY / ALMOST / PARTIAL, with the specific
      remaining blockers. Not percentages alone — name the blockers.
      Then: remaining P0, remaining P1, optional P2.
            Public demo URLs actually verified this session.
            CV bullets that are safe to publish.
            CV claims removed or weakened for lack of evidence.
            Recommended CV project order — per section 8, which is
              MAIA -> Helpdesk -> Factory, confirmed against the
              evidence actually collected in this run. Do not re-derive
              it from the completion score.

FINAL QUESTION, answer honestly with evidence:
  "Can the candidate defend every major CV claim with
   code + test + runtime evidence + commit SHA + trade-off?"

Answer YES only if the evidence chain is complete for every bullet.
Answer NO otherwise, and list exactly which bullets fail.

## 8. CAREER POSITIONING — DO NOT CONFUSE WITH DEVELOPMENT PRIORITY

Development priority is evidence-based:

```
  1. Helpdesk
  2. Factory
  3. MAIA
```

This order exists only to close the largest gaps first.

It MUST NOT automatically determine CV project order.

For AI Application Engineer / Applied AI Engineer applications,
the default CV positioning remains:

```
  1. MAIA
     Flagship AI Application / RAG / Agent / MCP

  2. Enterprise-IT-Helpdesk-Lab
     Enterprise AI / Automation / Backend

  3. Factory-Data-Automation-v-AI-Reporting-Platform
     Data / AI / Business Automation
```

A lower-completion project may receive more engineering effort without
becoming the primary CV identity.

Likewise, a mature project may require little engineering effort while
remaining the flagship project.

MAIA status terminology:

```
  APPLY_READY:
    tests/evidence sufficient for public CV claims, with limitations
    documented.

  FULLY_DONE:
    no material P0/P1 gaps under the agreed project scope.
```

Do not require FULLY_DONE before job applications.
Do not label MAIA FULLY_DONE merely because the suite is green if
Gate 8 remains PARTIAL.

---

START WITH PHASE A — BASELINE RECONCILIATION.
Do not modify any file until Phase A is reported.


