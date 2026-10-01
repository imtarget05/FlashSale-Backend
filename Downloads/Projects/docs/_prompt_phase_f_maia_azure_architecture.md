# PHASE F — ENTERPRISE AZURE ARCHITECTURE FOR MAIA

_Established 2026-10-01. Workspace: `/Users/mainguyenbinhtan/Downloads/Projects`_
_Replaces the retired `docs/HISTORICAL-AUDIT-PROMPT.md`._

---

## 0. READ THIS FIRST — what is true right now

This phase starts from a **verified baseline**, not from an audit. Do not
re-derive it. If a number below disagrees with what you measure, the
measurement wins and the baseline is wrong — say so, do not paper over it.

**MAIA is the only project allowed to change.** Factory and Helpdesk are
`FROZEN`. Do not edit their source, workflows, tests, Azure resources, or
documentation. If MAIA's work seems to want a change in them, stop and report
instead.

### MAIA status, exactly as it stands

```text
canonical source     68e7d15
CI                   GREEN 13/13
Azure source         68e7d15   (digest-pinned, provenance verified)
artifact             immutable digest
provenance           VERIFIED
cold/warm behaviour  measured (21.8 s cold / 0.19–0.30 s warm)
SSE implementation   CI VERIFIED
SSE on Azure         NOT VERIFIED / AUTH_BLOCKED
/ready semantics     OPEN DEFECT
durable state        NOT PRESENT
```

Full identity detail:

```text
code_sha            68e7d15f506de2497183af659087e611af560f79
CI                  GREEN 13/13, run 36804255801
full regression     1008 passed / 3 skipped / 3 xfailed / 0 failed
                    (clean worktree, -rsxX; 3 skips are opt-in integrations)
type gate           pyright 0 errors, no optional-dep suppressions
image build         run 36804255788, green
image index digest  sha256:5d603584ff85a8270082583ee1e61dac38ab22d6e2e3009339228ffadd098323
platform manifest   sha256:e080968afdbda67cd5f8095c46d24fd5afb0cf713b214700496b18341205ad79
attestation         sha256:f17855df1fd96e66d6e538d38ff081d00bce4331f97c24fdba0713dac90e88af
provenance          VERIFIED, SLSA v1, subject = platform manifest,
                    vcs:revision = 68e7d15f506de...
Azure               ca-maia-api, rg rg-portfolio-evidence
revision            ca-maia-api--0000005, traffic 100%
pinned by digest    YES (not by tag)
minReplicas/max     0 / 1
```

Three digests are three different objects. **Never merge them into one
"digest".** Azure records the index; SLSA binds the platform manifest.

### Frozen for reference

```text
Factory    canonical tests be96306, deployed be4ace3, rev ca-factory-api--0000002, CI 8/8
Helpdesk   canonical evidence c6671ce, no cloud by design
```

---

## 1. OPEN DEBT — close this before adding anything

These are real, measured, and already recorded in
`docs/evidence/final/portfolio-completion-audit.json` under
`open_debt_before_phase_f`. Phase F that leaves them open is not finished,
regardless of how many Azure services it adds.

### 1.1 SSE contract is AUTH_BLOCKED on Azure

The `meta → token → citations → done` contract is verified in **CI only**
(`tests/test_chat_stream_sse.py`, 16 tests). It is **NOT verified on Azure**.

Blocker: the live probe needs authentication, and the demo password lives in
an Azure Container Apps secret, which is **write-only and cannot be read
back**. This is an architecture problem, not a probing inconvenience, and
Wave 1 exists to fix exactly this class of thing.

Do not mark SSE verified on Azure until a real authenticated stream has been
observed end to end.

### 1.2 `/ready` returns HTTP 200 while reporting `degraded`

```text
GET /health  200  {"status":"ok","version":"0.4.0"}
GET /ready   200  {"status":"degraded", error: cannot reach Qdrant at localhost:6333}
```

`QDRANT_URL` is not set in the container env, so it defaults to
`localhost:6333`, which does not exist in the self-contained image. This is
**pre-existing, not a regression from the closeout**.

