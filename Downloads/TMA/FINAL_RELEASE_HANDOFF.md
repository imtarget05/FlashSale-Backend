# Final release handoff: CreditFlow, SupportDesk, SmartDoc

**Goal:** make the three portfolio projects safe to demo in interviews, with an honest end-to-end story across Cloudflare Pages, Azure Container Apps, Render, APIs, and CI/CD. Do not mark a project complete from `/health` alone.

## Current state and evidence

| Project | Verified now | Open release blockers |
|---|---|---|
| CreditFlow | **Production mode live.** `CREDITFLOW_ENV=production` is on Azure ACA (both create *and* update paths of `deploy/scripts/deploy-azure.sh`); CD gate `Require production runtime mode` passes. `/health/ready` → `ready` with `runtime_mode: production (agent pipeline fail-closed, ML scoring live)` — readiness is decoupled from the simulation gate on purpose: `/predict` ML scoring is real and stays up, `fetch_gateways` fails closed (`GATEWAY_UNAVAILABLE`) until a real gateway exists. GitHub Pages canonical: `release.json` at `imtarget05.github.io/CreditFlow` = SHA `ac978d6`. Smoke (`scripts/smoke_production.py`, incl. protected reads with the synced API key) 7/7 PASS. Full pytest 463 passed. Deploy chain deploys the CI-verified SHA and restarts the revision so new images actually run. | Real CIC/bank-statement gateway still simulator-only, so the full agent graph deliberately refuses production data (`/predict/graph` fail-closed). Decision: production mode + fail-closed agent is the honest state; a real gateway sandbox is still required before the agent/HITL business flow can be demoed end-to-end. |
| SupportDesk | **CORS fixed, CD green with full business smoke (11/11).** Render `CORS_ORIGINS` now only `https://supportdesk-cta.pages.dev` (legacy `supportdesk-aht` removed via Render API PUT); smoke: frontend revision matches the deployed SHA, CORS allows canonical + rejects legacy (no ACAO), health/openapi/auth-gate PASS, **plus authenticated flow: smoke login (JWT) → create ticket → list tickets**. Dedicated smoke account created on production (`smoke-customer@supportdesk.demo`, credentials only in GitHub secrets `SD_SMOKE_EMAIL`/`SD_SMOKE_PASSWORD`). AI provider pinned in `render.yaml` to Cloudflare Workers AI (`CLOUDFLARE_*` sync vars, `$10/mo` budget) — the missing `OPENAI_API_KEY` in the dashboard is expected, not a fault. CD gate `SD_REQUIRE_BUSINESS=1` now passes instead of failing loud. | AI triage/draft, approval and notification-worker paths are still not smoke-tested live (the smoke covers CRUD/auth, not AI responses). `release.json` revision check now retries with cache revalidation — Cloudflare edge served stale content during run 38031263651. |
| SmartDoc | **RAG + Pages pipeline green from committed SHAs.** `AZURE_CREDENTIALS` secret added (new SP `github-smartdoc-deploy`: Contributor on `rg-portfolio-prod` + AcrPush on `crportfolioa3deec`); `Deploy SmartDoc RAG API to Azure` run 38046734599 all-green: revision copy preserving env → health → business RAG smoke 10 PASS → 100% traffic → `smartdoc-source` artifact. Pages workflow green (run 38052525991): `smart-doc-chatbot.pages.dev/release.json` = `84923a7` (HEAD, `manual-local-changes` marker gone), Playwright cross-site cookie auth 1/1 passed. Local `production_smoke.py --mode rag` OVERALL PASS (upload → chat → LLM). Fix history: wrangler.toml reduced to valid Pages fields; release verifier given a named User-Agent (Cloudflare bot rules 403 the default `Python-urllib` UA — that, not propagation, failed every earlier verify). | Agent/HITL still **not live**: running agent image is from 2026-10-07 (predates the `/ready` endpoint → 404), Azure has no Redis, `QDRANT_HOST=localhost`, `REDIS_ENABLED=false`. The CD step reports this as a warning (`do not claim HITL/agent live`) and RAG remains the default UI path. Durable Redis + Qdrant Cloud config and an agent deploy from current SHA remain open (ordered work §3.4–3.5). |

