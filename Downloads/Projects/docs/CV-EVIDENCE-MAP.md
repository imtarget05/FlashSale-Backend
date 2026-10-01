# CV Evidence Map

This document decides **what the CV is allowed to say**. It is the source for
`CV-ready` in `PORTFOLIO-FLAGSHIP-MATRIX.md`; the completion score is not.

Every claim must fill six columns. A claim that cannot fill all six is removed
or weakened — it is not written with a hedge.

| Field | Meaning |
|---|---|
| **Evidence** | file path a reviewer can open |
| **Verified SHA** | commit the evidence was produced at, not the docs HEAD |
| **Runtime status** | VERIFIED / PARTIAL / NOT_RUN / FAILED |
| **CV wording** | the strongest sentence the evidence supports, verbatim |
| **Limitation** | what must not be implied |
| **CV usable** | YES / PARTIAL / NO |

Identity rule: a repository's docs HEAD can move without moving what was
tested or deployed, so docs HEAD and verified SHA are tracked separately and
never substituted for one another.

Source of truth for all numbers: `docs/evidence/final/portfolio-completion-audit.json`,
generated into `docs/PORTFOLIO-COMPLETION-AUDIT-v2.md`.

---

## 1. MAIA — flagship AI application

| | |
|---|---|
| Docs HEAD | `68e7d15` |
| Verified evidence SHA | `68e7d15` (code, tests and docs are one SHA -- the closeout landed as source, not as a docs-only delta) |
| Suite | **1008 passed, 3 skipped, 3 xfailed, 0 failed** -- reproduced in a clean worktree with `-rsxX` (746s), then independently confirmed by GitHub Actions run `36804255801` (13/13 jobs) on the same SHA |
| Canonical environment | Python 3.11.15 . pytest 9.1.1 . clean worktree at `68e7d15` . `MAIA_EMBED_FORCE_HASH=1` |
| Absent by design | `LIVE_TESTS!=1` (1 skip, live LLM) and `MAIA_POSTGRES_DSN` (2 skips, durable checkpoints). No longer skipped at `68e7d15`: `llama-index` and `opentelemetry.sdk` are now installed, which is why the skip count fell from 7 to 3. |
| CI | **GREEN 13/13** -- run `36804255801` at `68e7d15`. Every job reaches a conclusion, including `Notify on completion` |
| Type gate | pyright **0 errors, 0 warnings**, no optional-dep suppressions |
| Azure | LIVE -- `https://ca-maia-api.wittysand-b748274c.eastasia.azurecontainerapps.io` -- **still on an older image; runtime identity NOT yet reconciled with `68e7d15`** |

**Why the skip count fell.** The earlier 7 skips were 1 llama-index, 2
PostgreSQL, 3 opentelemetry and 1 live-LLM. At `68e7d15` only the PostgreSQL and
live-LLM skips remain. That is a reduction in what is *not* covered, so it is an
improvement -- but the 3 that remain are opt-in integrations skipped by design,
not defects, and none of them is evidence of a passing capability.

**Superseded counts -- do not publish any of these.** `997` (dirty worktree, not
reproducible from a clean tree), `983/7/3` at `a80e330`, and `987` at `2d2eaf9`.
They are also not comparable to the current figure: all three predate
`_canonical_texts()` in `eval.py`, which changed what the evidence metrics
measure (see 1.3).

**Deployed canonical.** `a82f24b` = `68e7d15` + error-path fix (live-probe find:
`llm.mode` crash when the stack is down) + 1 regression test. Same eval
semantics as `68e7d15`; CI unit-tests 998 (CI skip profile) GREEN 13/13, Azure
revision `ca-maia-api--0000006` at 100% traffic live-probed. Test counts are
only publishable with their SHA + environment; do not compare raw counts
across envs.

### 1.1 RAG over a tenant-scoped corpus

| Field | Value |
|---|---|
| Evidence | `src/maia/retriever.py`, `src/maia/vector_store.py`, `eval/golden/*.jsonl` (10 files), `gate8b_rag_quality.py` |
| Verified SHA | `38189ca` |
| Runtime status | **VERIFIED** |
| CV wording | "Hybrid retrieval (dense + BM25/RRF) over a tenant-scoped Qdrant collection, with a corpus-grounded golden-set benchmark." |
| Limitation | Golden-set chunk IDs are recorded as "not resolvable against a fresh ingest" at `gate8b_rag_quality.py:153`, so the gate scores on `gold_keywords`, not chunk identity. Do not claim chunk-level gold precision. |
| CV usable | **YES** |