This is the same class of defect as the fake `threshold-regression` CI gate
that was found during closeout: **a check whose pass signal cannot fail.**
Any uptime monitor keyed on HTTP 200 calls this app healthy while its vector
store is unreachable.

Fix it properly — pick one and justify it:
- configure a real vector store and make `/ready` genuinely pass;
- return **503** when degraded, so the code means something to a probe;
- if a vector-store-free demo is the intent, report a distinct status such as
  `demo-mode` instead of `degraded`, and document the intent.

### 1.3 No durable state

No `QDRANT_URL`, no Postgres. No vector store, no durable checkpoints. Waves 1–3
close this.


---

## 2. THE ONE RULE — no component without seven things

Do not create an Azure resource because a diagram would look complete. Every
component must come with **all seven**, or it does not get built:

1. **A business problem** — the concrete failure it prevents, in one sentence.
2. **A failure mode it solves** — what breaks without it.
3. **Infrastructure code** — Bicep, committed. Not a portal click.
4. **Runtime proof** — evidence it actually works, not that it was created.
5. **A security boundary** — what it may reach and what it cannot.
6. **Observability** — how you would know it broke before a user tells you.
7. **A cost/trade-off explanation** — what it costs and what it trades away.

If you cannot fill all seven, the answer is **do not build it**. Deleting an
unjustified component is a success in this phase.

### Rules carried over from the retired audit prompt — still binding

- Never turn `PARTIAL` or `FAILED` into a pass by skipping, lowering a
  threshold, deleting a test, or editing an assertion. This already bit us
  once: a CI gate that compared two numbers and never acted on the
  comparison, and a readiness endpoint returning 200 while degraded.
- **Diagram only what exists.** No Kubernetes, no service mesh, no component
  that is not deployed and probed.
- Evidence is **runtime**, never a config file read on its own. A workflow
  that exists is capability; a workflow observed green is evidence.
- Configuration that is not installed is not available. The pyright gate was
  red partly because the lint job never installed declared optional deps.

---

## 3. TARGET ARCHITECTURE

A target, not a one-shot plan. It becomes true wave by wave, and a box is
only drawn once it is real.

**Current status, stated honestly before the target:**

```text
✅ DEPLOYED + VERIFIED   MAIA API on ACA (rev 0000005, digest-pinned)
⚪ TARGET / PLANNED      everything else below
🟡 IMPLEMENTED / NOT DEPLOYED   (none yet)
```

```text
                    ⚪ [W5] Azure Front Door Premium + WAF
                          │
                    ⚪ [W5] Azure API Management
                          │
                    ⚪ [W5] Private Container Apps Environment
                          │
    ✅ DEPLOYED           ├── MAIA API
                          ├── ⚪ [W4] ingestion worker
                          └── ⚪ [W2] evaluation / maintenance jobs
                                │
                                ├── ⚪ [W3] Vector store
                                ├── ⚪ [W3] Azure PostgreSQL
                                ├── ⚪ [W3] Redis
                                └── ⚪ [W0/W3] model provider config
                    ⚪ [W4] Blob → Event Grid → Service Bus → Container Apps Job
```

Cross-cutting — all ⚪ except where noted:

```text
⚪ [W1] Managed Identity ──► Key Vault
⚪ [W1] GitHub Actions ──OIDC──► Azure RBAC
⚪ [W6] Bicep for everything
⚪ [W2] OpenTelemetry ──► App Insights ──► Log Analytics ──► Azure Monitor
⚪ [W5] VNet / Private Endpoint / Private DNS
```

**The ordering is deliberate.** Wave 0 fixes the app's ability to report its
own state before any platform is built on it. Identity and secrets next (W1)
so nothing later rests on a long-lived credential. Observability (W2) before
durable state (W3), so that work is observable *while* it happens. Network
hardening (W5) later among the runtime waves because it is the change most
likely to break the app, and you want telemetry before you pull that trigger.

---

## 4. WAVES — one at a time, with acceptance evidence each

**Do not start a wave until the previous wave is accepted.** Do not batch two
waves "to save time"; that is how you end up unable to attribute a break.