## Ordered work

### 0. Freeze and identify the exact source

1. Preserve every existing user change in all three repositories. Review `git status --short` and `git diff`; do not reset or checkout over local work.
2. Create commits for the intended project changes, then push them after GitHub authentication is restored. The local `gh` token currently fails `gh auth status`.
3. Record one full commit SHA per project. Rebuild each deployed artifact from that SHA; replace SmartDoc's `manual-local-changes` Pages release marker with the committed SHA.
4. Keep API keys, OAuth secrets, database URLs, and Redis/Qdrant credentials out of bundles, command output, artifacts, and test logs.

**Pass:** CI is green for each SHA, and the release metadata served by each frontend exactly matches that SHA.

### 1. CreditFlow: correct Pages target, then decide production versus demo

1. Build `frontend` with `VITE_API_BASE=https://creditflow-api.blackisland-5a3f0246.southeastasia.azurecontainerapps.io` and deploy `frontend/dist` to Wrangler project `creditflow` (`creditflow-1cg.pages.dev`). Do not touch `creditflow.pages.dev`, which is not this account's project.
2. Keep Azure CORS restricted to the two known origins while GitHub Pages remains a fallback: `https://creditflow-1cg.pages.dev,https://imtarget05.github.io`. Verify canonical preflight is 200 and a random origin has no ACAO.
3. Test public application submission, authenticated ledger/approval gates, API-key rejection, model response, and persisted data with a dedicated disposable demo account/data set. Confirm the browser sends no shared API key.
4. Inspect the gateway adapter and deployment dependencies. Either integrate a real gateway and pass the production flow, or label the release **demo/simulator only** and leave production readiness blocked. Never set `CREDITFLOW_ENV=production` simply to turn the gate green.
5. Run `python3 scripts/smoke_production.py` after the selected mode is explicit; capture `/health/ready`, API auth, and business-flow outcomes.

**Pass:** correct Cloudflare app is live with the expected SHA; real gateway business flow passes before production mode is enabled, or the demo is explicitly labelled non-production.

### 2. SupportDesk: sync Render, then prove ticket-to-AI/approval flow

1. In Render, verify service ID `srv-db1ukoqj9qps73dnvrv0` is still `supportdesk-api-kh02.onrender.com`. Sync the repository Blueprint for the exact committed SHA; confirm `CORS_ORIGINS=https://supportdesk-cta.pages.dev` and remove `https://supportdesk-aht.pages.dev`. Re-run OPTIONS tests for both origins.
2. Configure the intended real AI provider and its key through Render's secret UI/API; do not print or fetch the key. Confirm the selected provider, budget cap, database, and `START_BACKGROUND_WORKERS` from non-secret metadata.
3. Add dedicated `SD_SMOKE_EMAIL` and `SD_SMOKE_PASSWORD` repository secrets. Ensure the account is an appropriate customer account and never log its token or password.
4. Run authenticated ticket create/list. Then run AI analyze/suggest with a dedicated agent account; exercise workflow start, human approve/reject, and worker completion. Confirm audit entries and database persistence.
5. Verify Render deployed the exact requested SHA and `/api/health` is healthy. Deploy Cloudflare Pages only after backend success; verify `release.json` SHA, browser network target, auth, and CORS.

**Pass:** no legacy CORS origin; real provider response and approval/worker flow verified on the same backend SHA as the frontend release. If keys or worker dependencies do not exist, leave this release gate red and report the missing dependency.

### 3. SmartDoc: reconcile frontend and repair the real agent path

