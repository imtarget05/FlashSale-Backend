# Phase 5 — AI Content Generation (§8) + Support Triage (§9) — Evidence

Date: 2026-09-23 · Build: local · Model: **qwen3:4b via Ollama** (real inference)

## Live smoke — `scripts/content-support-smoke.sh` = **24 PASS / 0 FAIL**

```
API ready
PASS  two tokens acquired (STAFF + CUSTOMER)
--- authZ guards ---
PASS  anonymous generate -> 401    PASS  CUSTOMER generate -> 403
PASS  anonymous triage -> 401
--- generate with REAL model ---
PASS  generate -> 200      PASS  draft is ReviewRequired
PASS  model is qwen3:4b    PASS  short description present
INFO  inference wall-clock: 216s (attempt 1 timed out at 120s budget → attempt 2
      succeeded — bounded retry per §12; per-call log:
      "AI content draft 10 ... latency 215914ms")
--- review queue + publish blocked ---
PASS  queue shows draft      PASS  publish unapproved -> 409  (AI never auto-publishes)
--- approve, double-approve, publish ---
PASS  approve -> 200         PASS  approved status
PASS  double approve -> 409  (approval transition is idempotent-guarded)
PASS  publish after approval -> 200   PASS  published status
PASS  product description updated with approved copy   (APPLIED to Products.Description)
--- reject flow + unknown product ---
PASS  reject with reason -> 200       PASS  generate unknown product -> 404
--- support triage (real order, real model) ---
PASS  triage carries real order facts (groundedFacts echoes the REAL order key,
      status "Confirmed" from PostgreSQL — model cannot invent order status §9)
PASS  triage has a draft reply  (category=OrderStatus grounded=True, 64,968ms)
PASS  refund triage demands human review (category=Refund requiresHumanReview=true,
      per-call log: 61,157ms; code EscalateIfObvious also forces human review
      when the model under-escalates sensitive intents)
PASS  empty triage message -> 400
--- OpenAPI ---
PASS  OpenAPI lists content generate   PASS  OpenAPI lists support triage
===================================
PASS=24 FAIL=0
```

## Development honesty trail (fixed before green)

1. **First smoke run**: 200 but empty body on content generate → root cause the
   OllamaChatClient *reported* HTTP 200 into the error path — fixed mapping +
   parser fence tolerance (`AssistantOutputParser` trims ```json fences).
2. **`HttpRequestException: Cannot write to a read-only...`**: race between two
   in-flight `PostAsync` bodies — **fixed with a dedicated `HttpClient` instance
   per typed client registration** (`AddHttpClient("ollama")` + named handler).
3. **Timeout at ~200s (503)**: the `HttpClient.Timeout` default (100s) fired
   before the use-case budget (120s) and burned the retry → set
   `client.Timeout = TimeSpan.FromSeconds(150)` so the use case owns the
   budget (evidence above: attempt 1 timeout at 120s + attempt 2 success).
4. **Seed/expired-JWT flakiness**: seeded staff login + refresh-replay
   behaviours reset by re-seeding; smoke now provisions both tokens up front.
5. **Triage grounding missing in two runs**: `Products.AvailableStock = 0`
   (earlier scenarios drained stock) → the grounding order could not be created
   (409). Smoke now tops stock up before the grounding-order step (comment in
   script), and the facts check then passed.

## Tests

- Unit **134**, Integration **40** = **174 PASS** (includes
  `ContentApprovalTests`, `SupportTriageRulesTests`, `ContentFlowTests`,
  `SupportTriageTests` — approval transitions, category/grounding rules,
  human-review escalation, publish-blocked-without-approve).
- Build: clean (`dotnet build -clp:ErrorsOnly` → `Build succeeded`).

## Spec mapping

| § | Evidence |
|---|---|
| §8 statuses DRAFT→AI_GENERATED→REVIEW_REQUIRED→APPROVED/REJECTED→PUBLISHED | approval state machine +409 on illegal transitions (smoke) |
| §8 "AI output must never auto-publish by default" | `publish unapproved -> 409` |
| §9 "AI must never invent order status" | groundedFacts from PostgreSQL; no-facts + order-status intent ⇒ human review |
| §13 human approval mandatory | publish requires `Approved` |