### Wave 0 — Runtime Contract Closeout   *(runs first, gates everything else)*

**Why this is first.** Do not add Key Vault, Entra or Front Door while the
running app can still emit a false health signal. A platform built on top of
an app that misreports its own state inherits that unreliability, and the
evidence chain stops being trustworthy at the bottom.

This is the same lesson Factory taught earlier in this portfolio: a green
health endpoint did not mean the business capability was usable.

#### W0.1 — `/health` and `/ready` must mean different things

```text
/health   = the process is alive
/ready    = this revision can receive intended production traffic
```

If Qdrant is a hard dependency of the serving path, then:

```text
Qdrant DOWN  →  /health 200, /ready 503
```

**Negative control, both directions — this is the acceptance test:**

```text
Qdrant reachable    → /ready 2xx
Qdrant unreachable  → /ready non-2xx
app still running   → /health stays 200
```

**Do not** fix this by renaming the body string from `"degraded"` to
`"ready"`. That inverts the defect: it makes the lie stronger. If the codebase
genuinely supports a mode that needs no Qdrant, then encode the dependency
policy explicitly — a declared list of `required` versus `optional`
dependencies that drives the status code — rather than leaving HTTP 200
ambiguous.

#### W0.2 — Determine *why* Azure has no `QDRANT_URL`

Do not merely change the status code. The missing configuration is a symptom;
decide which of these it is, **using runtime and code evidence, not a guess**:

```text
A. Qdrant is genuinely required in production
   → the missing config is a DEPLOYMENT DEFECT

B. Qdrant is intentionally optional for the cloud demo
   → the readiness POLICY is wrong/ambiguous, not the config

C. Cloud uses a different retrieval path
   → config, docs and runtime identity are DRIFTING
```

State which one, and show the evidence that decided it. The three lead to
different fixes, and guessing produces a green probe over a broken system —
precisely the failure this phase exists to prevent.

#### W0.3 — SSE stays `AUTH_BLOCKED`

Do **not** hack a token, read a secret out of band, or reset a credential
just to turn this line green. The current wording is correct and must stay:

```text
SSE implementation:  VERIFIED in CI
SSE on Azure:        NOT VERIFIED — authenticated runtime probe unavailable
```

Wave 1 supplies a real identity — Entra ID → token issuance → APIM/application
validation → authenticated `/chat/stream` — and only then is
`meta → token → citations → done` closed on Azure.

**Wave 0 acceptance:** `/health` and `/ready` have distinct, tested
semantics; the Qdrant question is answered A/B/C with evidence; the
correctness fix is deployed **by digest from a green CI run**; Azure is
probed in both the reachable and unreachable case.

### Wave 1 — Identity and secrets

Resolve **three distinct kinds of identity**, not just "put secrets in Key
Vault". Conflating them is what makes most portfolios vague:

```text
Human identity        → Microsoft Entra ID
Workload identity     → Managed Identity
CI/CD identity        → GitHub OIDC federation
Secret material       → Key Vault
```

Target:

```text
GitHub Actions
      │  OIDC — no long-lived Azure secret
      ▼
Microsoft Entra
      ▼
Azure RBAC
      ▼
Deploy

MAIA Container App
      │  Managed Identity
      ▼
Key Vault / Azure resources
```

Acceptance:
- no deployment credential is a static secret anywhere in the pipeline;
- GitHub Actions authenticates to Azure via OIDC federation, not a stored key;
- MAIA reads its secrets at runtime via managed identity;
- **this closes W0.3** — with a real identity you can obtain a token and
  finally observe the authenticated SSE contract on Azure.

**Acceptance requires the negative path, not just the happy path:**

```text
authorized identity    → allowed
wrong / no identity    → denied
revoked permission     → denied      (then restore RBAC)
```

Least privilege that was never tested is a guess, not a control.

### Wave 2 — Observability

```text
OpenTelemetry · Application Insights · Log Analytics · Azure Monitor · alerts
```

Must trace at least: `request → retrieval → model → SSE → persistence`.