### 1.2 Answerability / abstention gate

| Field | Value |
|---|---|
| Evidence | `src/maia/eval.py`, `eval/audit_eval_rows.py`, `tests/test_golden_eval.py` |
| Verified SHA | `38189ca` |
| Runtime status | **PARTIAL** |
| Measured | `audit_eval_rows.py --only no_answer` → `rows=9 usable=0 unusable=8 rejected=1 conflict=0` |
| CV wording | "Built an abstention gate with a corruption-resistant label-review pipeline, where corpus verification retires labels instead of trusting prose." |
| Limitation | **The abstention metric is unmeasured, not passing.** Zero no-answer rows are usable. No refusal-accuracy, false-refusal rate, or confusion matrix may be quoted. Never write "N% refusal accuracy". |
| CV usable | **PARTIAL** — the design and review process are defensible; the performance number is not |

This is the most important limitation in the portfolio. It is written down
here so it cannot be quietly dropped later.

### 1.3 Multi-tenant isolation

| Field | Value |
|---|---|
| Evidence | `tests/test_tenant_authorization.py` (10 tests), `src/maia/api.py` |
| Verified SHA | `38189ca` |
| Runtime status | **VERIFIED** |
| CV wording | "Enforced per-request tenant scoping at the retrieval and authorization layer, with negative tests that a wrong-tenant request is rejected." |
| Limitation | Verified by test and source review. No live multi-tenant Azure traffic was captured. |
| CV usable | **YES** |

### 1.4 Human-in-the-loop approval for side effects

| Field | Value |
|---|---|
| Evidence | `tests/test_approval.py` (9 tests), `src/maia/agent/` |
| Verified SHA | `38189ca` |
| Runtime status | **VERIFIED** |
| CV wording | "Agent actions that cause side effects pass through an approval gate before execution." |
| Limitation | Covered by unit tests. Do not claim approval latency or concurrency guarantees; none were measured. |

### 1.5 Agent tool use

| Field | Value |
|---|---|
| Evidence | `src/maia/agent/langgraph_agent.py`, `src/maia/mcp/{bridge,client,protocol}.py`, `src/maia/mcp/servers/`, `tests/test_mcp_*.py` (6 files) |
| Verified SHA | `38189ca` |
| Runtime status | **VERIFIED** |
| CV wording | "LangGraph agent orchestrating retrieval and MCP tool calls." |
| Limitation | The LlamaIndex data-plane adapter is opt-in behind `LLAMA_INDEX_DATA_PLANE`; `llama_index` is not installed in the canonical env, so `tests/test_llama_index_dataplane.py` skips (declared via `importorskip`). Do not claim the LlamaIndex path runs in CI. |
| CV usable | **YES** for the LangGraph/MCP path |

### 1.6 SSE streaming

| Field | Value |
|---|---|
| Evidence | `src/maia/streaming.py`, `tests/test_chat_stream_sse.py` (16 tests: STREAM-001..015 + no-orphan-threads), `tests/test_p1a_stream_ratelimit.py`, `docs/evidence/stream_closeout.md` (MAIA repo), mutation controls M1/M2, CI 13/13 green (run `36804255801` at `68e7d15`) |
| Verified SHA | `68e7d15` |
| Runtime status | **VERIFIED** |
| CV wording | "Implemented authenticated multi-tenant SSE response streaming with grounded citations, disconnect-aware delivery cancellation, partial-write discard, and approval-safe agent execution." |
| Limitation | Provider token streaming is provider-dependent (deterministic chunked fallback otherwise); sync `agent.chat()` compute cannot be force-cancelled mid-call (delivery stops, partial writes discarded). TTFT numbers are MOCK orchestration evidence only — never quote as production LLM latency. No concurrent-client load test; do not quote throughput. |
| CV usable | **YES** |

### 1.7 Azure Container Apps deployment

| Field | Value |
|---|---|
| Evidence | probe `HTTP=200`, `sha256=82e530f7dc4baa2f...` @ 2026-09-30T13:30:00Z, recorded in `docs/PORTFOLIO-COMPLETION-AUDIT-v2.md` |
| Verified SHA | `38189ca` |
| Runtime status | **VERIFIED** (health endpoint) |
| CV wording | "Deployed to Azure Container Apps with a live health endpoint." |
| Limitation | Only `/health` was re-probed for MAIA in this session. A full business-endpoint runtime matrix was captured for Factory, not MAIA. Do not claim all live endpoints were verified for MAIA. |
| CV usable | **PARTIAL** — say "deployed with a live health endpoint", never "all endpoints verified in production" |