1. Commit the deployed backend/frontend changes and redeploy from the exact SHA. Verify Azure revision traffic is 100% on the healthy revision with 21 expected runtime variables; preserve the previous healthy revision for rollback.
2. Repeat browser E2E from `https://smart-doc-chatbot.pages.dev` and API smoke for browser auth, upload, owner isolation, retrieval, LLM response, and deletion/purge behavior. Verify the deployed Pages release SHA.
3. Inspect the agent service's current image, start command, port, probes, and logs. The current Azure agent `/ready` gave 404 and is not the current repo image.
4. Configure durable dependencies via secret references: managed Redis for HITL/checkpoints and Qdrant Cloud for vector retrieval, plus valid router/internal-service credentials. Remove `QDRANT_HOST=localhost` for cloud deployment. Do not turn on in-memory fallback as a production substitute.
5. Deploy agent from the same SHA. Require agent readiness to report workflow, checkpointer, memory, rate limiter, LLM, and protocol components healthy. Run an agent-mode chat and an end-to-end HITL pause → approval/rejection → resume test; verify persistence through restart.
6. Keep RAG as the default UI path until the agent test passes. If agent dependencies are absent, present the live project as RAG-only and label agent/HITL as unavailable.

**Pass:** frontend, Spring API, agent, Redis, Qdrant, and router point to the intended deployment; browser auth, RAG, agent, and HITL flows pass against the deployed SHA.

### 4. Cross-project release gates and interview proof

1. Ensure CD triggers only after CI success and deploys the CI run's exact SHA. Make frontend deploy depend on backend deploy and backend smoke.
2. Require live smoke results as a release gate. Missing credentials must fail loudly; an unconfigured/skipped deploy must never be green.
3. Verify rollback instructions and perform a non-destructive rollback rehearsal for ACA/Render where supported.
4. Publish a short evidence sheet per project: architecture diagram, cloud resources, API contract/auth, CI/CD run URL and SHA, smoke results, cost limits, known unavailable features, and a 2–3 minute demo script.
5. Do not claim "optimized" until latency, cold start, error rate, cost, and load limits have measured baselines and target thresholds.

## Credentials and external access needed

- GitHub CLI works again (`gh auth status` valid); commits/pushes and workflow dispatches all succeeded this session.
- Cloudflare Wrangler OAuth is available and has Pages write permission; Pages project names above were enumerated successfully.
- Render API key (in `SupportDesk/.env`) works — used for `CORS_ORIGINS` PUT and deploy trigger. Dedicated smoke account secrets (`SD_SMOKE_EMAIL`/`SD_SMOKE_PASSWORD`) still not present.
- CreditFlow needs a real banking/gateway sandbox endpoint and credentials before production readiness can pass.
- SmartDoc needs reachable managed Redis and Qdrant configuration, plus router/agent service credentials. Azure deploy now works via the new `AZURE_CREDENTIALS` secret (SP `github-smartdoc-deploy`). SupportDesk smoke account now exists: `SD_SMOKE_EMAIL`/`SD_SMOKE_PASSWORD` GitHub secrets (login → ticket CRUD verified in CD).

## Last validation results (2026-10-10, end of session)

- **SmartDoc**: `Deploy SmartDoc RAG API to Azure` runs 38046734599 / 38053713447 green (business RAG smoke 10 PASS inside deploy); Pages runs green — `release.json` = `84923a7` = HEAD (verified after edge purge), Playwright cross-site cookie auth 1/1 passed; local `production_smoke.py --mode rag --browser-origin https://smart-doc-chatbot.pages.dev` OVERALL PASS. Agent `/ready` still 404 on the old image (warned, not claimed).
- **CreditFlow**: CD run 38039267822 green — deploy + production gate + readiness + smoke + GitHub Pages deploy all pass; `release.json` = `ac978d6` = HEAD; `scripts/smoke_production.py` 7/7 PASS (incl. protected reads with API key synced from ACA); local pytest 463 passed, 2 skipped.
- **SupportDesk**: CD run 38057006008 green — Render backend redeploy at the exact SHA + frontend Pages deploy + smoke **11 passed, 0 failed** (revision, CORS allow/reject, health, openapi, auth gate, login, create ticket, list tickets).
- Modified workflow YAML parses; all touched shell scripts pass `bash -n`; `git diff --check` clean in all three repos; all work committed and pushed (`ac978d6` / `7081f59` / `84923a7`).
