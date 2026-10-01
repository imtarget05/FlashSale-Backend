# PORTFOLIO COMPLETION AUDIT v2 (derived — do not hand-edit numbers)

_Source: `docs/evidence/final/portfolio-completion-audit.json` · generator: `docs/_gen_portfolio_audit_v2.py` · model: MODEL_A_

## Executive Summary (generated)

```text
Projects audited: 7
Average completion: 81.3%
Projects >=82: 5/7
Azure live: 3/7 (3/5 among CV-ready top five)
P0: 0  P1: 8  P2: 9
Critical blockers: 0 (MODEL A: P0 implies blocker)
DONE: 0  ALMOST: 5  PARTIAL: 2
```

## Ranking by completion (derived)

```text
1. CreditFlow — 86% (sha f9f8625f198e, ENTERPRISE-ORIENTED)
2. MAIA — 86% (sha a82f24b2123b, STRONG PORTFOLIO)
3. FlashSale-Backend — 85% (sha c6f7b3e1d647, ENTERPRISE-ORIENTED)
4. Factory-Data-Automation-v-AI-Reporting-Platform — 85% (sha 77e5d140b951, STRONG PORTFOLIO)
5. ApexInspect-AI — 83% (sha 6e0475c618e4, STRONG PORTFOLIO)
6. MiniERP-Manufacturing-Warehouse — 72% (sha 97bb3612fadf, PORTFOLIO USABLE)
7. Enterprise-IT-Helpdesk-Lab — 72% (sha 37c5c0d8e009, PORTFOLIO USABLE)
```

## CreditFlow — 86/100

```text
HEAD: f9f8625f198e39ae850dce470c8654bcce32469d
Path: /Users/mainguyenbinhtan/Downloads/Projects/CreditFlow
Worktree: DIRTY (untracked k8s/ from dir-cleanup; no code change)
RUN_A: python3 -m pytest tests/ -q -p no:cacheprovider --ignore=tests/test_verify_deploy.py --ignore=tests/test_deploy_pages.py
  -> passed=405 failed=0 skipped=2 blocked=0 dur=148.83s [2026-09-30]
PRIOR (docs/execution-state.md Phase 7): passed=410 failed=0 skipped=5 @ c2d87d3
Azure LIVE: https://ca-creditflow-api.wittysand-b748274c.eastasia.azurecontainerapps.io/health HTTP=200 sha256=aa5c315c219f6022... @ 2026-09-30T09:53:48Z
P1: [cf-metrics-evidence] /metrics + alert-firing runtime evidence
P1: [cf-deploy-evidence] Disbursement retry/DLQ evidence on Azure path
P2: [cf-badge-count] README badge 357 -> 405 passed
CV=YES LI=YES IV=YES DONE=False
```

## MAIA — 86/100

```text
HEAD: a82f24b2123b5abceeeb5264677aa81bbc437df7
Path: /Users/mainguyenbinhtan/Downloads/Projects/MAIA
Worktree: CI-backed at a82f24b and RUNNING on Azure as ca-maia-api--0000006 (100% traffic, Healthy). 2d2eaf9 and the 987 figure are historical.
RUN_A: GitHub Actions CI (13 jobs)
  -> passed=998 failed=0 skipped=12 blocked=0 dur=614.0s [2026-10-01]
PRIOR (2d2eaf9 CI run 36768831367 (retired)): passed=987 failed=0 skipped=12 @ 2d2eaf9f95d8
Identity: CANONICAL_RESOLVED
Azure LIVE: https://ca-maia-api.wittysand-b748274c.eastasia.azurecontainerapps.io/health HTTP=200 sha256=82e530f7dc4baa2f... @ 2026-10-01T05:05Z
P1: [maia-gate8-unmeasured] Gate 8 abstention metric is UNMEASURED: audit reports usable=0 of 9 no-answer rows. The suite is green but no refusal-accuracy figure is published, because reporting one over 0 usable rows would be reporting a metric of nothing. 8 rows remain PENDING corpus verification.
P1: [maia-iac-invariant-contract-drift] IaC invariant checker crashes on the canonical ARM output: it was written against an earlier main.bicep structure and cannot tell an invariant violation apart from an unsupported ARM node. Records validator-template contract drift ONLY -- it is NOT a claim that any Key Vault invariant is violated, because the check never reached a verdict. TODO 3 is REOPENED/BLOCKED until unknown ARM structures fail closed with a JSON path instead of a traceback.
P2: [maia-commit-hygiene] untracked k8s/ and docs/evidence/ in main worktree; consider documenting min_replicas=1
CV=YES LI=YES IV=YES DONE=False
```