### 1.8 Citation reliability — do not claim

| Field | Value |
|---|---|
| Evidence | `tests/test_golden_eval.py` citation assertions |
| Verified SHA | `38189ca` |
| Runtime status | **UNRESOLVED FLAKE** |
| CV wording | *omit entirely* |
| Limitation | A citation test flaked across 2 runs, then passed 2 runs on earlier SHAs. It was tracked as `maia-citation-flake` (P1). The current SHA is green, but the flake was never root-caused to a mechanism. Do not claim citation determinism. |
| CV usable | **NO** — nothing to gain, something to lose |

---

## 2. Enterprise-IT-Helpdesk-Lab — enterprise AI / automation

| | |
|---|---|
| Docs HEAD | `37c5c0d8e0096bcf84fac4a7bae807311863f92b` |
| Verified evidence SHA | `37c5c0d8e0096bcf84fac4a7bae807311863f92b` |
| Python suite | 156 passed, 4 xfailed (6.23s) |
| Node portal | 336 passed, 1 skipped (337 cases) — `npm test` |
| API smoke | 68/68 passed — `./test-api.sh` |
| Azure | **VERIFIED REACHABLE** — public Azure Container App `ca-helpdesk-portal`, revision `ca-helpdesk-portal--0000001`, `GET /api/health` → `200 OK` in 0.31s on 2026-10-01. Committed artifact: `Enterprise-IT-Helpdesk-Lab/docs/evidence/azure/health-probe-2026-10-01.json`. See §2.6 for the full disclosure and the boundary. |

The three suites are separate runners and are **not summable**. The earlier
`336/337` header badge implied otherwise and was removed.

### 2.1 Fail-closed safe-automation gateway — the strongest claim here

| Field | Value |
|---|---|
| Evidence | `llm-gateway/automation/{models,policy,gateway,executor}.py`, `docs/SAFE-AUTOMATION.md`, `llm-gateway/tests/test_automation_*.py` (19 passed, 4 xfailed, 12 scenarios), `docs/evidence/helpdesk/safe-automation/safeauto-20260930-122611-6d6a01.json` |
| Verified SHA | `c6671ce` |
| Runtime status | **VERIFIED** (evidence JSON `status: VERIFIED`, `mutations_caught: 4/4`) |
| CV wording | "Built a fail-closed automation gateway where the LLM proposes a structured action ID plus typed parameters, policy authorizes it, high-risk actions require human approval, and a deterministic executor maps action IDs to fixed script argv — the proposal schema has no field that can carry a shell command." |
| Limitation | Verified against a stub runner; real PowerShell execution was not performed against a live domain. Do not claim it ran against a production AD. |
| CV usable | **YES** |

The load-bearing detail is negative, and it is why the claim holds:
`models.py` has no `command`/`powershell`/`script` field at all, and 9 known
smuggling field names are rejected explicitly. The executor builds argv lists
with `shell=False`. Policy and parameter validation sit *behind* that boundary
as defence in depth, not the boundary itself.

### 2.2 RBAC and approval controls

| Field | Value |
|---|---|
| Evidence | `llm-gateway/automation/policy.py` (`AUTOMATION_ROLES`, fail-closed `authorize`), `llm-gateway/tests/test_automation_gateway.py::test_t09_unauthorized_actor_denied` |
| Verified SHA | `c6671ce` |
| Runtime status | **VERIFIED**, with a negative-control proof |
| CV wording | "Fail-closed RBAC: unauthenticated actors and roles outside `IT_ADMIN`/`HELPDESK_L2` are denied before any executor call, and an approval token is single-use and invalidated if parameters change after approval." |

### 2.4 Mutation-verified safety tests

| Field | Value |
|---|---|
| Evidence | `llm-gateway/tests/test_automation_mutations.py` (M1–M4), reported as `xfailed` in the suite output |
| Verified SHA | `c6671ce` |
| Runtime status | **VERIFIED** (4/4 mutations caught) |
| CV wording | "Each safety test is mutation-checked: demoting a HIGH_RISK action to READ_ONLY, allowing an unknown action, widening RBAC so a VIEWER can execute, and accepting a raw PowerShell path are each re-introduced to prove the suite catches them." |
| Limitation | These report as `xfail`: they pass by catching a deliberately injected defect. If they were ever marked as ordinary tests, the evidence would disappear. |
| CV usable | **YES** — but be ready to explain the mechanism, because `xfailed` reads as a failure to anyone who does not know what it means here |