Acceptance:
- a single request is followable across all five stages by correlation id;
- at least one alert is **observed firing**, not merely defined. MAIA already
  has a documented case of alert rules existing while never firing;
- W0.1 is closed by now; if the /health vs /ready contract is not yet
  verified in Azure, it is explicitly deferred here with a reason.

### Wave 3 — Durable state

```text
Azure Database for PostgreSQL · Redis
```

**Absence of durable state is not a P0 defect** if the application is
defined as stateless / demo-compatible. Do not see the gap in §1.3 and
immediately stand up PostgreSQL.

Wave 3 is only justified after writing the business invariant down:

```text
What must survive:
- container restart?
- revision replacement?
- scale-to-zero?
- failed request?
```

For example, if a session or checkpoint must outlive a revision:

```text
container replica dies
  → conversation / checkpoint remains
  → new replica resumes it
```

then PostgreSQL has a reason to exist. That is the reason. This is **not**:

> PostgreSQL, because enterprise architectures have a database.

`Redis` is justified only if the multi-replica rate limiter is actually used;
`minReplicas=0, maxReplicas=1` means today it is not.

Acceptance:
- the invariant is written down before the resource is created;
- real Postgres and/or vector store behind private endpoints;
- `/ready` now returns genuinely ready, verified by probe;
- **state survives the failure named in the invariant** — proven by killing
  the thing, not asserted.

### Wave 4 — Enterprise ingestion

```text
Blob → Event Grid → Service Bus → Container Apps Job
```

Highest enterprise value of any wave, because it demonstrates retry,
backpressure and dead-lettering instead of a synchronous PDF upload.

Acceptance:
- a deliberately poisoned message reaches a dead-letter queue and is visible;
- retry and backpressure behaviour is observed, not assumed;
- the job is idempotent under at-least-once delivery, demonstrated by
  replaying the same message.

### Wave 5 — Network security

```text
Front Door + WAF → APIM → private Container Apps → private data services
```

Riskiest wave: it can break the app. Do not start it before Wave 2 telemetry
exists.

Acceptance:
- the public endpoint is genuinely unreachable once private-only;
- WAF blocks a real attack payload, observed;
- API Management enforces a rate limit, observed being hit;
- **rollback is tested before you need it.** Record the exact rollback
  command in the runbook *before* making the change.

### Wave 6 — Infrastructure as Code

Everything reproducible in Bicep, from a clean subscription.

Acceptance:
- a clean deploy reproduces the environment;
- drift between Bicep and reality is detected and fails loudly;
- the app revision is still pinned **by digest** — never regress to tags.

---

## 5. TWO OUTPUT FORMATS REQUIRED PER WAVE

### 5.1 Architecture Decision Record — one short ADR per wave

```text
ADR-00X
  Problem
  Existing failure / risk
  Alternatives considered
  Decision
  Why this Azure service
  Security boundary
  Failure mode
  Cost implication
  Rollback
  Runtime evidence
  Residual limitation
```

An ADR for APIM is **not** acceptable if it says:

> "Added APIM for enterprise architecture."

It must say something like:

> Client-facing authentication, rate limiting and API policy were previously
> implemented inside the application boundary. APIM was introduced to
> centralise those controls and separate edge/API governance from application
> business logic.

That reasoning — the thing the service replaces and why it moves out of the
app — is what a technical interviewer is actually assessing.

### 5.2 Diagram status markers — mandatory on every diagram

```text
✅ DEPLOYED + VERIFIED
🟡 IMPLEMENTED / NOT DEPLOYED
⚪ TARGET / PLANNED
```

**Never** draw a chain like this as though production already contains it:

```text
Front Door → APIM → Key Vault → Service Bus → …
```

The recruiter-facing diagram promotes a component to **solid / ✅** only
after that wave has passed acceptance. A diagram that shows the target as if
it were current is the exact "diagram only what exists" violation the retired
audit prompt warned about.

---

## 6. PROOF HIERARCHY — what actually proves a revision is running

Do not use an identical response body as evidence that new code is live.
`same response body ≠ same runtime code`. The `/health` payload is a static
version string, so it stays byte-identical across rollouts and proves only
that the payload was observed.