## FlashSale-Backend — 85/100

```text
HEAD: c6f7b3e1d647eb95dc59d587e45456ee24c4f376
Path: /Users/mainguyenbinhtan/Downloads/Projects/FlashSale-Backend
Worktree: CLEAN
RUN_A: dotnet test tests/UnitTests + dotnet test tests/IntegrationTests (Testcontainers Redis/PG/RabbitMQ)
  -> passed=234 failed=0 skipped=0 blocked=0 dur=19s [2026-09-30]
Azure: not live (no public endpoint expected; deploy stops at docker-compose)
P1: [fs-restore-rto] Restore drill + publish RTO/RPO
P2: [fs-testcontainers-pin] Pin Testcontainers version (NU1603 warnings)
CV=YES LI=YES IV=YES DONE=False
```

## Factory-Data-Automation-v-AI-Reporting-Platform — 85/100

```text
HEAD: 77e5d140b951c27bd1e141bbd55eb4457f8e9e0d
Path: /Users/mainguyenbinhtan/Downloads/Projects/Factory-Data-Automation-v-AI-Reporting-Platform
Worktree: CLEAN at 77e5d14. docs_head is a docs-only commit (README provenance summary); it does not change the runtime artifact, so no redeploy is implied by it.
RUN_A: GitHub Actions CI/CD Pipeline
  -> passed=290 failed=0 skipped=5 blocked=0 dur=120.0s [2026-10-01]
PRIOR (70509fc (CI lint/security hygiene, recorded this session)): passed=288 failed=0 skipped=5 @ 70509fc2e3bf
Azure LIVE: https://ca-factory-api.wittysand-b748274c.eastasia.azurecontainerapps.io/api/v1/health HTTP=200 sha256=954073c5e505932d... @ 2026-09-30T15:35:31Z
P2: [factory-alert-firing] NOT RUN: 6 Prometheus alert rules exist in observability/prometheus/alerts.yml and the API serves 16 alerts, but no alert has been observed firing through Prometheus/Alertmanager. Non-blocking for application readiness.
P2: [factory-kubernetes] NOT DEPLOYED / NOT RUN: 108 files are tracked under k8s/. No cluster, no apply evidence, no README claim. They are not evidence that Factory runs on Kubernetes.
P2: [factory-rbac] NOT RUN: this app has an API-key guard (app/api/security.py) verified to return 401 without a key and 200 with one, but no per-role AuthZ/RBAC model exists and none was invented.
P2: [factory-gate-cloud-negative] NOT RUN on Azure: the quality gate was observed on the live GOOD path (status GOOD, 5/5 checks, 13 provenance entries). The BAD/UNKNOWN fail-closed branch was NOT triggered against the live deployment, to avoid corrupting demo data. It is verified in the HTTP-level regression suite and locally in a container. Do not claim the cloud blocked bad data.
Wording guard: Five identities are recorded and are not merged. (1) Docs HEAD is 77e5d14, a docs-only commit that moved the OCI identifier out of README without touching runtime. (2) Test evidence is be96306: 290 passed / 5 skipped / 4 xfailed, independently reproduced in an isolated worktree, and the 4 xfails are M1-M4 mutation controls that must never pass. (3) The deployed source is be4ace3, which carries the DLQ run-scope fix; the live revision ca-factory-api--0000002 reports scope=current_etl_run, a field that exists only after that fix. (4) The artifact digest is sha256:7c3e81b7..., linked to be4ace3 by CI buildx provenance, SLSA v1 attestation verified. (5) The quality gate is VERIFIED on the live GOOD path (status GOOD, 5/5 checks, 13 provenance entries, 0 rejected); the BAD/UNKNOWN fail-closed branch is NOT RUN against live data, to avoid corrupting the demo dataset. generation_mode is FALLBACK, not a real model: no Ollama endpoint is reachable from the container. Production AuthN is NOT VERIFIED: FACTORY_API_KEY is unset, so the live API is open by design; the guard is verified in tests and a controlled container. Cold-start SLA is NOT CLAIMED. CI is GREEN 8/8 at 77e5d14, including the container-smoke job observed green on hosted CI for the first time. Superseded historical figures 258/5 and 288/5 must not be published as current.
CV=YES LI=YES IV=YES DONE=False
```