### 2.5 REST API surface and smoke coverage

| Field | Value |
|---|---|
| Evidence | `internal-portal/test/*.test.js` (32 files), `internal-portal/test-api.sh` |
| Verified SHA | `c6671ce` |
| Runtime status | **VERIFIED** (336 passed / 1 skipped; 68/68) |
| CV wording | "~40 REST routes with a 336-case Node test suite and a 68-check curl smoke suite, both run in CI." |
| Limitation | The single skip is deliberate and named. Never present 336+1 as 337 passing. |
| CV usable | **YES** |

### 2.6 Documented capability gaps — state these, do not fix them for CV

| Field | Value |
|---|---|
| Evidence | `grep` over `internal-portal/python-portal/app.py` and `internal-portal/server.js`; audit P2 `helpdesk-capability-gaps` |
| Verified SHA | `c6671ce` |
| Runtime status | **NOT IMPLEMENTED — by decision** |
| Gaps | (1) no role-based access on portal routes; (2) no alert/ticket dedupe — `grep dedup` returns 0 in both Node and Flask; (3) SLA is `slaPercent` only, with no clock-based breach logic |
| CV wording | "Known gaps, stated rather than papered over: portal routes are unauthenticated, alerts are not deduplicated, and SLA reporting is a completion percentage rather than a breach clock." |
| Limitation | Do not claim RBAC across the product, alert deduplication, or business-hours SLA. Each was left unimplemented on purpose rather than built to fill a checklist. |
| CV usable | **YES, as a limitations statement.** A documented gap is defensible; an implied capability is not. |

### 2.7 Deployment and cloud

| Field | Value |
|---|---|
| Evidence | `Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml` (Node 20/22 matrix); live probe artifact `Enterprise-IT-Helpdesk-Lab/docs/evidence/azure/health-probe-2026-10-01.json` |
| Verified SHA | `c6671ce` (local evidence); deployment probed 2026-10-01 |
| Runtime status | **VERIFIED REACHABLE** — Azure Container App `ca-helpdesk-portal`, revision `ca-helpdesk-portal--0000001`, `GET /api/health` → `200 OK` in 0.31s |
| CV wording | "CI-tested containerised internal portal, deployed to Azure Container Apps and verified reachable on a live revision." |
| Limitation | Live and reachable, but the boundary is narrow and must be disclosed whenever the deployment is described: `authMode` is `"lab"`, not production identity, and `webhook` is `"mock"`, so notification delivery is stubbed. The probe was health-only and sent no credential. The PowerShell / Active Directory paths are **NOT_RUN** (`Enterprise-IT-Helpdesk-Lab/docs/SAFE-AUTOMATION.md:52-53`), and the Python automation gateway is proven by pytest only — it is not wired to any HTTP route in the running portal. |
| CV usable | **PARTIAL** — say "verified reachable on a live Container App" plus the `authMode: lab` / mock-webhook boundary; do not say production identity or that the automation gateway ran in the cloud |

| Limitation | These role checks apply to the automation layer only. See 2.6 — the portal routes themselves have no role model. |
| CV usable | **YES** for the automation layer |

Mutation proof: replacing `authorize()` with an unconditional allow caused
`test_t09_unauthorized_actor_denied` to fail. The test has teeth.

### 2.3 Deterministic PowerShell executor

| Field | Value |
|---|---|
| Evidence | `llm-gateway/automation/executor.py` (`ACTION_SCRIPT` allowlist, argv-only), `scripts/*.ps1` (8 scripts), `scripts/verify-ps1-syntax.sh` |
| Verified SHA | `c6671ce` |
| Runtime status | **VERIFIED** |
| CV wording | "Mapped 8 allowlisted action IDs to fixed script paths with argument binding only — no string interpolation, no `shell=True`, and path inputs confined to approved roots with shell metacharacters rejected." |
| Limitation | `pwsh` was not executed against a live environment in this verification; the allowlist and argv construction are asserted, the OS-level effect is not. |
| CV usable | **YES** for the design and its tests |


---

## 3. Factory-Data-Automation-v-AI-Reporting-Platform — data platform