Evidence runs in this order, strongest last:

```text
source SHA
   ↓
build provenance          (SLSA subject = the platform manifest)
   ↓
immutable image digest     (pinned, never a tag)
   ↓
Azure revision
   ↓
traffic assignment
   ↓
runtime behaviour
```

A response hash sits below this chain. It evidences observable payload, not
provenance.

### Cold start — keep the numbers, keep the semantics apart

```text
cold   21.8 s      (first request after ScaledToZero)
warm   0.19–0.30 s
```

This is an observation of scale-to-zero behaviour. It is:

- **not** p99 latency
- **not** an SLA
- **not** an outage — every request returned HTTP 200
- a **cost ↔ latency trade-off**

Do not compare latency before/after adding Front Door or APIM naively while
the cold/warm state differs. A like-for-like comparison requires the same
cold/warm state on both sides.

A good later experiment, decided by requirement rather than assumption:

```text
ACA minReplicas=0   vs   ACA minReplicas=1
measure: cold availability · warm latency · monthly baseline cost
```

---

## 7. EVIDENCE CHAIN — the recruiter-facing requirement

Every claim must be traceable end to end. Extend `CV-EVIDENCE-MAP.md`:

```text
claim
  → source code
  → infrastructure code (Bicep)
  → CI run
  → Azure resource
  → runtime probe
  → logs / metrics
  → commit SHA
  → limitation
```

A claim missing any link is not CV-ready. Record it as `PARTIAL` with the
missing link named — do not shorten the chain to make it fit.

**Status vocabulary, used exactly:** `VERIFIED` · `PARTIAL` · `NOT_RUN` ·
`NOT_DEPLOYED` · `AUTH_BLOCKED` · `NOT_CLAIMED`.

---

## 8. DEFINITION OF DONE

Phase F is complete only when all of these are true and evidenced:

```text
W0.1  /health and /ready have distinct semantics, negative-tested both ways
W0.2  Qdrant question answered A / B / C, with the evidence that decided it
W0.3  SSE wording unchanged and still honest (CI VERIFIED, Azure NOT VERIFIED)
W1    three identity classes; no long-lived cloud credential anywhere;
      negative path: wrong/no identity denied, revoked denied, RBAC restored
W2    one request traceable across all five stages; one alert observed firing
W3    business invariant written BEFORE the resource; state survives the
      failure the invariant names
W4    DLQ observed; replay proven idempotent
W5    private-only, WAF and rate limit observed; rollback pre-written
W6    Bicep reproduces from clean; drift detection fails loudly
ADR   one per wave, with the real "why this service" reasoning
diagram  every component carries ✅ / 🟡 / ⚪ honestly
digest pinning preserved end to end
CI green on the final SHA
CV-EVIDENCE-MAP complete, with limitations stated
```

MAIA is not frozen until this list is satisfied. Factory and Helpdesk stay
frozen throughout.

**Wave 0 is a hard gate.** Do not begin Wave 1 while W0.1 or W0.2 is open.
Adding Key Vault, Entra or Front Door to an app that can still emit a false
health signal builds the platform on an unreliable foundation, and every
later piece of evidence inherits that unreliability.

---

## 9. FAILURE POLICY

If a wave breaks the running app: **roll back traffic to the last known-good
ACA revision** and keep that run recorded as `FAILED`. Rollback is a
first-class outcome, not a failure of the attempt.

Never hot-fix directly on the cloud. A fix applied only in Azure and never
in the repo is invisible to CI, to the audit, and to the next person — and it
re-creates exactly the doc/code/cloud divergence that the closeout just
eliminated.

Report honestly at the end of every wave, including:

- what is now `VERIFIED`, with the evidence that proves it;
- what remains `PARTIAL`, `NOT_RUN`, `NOT_DEPLOYED` or `AUTH_BLOCKED`;
- anything you added that you now think is unjustified — **removing it is
  preferred to keeping it and hedging**.

A wave that ends with three components and an honest limitations list is
better than one that ends with fifteen components and vague ones.

