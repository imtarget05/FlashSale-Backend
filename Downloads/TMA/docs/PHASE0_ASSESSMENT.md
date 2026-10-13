# Phase 0 Assessment — TMA Portfolio (CreditFlow / SmartDoc / SupportDesk)

Date: 2026-10-08. Method: static reads only (workflows, Docker, compose,
render.yaml, infra/terraform, entrypoints, ADRs). No live probes.

## 1. Canonical Terraform decision (closes prior open question)

`CreditFlow/infra/terraform/` (main/storage/variables/outputs) is the SOLE
authoritative tree per ADR-006 (Accepted). `deploy/terraform/` is superseded
and deleted (disk 2026-10-08: path absent; `git grep deploy/terraform` = 0).
`.kilo/` exists but no `*worktree*terraform*` tree found — the "jungle-kite
dual Terraform" note is stale. `infra/bicep/main.bicep` retained as
capability evidence; `deploy/scripts/deploy-azure.sh` retained (called by
deploy-azure.yml, outside Terraform scope). Claim boundary: IaC exists !=
live infra; do not claim deployed without state/endpoint evidence.

## 2. CreditFlow (FastAPI + sklearn/XGBoost + LangGraph workflow)

- Backend: `backend/app.py` (+predict_service/security), agent
  `fetch_gateways`, `models/production` SHA256-gated. Minimal serving deps
  (`requirements.api.txt`: fastapi/uvicorn/pydantic/pandas/numpy/sklearn/
  joblib/multipart); full `requirements.txt` adds xgboost, langgraph 1.2.11,
  langchain, psycopg (LAZY/workflow-only).
- CI: py3.12 fast pytest (`not integration/slow/live/infra`), safety subset,
  artifact SHA256, prod-simulation gate (GATEWAY_UNAVAILABLE under
  CREDITFLOW_ENV=production); slow job; coverage report-only; gitleaks HARD;
  pip-audit advisory; frontend build verifies dist/index.html + _redirects.
- CD: `cd.yml` -> GHCR creditflow-api/web; `deploy-azure.yml`
  (dispatch/call): pytest + sim gate, job-level AZURE_CREDENTIALS (prior
  always-skip bug fixed), deploy-azure.sh, smoke vs
  `creditflow-api.blackisland-...azurecontainerapps.io`, fails loudly w/o
  creds. `render.yaml` intentionally empty: canonical = ACA.
- Containers: `Dockerfile.api` (py3.11-slim, uid 10002, /health/live, :8000)
  for ACA; compose api :8081->8080 + web :8080 + mlflow profile (:5050).
- TF: RG + Log Analytics + CA env + UAI + App (0-3, http-scale 30,
  CREDITFLOW_ENV=production, Blob wiring).

## 3. Smart-Document-Chatbot (Spring Boot 3.2/J17 + React + FastAPI router + LangGraph)

- Backend: backend/src/main/java (entity/repo/service/controller), Flyway
  V1-V17 (prod DDL=validate), pgvector. Agent graph workflow/state.py,
  hitl.py (approve TTL 15m), Ollama qwen3:8b fallback; supply-chain core
  kept (:8020), n8n removed per plans/plan-maia-diff-prune-2026-09-14.md.
- CI: pgvector pg16 service, mvn clean verify, JaCoCo BUNDLE 65% gate; GHCR
  build both images; Render creds REQUIRED (anti deploy illusion); poll
  /api/actuator/health 20x15s.
- CD: push main + v* tags to vetted GHCR images (frontend VITE_API_URL baked
  to onrender backend); release on tags. Deploy = Render autoDeploy + Pages.
- Compose prod: llm-router (:8001, 256M), backend (Neon JDBC + R2 + Qdrant),
  agent :9000, airflow pair + monitoring + nginx. Render: 4 free keep_alive
  services; CF llama-3.3-70b + bge-base; Langfuse on; SSO off pending Keycloak.
- TF: main + container_apps + ai_search (BM25+vector RRF) + service_bus
  (DLQ) + storage. Richest of the three. Bicep present.

## 4. SupportDesk (FastAPI py3.12 + TS gateway + React + Kafka/Temporal/Qdrant)

- Backend: app/main.py; api auth/tickets/ai/automation/dashboard/metrics/sla/
  webhooks; workflows definitions/pipeline/local_runner/temporal_worker;
  workers outbox_publisher/notification_worker; Alembic offline-sql check;
  eval harness vs baseline.json (stub offline).
- CI: backend pytest (stub) + grep secret scan + alembic history/offline +
  eval smoke + regression gate (sqlite via alembic); gateway typecheck/build/
  test; frontend build/test. Branches main + feat/**.
- CD deploy.yml (CI-success + dispatch): backend to GHCR imtarget05/
  supportdesk, Render redeploy (kh02, fail loudly), wait live 20min + poll
  /api/health status:ok; frontend to Pages (supportdesk). Azure workflow only
  warns without creds -- weakest gate.
- Containers: multistage Dockerfile (appuser 1000, /api/health, stub/BOW).
  Compose: pg16 + Kafka KRaft 3.7 + backend + 2 workers + optional
  qdrant/temporal + gateway JWT proxy + nginx frontend. JWT fail-fast.
- Render: API(docker)+PG free, ALEMBIC_MIGRATE=true, CORS pinned to
  supportdesk-cta.pages.dev. Canonical kh02; zpkv suspended, banned refs.
- TF: main + container_apps + service_bus (NO storage.tf). Local Kafka vs
  Service Bus divergence intentional (ADR-002).

## 5. Cross-cutting findings -> Phase 1 seeds

1. Uniform hybrid: Render demo + ACA canonical prod + CF Pages; .env demo vs
   KeyVault+UAI prod. Same ADR family copied x3 -- standardize to shared pack.
2. Gate gradient: CreditFlow/SmartDoc fail loudly w/o creds; SupportDesk Azure
   only warns. Phase 1: align to fail-closed everywhere.
3. Hardening gradient: CreditFlow gitleaks HARD + SHA + sim gate; SmartDoc 65%
   coverage + readiness poll; SupportDesk secret-grep + eval regression gate.
   Phase 1: port eval-regression + readiness-poll to all; shared workflows.
4. SupportDesk local-first fallbacks strongest (in-process index/runner,
   optional qdrant/temporal) -- backport pattern.
5. Drift risks: SmartDoc VITE_API_URL baked at build vs SupportDesk runtime
   var; SmartDoc SSO unfinished; CreditFlow render.yaml empty (intentional).
6. No live evidence collected -- Phase 1 needs smoke verification before any
   production claim (ADR-006 s4).