| | |
|---|---|
| Docs HEAD | `77e5d140b951c27bd1e141bbd55eb4457f8e9e0d` (HEAD moved past verified-test SHA via parallel track: quarantine fix `be4ace3` + redeploy record; see that track's docs) |
| Verified test SHA | `70509fc2e3bf58a0f9bf48bd2f348ce4dc6c6f19` (CI GREEN run `36741560374`; vs `4ea75e5`: ruff/format/allowlist only, no runtime behavior change) |
| Deployed source SHA | **Two recorded identities, not merged:** `4ea75e599200a50a87b0a235b9a5b3de9117dad3` (per the original audit of this map) and `be4ace3ad14330cb92c40d5a63fa27c0544ae0ac` (per the repo README identities table, which also carries the cumulative-DLQ quarantine-rate fix). Both contain the gate. Which revision serves traffic **now** is NOT VERIFIED from committed material — re-probe the revision name before publishing one. |
| Artifact digest | `sha256:e792a653…` (→ `4ea75e5`) and `sha256:7c3e81b7…` (→ `be4ace3`, CI buildx provenance, SLSA v1 attestation verified) |
| Runtime revision | `ca-factory-api--gate` (weight 100, RunningAtMaxScale) and `ca-factory-api--0000002` (weight 100, 1 replica) — treat as NOT reconciled |
| Suite | **290 passed, 5 skipped, 4 xfailed** at the deployed SHA `be4ace3`; **288 passed, 5 skipped, 4 xfailed** at the earlier verified-test SHA `70509fc`. See the two-identity note below — publish the figure with its SHA, never bare. |
| Azure | LIVE — `https://ca-factory-api.wittysand-b748274c.eastasia.azurecontainerapps.io` |

**Deployed and verified.** The gate shipped: a live revision runs a commit that
contains the gate and `/api/v1/report` returns the gate's own fields. The
earliest revision on `4e4e8b58` was superseded and takes no traffic. Which of
the two recorded deployed SHAs (`4ea75e5` or `be4ace3`) is the one currently
serving traffic is **NOT VERIFIED** from committed material; re-probe the
revision name before publishing either.

**Identities, deliberately not merged.** The deployed SHA, the verified test SHA
and the docs HEAD all differ, and that difference is the honest state:

| Scope | Identity | Status |
|---|---|---|
| Azure runtime availability (container, data load, health, business endpoints, metrics) | `4ea75e5` / `ca-factory-api--gate` / `sha256:e792a653…` **or** `be4ace3` / `ca-factory-api--0000002` / `sha256:7c3e81b7…` — not reconciled | **VERIFIED** (that a live revision serves); **NOT VERIFIED** (which one) |
| Phase C quality gate (gate, quarantine authorization, grounded evidence, provenance, blocked-report semantics, JSON-safe boundary) | `9192da1` (gate `2477cf8`) | **VERIFIED local + test HTTP surface** |
| The same gate on the live Azure revision | — | **NOT VERIFIED** |

The 4 `xfailed` are mutation-detection controls `M1`–`M4`. They are **expected to
fail**: each one neutralizes a gate check and asserts the gate then lets bad data
through, so a passing mutation control means the mutation was not actually
neutralized. They are neither failures nor part of the pass count. Counts from
earlier phases (213, 231, 242, 258) are historical evidence only.

**288 and 290 are two SHAs, not two claims.** `70509fc` (verified test SHA, CI
GREEN run `36741560374`) measured 288. Its descendant `be4ace3` added exactly two
regression tests to `tests/test_reporting_boundary.py` for the cumulative-DLQ
quarantine-rate defect, and that is the SHA the running image was built from, so
it measures 290. `be4ace3` supersedes `70509fc`; 288 is history, not a rival
figure. The canonical published statement carries both: **290 at `be4ace3`**
(deployed), **288 at `70509fc`** (earlier verified-test SHA). The count is
reconciled from the committed trees, not chosen — do not restate a bare number
without its SHA.

### 3.1 ETL pipeline with quarantine and contracts

| Field | Value |
|---|---|
| Evidence | `app/etl/{pipeline,medallion,kpi_engine}.py`, `app/data_contracts/{validator,schemas}.py`, `tests/test_quarantine.py`, `tests/test_contracts.py`, `tests/test_contract_gate_fail_closed.py` |
| Verified SHA | `4e4e8b5` |
| Runtime status | **VERIFIED** |
| CV wording | "RAW→SILVER→GOLD pipeline with schema validation, row-level quarantine for bad records, and a fail-closed contract gate on downstream artefacts." |
| Limitation | The quality gate is committed and verified locally (see 3.8) but is **not** in the deployed image. Say the pipeline and contract gate are verified; do not imply the deployed service runs the gate. |
| CV usable | **YES** for the pipeline and contract gate |

### 3.2 Deterministic fixture generation

| Field | Value |
|---|---|
| Evidence | `scripts/generate_sample_data.py`, `scripts/verify_fixture.py`, `docs/expected/factory-fixture.json`, CI step in `.github/workflows/ci.yml` |
| Verified SHA | `4e4e8b5` |
| Runtime status | **VERIFIED** |
| Measured | `FIXTURE VERIFY: PASS schema=factory-fixture-v1 seed=42 days=90 datasets=5 total_rows=26119` |
| CV wording | "Seeded, reproducible sample-data generator with a blessed-fixture check in CI, so a data change that alters row counts or hashes fails the build." |
| Limitation | Needs `faker`, which is in `requirements.txt` but **not** `requirements.api.txt`; the image installs it only for the build step. `tests/conftest.py` has a stdlib fallback that seeds a *smaller* dataset, so the suite count holds only against the blessed fixture. |
| CV usable | **YES** |

### 3.3 Self-contained container image

| Field | Value |
|---|---|
| Evidence | `Dockerfile.api` (copies `scripts/`, generates fixture at build, asserts `data/raw/*.csv` non-empty), `tests/test_api_deployment_integrity.py` (16 tests) |
| Verified SHA | `4e4e8b5` |
| Runtime status | **VERIFIED** (built and run locally) |
| CV wording | "The API image generates and self-validates its own fixture at build time and fails the build rather than shipping an empty dataset." |
| Limitation | Verified locally in Docker. The deployed digest is a separate artefact (3.6). |
| CV usable | **YES** |

### 3.5 Azure Container Apps deployment — the strongest infra claim

| Field | Value |
|---|---|
| Evidence | `docs/evidence/factory_p1_triage.md`, runtime probes `HTTP=200` @ 2026-09-30T15:25Z, revision `ca-factory-api--gate` |
| Verified SHA | `4ea75e5` (source) → `sha256:e792a653…` (artefact, via buildx attestation) → `ca-factory-api--gate` (runtime) |
| Runtime status | **VERIFIED** — 6/6 endpoints 200, `factory_data_up 1`, 5 datasets / 26 132 rows |
| CV wording | "Deployed a manufacturing data API to Azure Container Apps with a traceable chain — commit → CI build provenance → image digest → revision — and probed every business endpoint live against real data." |
| Limitation | Write it as a chain, not an equation: the commit is not a hash of the digest, and provenance is what links them. A docs-only commit does not move the deployed image. |
| CV usable | **YES** |

### 3.6 Fail-closed health semantics

| Field | Value |
|---|---|
| Evidence | `app/api/main.py::data_is_loaded`, `tests/test_api_deployment_integrity.py`, negative control recorded in `docs/evidence/factory_p1_triage.md` |
| Verified SHA | `4e4e8b5` |
| Runtime status | **VERIFIED** |
| CV wording | "Found a false-positive health check where an empty dataset left the service reporting healthy while three business endpoints returned 500; health now returns 503 when no data is loaded, and the fix is mutation-tested — reverting it fails 9 of 16 tests." |
| Limitation | This is the strongest interview story here, but it is a bug found and fixed, not a capability present from the start. Present it that way. |
| CV usable | **YES** |

### 3.7 API-key authentication

| Field | Value |
|---|---|
| Evidence | `app/api/security.py::ApiKeyMiddleware` (open-mode bypass at `:35-36`, safe-path check, `X-Factory-API-Key` read at `:40`, 401 body at `:45`), `tests/test_api_key_auth.py`, container matrix (no key 401 / wrong key 401 / correct key 200) |
| Verified SHA | `4e4e8b5` (local + container); live-deployment rejection re-probed 2026-10-01 |
| Runtime status | **VERIFIED** (guard rejects unauthenticated requests against the live revision) · **NOT VERIFIED** (authenticated success path against the live secret) |
| CV wording | "API-key guard using `secrets.compare_digest` with an explicit safe-path list; verified in tests, and verified on the live deployment to reject unauthenticated requests with `401`." |
| Limitation | The live guard is **enforcing**, which proves the secret is provisioned: `GET /api/v1/kpis` → `401 {"detail": "missing or invalid X-Factory-API-Key"}`, and `GET /api/v1/metrics-evil` → `401` rather than `404` (in the documented open mode a missing route would return 404). The probe deliberately sent no credential, so the **success path against the live secret is NOT VERIFIED** — it is verified in `tests/test_api_key_auth.py` only. There is also no per-role RBAC. Never write "the production API is protected" or anything implying a full authenticated round-trip was exercised. |
| CV usable | **PARTIAL** — claim the rejection boundary and the header name, not a completed authenticated flow |

### 3.8 Quality-gated AI reporting — implemented AND deployed

| Field | Value |
|---|---|
| Evidence | `app/data_contracts/quality_gate.py`, `app/ai/reporting.py` (`_json_safe`, `extract_kpi_evidence`, `validate_provenance`, `_blocked_report`, `_finalize_report`), `app/api/main.py` (`_quality_context`), `tests/test_ai_quality_gate.py` (19), `tests/test_ai_quality_mutations.py` (M1–M4), `tests/test_reporting_boundary.py` (9) |
| Verified SHA | `9192da1` (gate implementation `2477cf8`) |
| Deployed SHA | `4ea75e5` — revision `ca-factory-api--gate`, digest `sha256:e792a653…` |
| Runtime status | **VERIFIED** local · **VERIFIED on Azure (GOOD path)** · **NOT RUN** on Azure (BAD/UNKNOWN branch) |
| Measured on Azure | `/api/v1/report` returns `generation_mode`, `quality`, `evidence`, `provenance`; `quality.status = GOOD` with 5/5 checks ok; `evidence` carries 5 sources + 8 KPI names; `provenance` has 13 entries with 0 rejected; `generation_mode = FALLBACK` |
| CV wording | "Built and deployed a deterministic data-quality gate that authorises AI reporting only from validated KPI evidence, with quarantine-aware blocking and structured provenance — observed running on the live Azure deployment." |
| Limitation | The live gate was observed on the **GOOD path only**. The BAD/UNKNOWN fail-closed branch was not triggered against the live deployment, to avoid corrupting demo data; it is verified in the HTTP regression suite and in a local container. Also note `generation_mode = FALLBACK`: the gate authorised the report, but no LLM generated it, because no Ollama endpoint is reachable from the container. Never present FALLBACK output as real model output. |
| CV usable | **YES** for the gate and its deployment · the cloud *blocking* behaviour is **NOT RUN** |


### 3.4 CI regression gate for deployment integrity


### 3.9 Kubernetes — do not claim

| Field | Value |
|---|---|
| Evidence | 108 files tracked under `k8s/`, including `40-factory-api-real.yaml` |
| Verified SHA | in the tree, never applied |
| Runtime status | **NOT DEPLOYED / NOT RUN** |
| CV wording | *omit* |
| Limitation | No cluster was provisioned and no manifest was ever applied. The README makes no Kubernetes claim and neither may the CV. Azure Container Apps is the deployed platform. |
| CV usable | **NO** |

### 3.10 Prometheus alert firing

| Field | Value |
|---|---|
| Evidence | `observability/prometheus/alerts.yml` (6 rules), `observability/alertmanager/alertmanager.yml` |
| Verified SHA | `4e4e8b5` |
| Runtime status | **NOT_RUN** |
| Measured | the API serves `factory_alerts_active 16`, but no alert was observed firing through Prometheus/Alertmanager |
| CV wording | "Six Prometheus alert rules defined" — the word *defined* is load-bearing. |
| Limitation | Rules exist and the application-level alert engine is real; the Prometheus→Alertmanager path was never observed end to end. |
| CV usable | **PARTIAL** |

### 3.11 KPI engine — correct an existing overclaim

| Field | Value |
|---|---|
| Evidence | `app/etl/kpi_engine.py::calculate_all_kpis` |
| Verified SHA | `4e4e8b5` |
| Runtime status | **VERIFIED** |
| Measured | 8 KPI groups: `daily_production`, `weekly_production`, `monthly_production`, `oee`, `machine_utilization`, `worker_productivity`, `inventory_kpi`, `defect_analysis`. Together 65–68 metric columns on the blessed fixture, varying with the data because `defect_analysis` expands per distinct `Defect_Type`/`Severity`. |
| CV wording | "8 implemented KPI groups — OEE, machine utilization, worker productivity, defect analysis — expanding to roughly 65–68 metric columns depending on the data." |
| Limitation | The README said "84+ KPIs"; that was an unverified estimate and is now corrected in `4ea75e5`. `docs/RECRUITER-EVIDENCE.md` had already flagged it as unverifiable. Do not resurrect "84+". |
| CV usable | **YES, at 8 groups** — corrected in the repo, so it is safe to state |

---

## 4. CV-ready determination

Derived from this map, not from the completion score.

| Repo | CV usable | Why |
|---|---|---|
| MAIA | **PARTIAL** | 5 of 8 claims YES; the Azure claim is health-endpoint only, the abstention gate is a **MEASURED DEFECT** (1 of 9 labelled no-answer queries refused, n=9, distributions overlap so no similarity threshold separates the classes), and citation reliability is an unresolved flake. See `../MAIA/eval/README.md:16-40` and `evidence/e2e/gate8b-abstention.json` |
| Enterprise-IT-Helpdesk-Lab | **PARTIAL** | Safe automation, RBAC and API surface are strong; three capability gaps are documented. A live Azure Container App is verified reachable (`ca-helpdesk-portal--0000001`, probe artifact `Enterprise-IT-Helpdesk-Lab/docs/evidence/azure/health-probe-2026-10-01.json`), but `authMode` is `"lab"`, the webhook is `"mock"`, the PowerShell / AD paths are NOT_RUN, and the automation gateway is not HTTP-wired in the running portal |
| Factory | **PARTIAL** | Deployment, health semantics and the quality gate are all VERIFIED on the live Azure revision, and the API-key guard is verified to REJECT unauthenticated requests there (401 on a business endpoint and on a non-existent path, 2026-10-01 probe). Still not claimable: an authenticated round-trip against the live secret, Kubernetes is NOT DEPLOYED, the gate's *blocking* branch was not triggered against live data, and `generation_mode` is FALLBACK rather than a real model |

No repo reaches `CV=YES`, and none should until the named gaps close. That
does **not** block applying: `CV=YES` is a strict bar meaning every major
claim is fully evidence-backed, and the correct move is to select 3–4 YES
claims per repo for the CV rather than hold the whole portfolio back.

The "84+ KPIs" correction landed in `4ea75e5`, so the highest-value remaining
fix is deploying `2477cf8` to make the quality-gate claim match production.

## 5. Claims that must not appear

| Claim | Why |
|---|---|
| Any previously published MAIA test count (`967 @ 38189ca`, `997 @ dd39026`, `983/7/3 @ a80e330`, `987 @ 2d2eaf9`) | All NON-CANONICAL. `997` came from a dirty worktree and is not reproducible from a clean committed tree. `983/7/3` and `987` predate `_canonical_texts()`, so their evidence metrics measured a 600-char UI excerpt instead of the canonical chunk text, which makes them not comparable. Publishable: **`1008/3/3 @ 68e7d15`** (eval-canonical milestone, clean tree) and **CI 998 @ `a82f24b`** (deployed canonical = `68e7d15` + error-path fix, CI skip profile, live-probed on Azure) — each only with its SHA + environment, never compared raw across envs. |
| Any refusal-accuracy % for MAIA | 0 usable no-answer rows; the metric is unmeasured |
| "Production API is protected" for Factory | the live guard rejects unauthenticated requests (401 on a business endpoint and on a non-existent path, 2026-10-01), but no authenticated round-trip was exercised — the probe sent no credential. Say the rejection boundary, never a completed authenticated flow |
| "The live cloud blocked bad data" for Factory | the gate ran on the live GOOD path; the BAD/UNKNOWN branch was never triggered against the deployment |
| "A real LLM generated the reports" for Factory | `generation_mode = FALLBACK`; no Ollama endpoint is reachable from the container |
| "Runs on Kubernetes" for Factory | 108 manifests, zero applied |
| "84+ KPIs" | the code computes 8 |
| "Alerts firing" for Factory | rules defined, never observed firing |
| "Role-based access" for the Helpdesk portal | no role model exists on portal routes |
| "SLA breach tracking" for Helpdesk | `slaPercent` is a completion percentage |
| "337 tests passing" for Helpdesk | that is the Node suite alone (336 pass, 1 skip) |
| "Deployment integrity regression caught in CI" | the job is committed but has not been observed green on a hosted run |

| Field | Value |
|---|---|
| Evidence | `.github/workflows/ci.yml` job `container-smoke` (7 steps) |
| Verified SHA | `4e4e8b5` |
| Runtime status | **PARTIAL** — workflow validated, not yet observed completing on a hosted run for this SHA |
| CV wording | "Added a CI job that builds the image from a clean clone and calls the business endpoints, because the pytest suite generates data before it runs and so cannot catch a missing-data deployment." |
| Limitation | Do not claim it has blocked a bad build; that has not been observed. |
| CV usable | **PARTIAL** — describe the gap you closed, not a CI catch |

| Azure | LIVE — `https://ca-maia-api.wittysand-b748274c.eastasia.azurecontainerapps.io` |
