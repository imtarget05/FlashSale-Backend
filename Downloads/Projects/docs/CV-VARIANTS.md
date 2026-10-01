# CV — Three Variants

_All three derive from `docs/CV-MASTER.md`. They reweight emphasis and
ordering; none invents a claim. If a phrase is not in the master, it does not
belong here._

**Principle applied: the CV does not use repository scores.** `MAIA 86 /
Helpdesk 72` never appears and never decides which bullet is stronger. Only
claim-level evidence does that. Helpdesk's repository-level `PARTIAL` comes
from portal capability gaps; the gateway claim itself is KEEP and defensible,
so Helpdesk stays in all three variants by default.

**Constraint honoured:** no job counts, no "13/13", no "secret scanning" or
"container smoke" for MAIA, no bare test figures, no test-profile comparison.

---

## Variant 1 — AI Application Engineer

_Strong nhất cho JD AI application / LLM platform. Cân bằng cả ba._

**AI Application Engineer** — building production-oriented AI systems with
Python, RAG, agents, APIs, deterministic safety controls, cloud deployment,
evaluation and observability.

**MAIA — multi-tenant RAG, agent, SSE · Azure Container Apps**
- Implemented authenticated multi-tenant SSE streaming with grounded
  citations, disconnect-aware delivery, and approval-safe agent execution,
  backed by automated regression tests and deployed on Azure Container Apps.
- Established a traceable CI/CD release path from Git commit through automated
  quality gates and an attested immutable container image to an Azure
  Container Apps revision.

**Helpdesk — enterprise AI automation**
- Built a fail-closed AI automation gateway where model output is constrained
  to typed business actions, while authorization, risk classification, human
  approval for high-risk actions and deterministic `shell=False` execution
  remain outside the model entirely.

**Factory — data quality for AI reporting**
- Built and deployed a deterministic data-quality gate that authorizes AI
  reporting only from validated KPI evidence, with quarantine-aware blocking,
  structured provenance and repeatable quality decisions.
- Built a traceable cloud delivery and runtime-verification workflow, and
  diagnosed production-only defects including missing container data and
  incorrect run-scoped quarantine metrics.

---

## Variant 2 — Applied AI / RAG Engineer

_MAIA evaluation/RAG mạnh nhất; Factory quality-gate hỗ trợ; Helpdesk thu gọn._

**Applied AI / RAG Engineer** — building retrieval and agent systems with
measured evaluation, grounded generation, and deployment to production.

**MAIA — RAG and agent evaluation · Azure Container Apps**
- Implemented authenticated multi-tenant SSE streaming with grounded
  citations, disconnect-aware delivery, and approval-safe agent execution,
  backed by automated regression tests and deployed on Azure Container Apps.
- Built retrieval evaluation that separates retrieval quality from generation
  quality, so a weak retriever cannot hide behind fluent generation, and
  recorded that a single similarity threshold provably cannot express
  answerability rather than tuning one until the metric looked acceptable.
- Established a traceable CI/CD release path from Git commit through automated
  quality gates and an attested immutable container image to an Azure
  Container Apps revision.

**Factory — data quality for AI reporting**
- Built and deployed a deterministic data-quality gate that authorizes AI
  reporting only from validated KPI evidence, with quarantine-aware blocking,
  structured provenance and repeatable quality decisions.

**Helpdesk — enterprise AI automation**
- Built a fail-closed AI automation gateway where model output is constrained
  to typed business actions, while authorization, risk classification and
  human approval remain outside the model entirely.

---

## Variant 3 — AI-focused Backend Engineer

_MAIA delivery/runtime + Helpdesk deterministic gateway + Factory cloud/reliability._

**AI-focused Backend Engineer** — building the services, gateways and delivery
path that AI features run on.

**Helpdesk — enterprise AI automation**
- Built a fail-closed AI automation gateway where model output is constrained
  to typed business actions, while authorization, risk classification, human
  approval for high-risk actions and deterministic `shell=False` execution
  remain outside the model entirely.

**MAIA — runtime and delivery · Azure Container Apps**
- Implemented authenticated multi-tenant SSE streaming with grounded
  citations, disconnect-aware delivery, and approval-safe agent execution,
  backed by automated regression tests and deployed on Azure Container Apps.
- Established a traceable CI/CD release path from Git commit through automated
  quality gates and an attested immutable container image to an Azure
  Container Apps revision.

**Factory — data platform reliability**
- Built a traceable cloud delivery and runtime-verification workflow, and
  diagnosed production-only defects including missing container data and
  incorrect run-scoped quarantine metrics.
- Built and deployed a deterministic data-quality gate that authorizes AI
  reporting only from validated KPI evidence, with quarantine-aware blocking,
  structured provenance and repeatable quality decisions.

---

## Selection guide

```text
JD says LLM / RAG / agents / AI platform  → Variant 1 or 2
JD says retrieval / evaluation / grounding  → Variant 2
JD says backend / APIs / gateways / services → Variant 3
JD generic "AI engineer"                     → Variant 1
```

**Variant 2 adds one MAIA bullet** on evaluation. That claim is KEEP
("Separated retrieval evaluation from generation so a broken retriever
cannot hide behind a fluent answer") and is traceable to the audit — but it
is used in one variant only, so the shorter variants stay tight.