## ApexInspect-AI — 83/100

```text
HEAD: 6e0475c618e494c4be1fadb43af01001cdf148e4
Path: /Users/mainguyenbinhtan/Downloads/Projects/ApexInspect-AI
Worktree: CLEAN
RUN_A: .venv-apex2/bin/python -m pytest tests/ -q -p no:cacheprovider
  -> passed=210 failed=0 skipped=2 blocked=0 dur=6.69s [2026-09-30]
PRIOR (docs/_gen_manifest_apex.py D3-APEX-BATCH (tests/FULL.xml)): passed=137 failed=0 skipped=2 @ unknown (man
Azure: not live (k8s manifest is http-echo test double by design; real API never deployed)
P1: [apex-accuracy] Labeled validation set + measure precision/recall
Wording guard: FULL MODEL ACCURACY = NOT FULLY MEASURED. PRELIMINARY QC OBSERVATION (2/15 boards rejected = 13.3% board-level observation) = MEASURED but reproducibility disputed in docs/RECRUITER-EVIDENCE.md. NEVER call it recall/miss-rate/precision/F1/mAP.
CV=YES LI=YES IV=YES DONE=False
```

## MiniERP-Manufacturing-Warehouse — 72/100

```text
HEAD: 97bb3612fadfcf5ede2db64017bdb878edf60689
Path: /Users/mainguyenbinhtan/Downloads/Projects/MiniERP-Manufacturing-Warehouse
Worktree: CLEAN
RUN_A: dotnet test MiniERP.Api.Tests -c Release --filter Category!=Integration + dotnet test MiniErp.LogicAudit
  -> passed=245 failed=0 skipped=0 blocked=81 dur=2s [2026-09-30]
Azure: not live (no public endpoint expected)
P1: [minierp-oracle-e2e] Run Oracle integration suite once in container + save evidence
P1: [minierp-outbox-test] Outbox publish->consume->dedupe test
P2: [minierp-cloud-deploy] Cloud/API deploy + dashboard e2e
CV=PARTIAL LI=PARTIAL IV=YES DONE=False
```

## Enterprise-IT-Helpdesk-Lab — 72/100

```text
HEAD: 37c5c0d8e0096bcf84fac4a7bae807311863f92b
Path: /Users/mainguyenbinhtan/Downloads/Projects/Enterprise-IT-Helpdesk-Lab
Worktree: CLEAN
RUN_A: GitHub Actions CI
  -> passed=156 failed=0 skipped=0 blocked=0 dur=129.0s [2026-10-01]
Azure: not live (n/a)
P2: [helpdesk-capability-gaps] DOCUMENTED CAPABILITY GAPS, not blockers: (1) no role-based access on portal routes, (2) no alert/ticket dedupe (grep dedup returns 0 in both Node and Flask), (3) SLA is slaPercent only, with no clock-based breach. None were implemented for CV value.
CV=PARTIAL LI=PARTIAL IV=YES DONE=False
```

## Old report bugs fixed here

