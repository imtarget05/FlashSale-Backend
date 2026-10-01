# CV — Master Source

_Single source of truth for all three CV variants. Variants reweight emphasis;
they never invent claims. Every bullet here traces to
`docs/evidence/final/portfolio-completion-audit.json` and
`docs/CV-EVIDENCE-MAP.md`._

**Positioning (not a title to compete on, a description of the work):**
AI Application Engineer — building production-oriented AI systems with Python,
RAG, agents, APIs, deterministic safety controls, cloud deployment, evaluation
and observability. Cloud/DevOps is evidence of shipping AI systems, not a
separate identity.

**Claims discipline:** the 11 KEEP claims are a claim bank, not 11 bullets.
Streaming is claimed as an implemented, tested, deployed capability — **not**
as verified end-to-end on Azure, because no artifact for that exists (see
`azure.sse_reconciliation_2026_10_01`). Test counts live in project metadata
and evidence, not in every bullet.

---

## Recruiter narrative — three questions, three projects

```text
MAIA      How do I build and operate an AI application?
Helpdesk  How do I stop AI from gaining unsafe execution authority?
Factory   How do I stop AI from reasoning over untrustworthy business data?
```

---

## The five bullets

### MAIA — AI/RAG/Agent/SSE · Azure Container Apps

> Built and deployed a multi-tenant RAG and agent application with
> authenticated SSE streaming, grounded citations and approval-safe tool
> execution, backed by automated regression tests and running on Azure
> Container Apps.

> Established a traceable CI/CD release path from Git commit through
> automated quality gates and an attested immutable container image to an
> Azure Container Apps revision.

_CI wording is deliberately qualitative and names no job count._ `13/13` would
go stale the moment a job is added, and the figure `998` belongs to a CI skip
profile that is not comparable with the `1008` eval milestone. This bullet is
defensible end to end instead: source SHA → CI run → digest → attestation →
revision. Gate types named in the evidence: unit tests, lint/type checks,
prompt and evaluation gates, threshold regression, container delivery.

_"Secret scanning" and "container smoke" are NOT claimed for MAIA._ The audit
does not carry evidence for those as named MAIA gates. Factory has the stronger
evidence for that phrasing.

### Helpdesk — enterprise AI automation

> Built a fail-closed AI automation gateway where model output is constrained
> to typed business actions, while authorization, risk classification, human
> approval for high-risk actions and deterministic `shell=False` execution
> remain outside the model entirely.

### Factory — data quality and AI reporting

> Built and deployed a deterministic data-quality gate that authorizes AI
> reporting only from validated KPI evidence, with quarantine-aware blocking,
> structured provenance and repeatable quality decisions.

> Built a traceable cloud delivery and runtime-verification workflow, and
> diagnosed production-only defects including missing container data and
> incorrect run-scoped quarantine metrics.

---

## Anti-claims — never write these

```text
✗ "Verified authenticated SSE end-to-end on Azure"   no artifact exists
✗ "Improved test coverage from 1008 to 998"          different env profiles
✗ any bare test count without SHA + environment
✗ "Kubernetes", "service mesh", "multi-cloud"        not deployed
✗ "Cloud Architect" / "DevOps Engineer"               competing identity
✗ bare "LLM + RAG + FastAPI + Docker"                says nothing
```