- average 79.6 -> 80.0 (560/7)
- projects_ge_82: 4/7 -> 5/7
- azure_live among top-5: 4/5 -> 3/5 (Apex/FlashSale never had endpoints)
- critical_blockers 0 + P0 1 contradiction -> MODEL_A: blockers=1
- badge counted P1+P2 -> P2 only
- v1 said P1 gaps = 7 but project sections support 9 (minierp-outbox-test and cf-deploy-evidence never made the v1 final list); derived count is 9
- v1 breakdown fields mixed scores and maxima; fixed to true category scores + added breakdown-sum assertion
- v1 P0 (MAIA citation red) resolved: green at 93f5788 full suite AND on re-run at 47110b8 -> state-dependent flake, demoted to P1 test-reliability; P0 now 0, blockers 0

## Self-audit notes

- Duplicate venvs found INSIDE projects (pre-existing). CORRECTION 2026-09-30: three are IN-USE evidence envs, NOT cleanup candidates - Factory/.venv (TEST_EXECUTION_REPORT gate-note run), ApexInspect-AI/.venv 1.6G (opencv vision run per TEST_EXECUTION_REPORT), MAIA/.venv 705M (2026-09-27 full run, 518 passed). KEPT. Only Factory/.venv-fixture 591M was truly unreferenced -> DELETED 2026-09-30 (~591M reclaimed, regenerable). No score impact.
- MAIA drift 860 -> 932 explained: +72 from 3 new MCP prompts/session-registry test files at 93f5788 (NOT gate8 tests; gate8b work lives in root scripts + eval goldens).
- Apex MODEL_CARD '13% miss rate' wording is FORBIDDEN by wording guard; recruiter evidence disputes reproducibility.
- First Azure probe round 2/3 timeouts were Consumption cold starts (25-28s), not outages; retry with 90s timeout all 200.
- Concurrent-session warning: during integrity pass another agent created MAIA/eval/*.py (birth 09:54 UTC) and docs/_msg_*.txt. Canonical re-audit ran in detached worktree, unaffected.
- P1 helpdesk-flask-env RESOLVED 2026-09-30 by secondary executor: created .venv-helpdesk per repo requirements (portal requirements.txt + gateway requirements-dev.txt), added .venv*/ ignore. 42 importorskip-gated tests unlocked: 82+2blocked+11skip -> 137 passed/0 failed/0 skipped. No app code changed. Score 68->71 (tests 11->13, correctness 11->12).
- P1 helpdesk-restore-drill RESOLVED 2026-09-30 by secondary executor: REAL backup/restore drill on /tmp fixture with pwsh 7.6.5 (happy path + 4 negatives, all refused-to-write verified). AD part NOT_RUN (Windows-only, not faked). Evidence docs/evidence/helpdesk/restore-drill/. No app code changed. Score 71->72 (reliability 7->8).
- P1 fac-api-auth RESOLVED 2026-09-30 by secondary executor: TDD request-level tests (11 tests, TestClient through real middleware; prefix-bypass test RED pre-fix proving gap). Fix: exact-or-boundary safe-path matching in security.py (minimal). Full suite 231->242 passed, 0 failed. Roles/expiry N/A by design (single shared key, no tokens). Score 84->85 (security 7->8). Committed by repo owner as 068a89a.
- WORKSPACE ARCHIVE 2026-09-30 (secondary executor): local copies DELETED after remote-verify - MiniERP (archive/local-20260930 @97bb361), CreditFlow (archive/local-20260930 @f9f8625), FlashSale (main @c6f7b3e fast-forward), ApexInspect (archive/local-20260930 @6e0475c); tag portfolio-freeze-2026-09-30 on all 4, clean-clone verified. Reclaimed ~2.6GB. BLOCKED_KEEP_LOCAL (no origin remote, never force): manufacturing-contracts, spark-benchmark, k8s-manifests (stale stub README removed). MAIA-clean worktree REMOVED after preserving k8s/10-maia-api.yaml + .venv-maia into MAIA/ (+ .gitignore entry). plans/tasks/tools = Cline closeout workspace, untouched.

