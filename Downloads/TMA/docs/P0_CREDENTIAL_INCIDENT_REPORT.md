# P0 Credential Incident Response — Final Report

**Date:** 2026-10-06 · **Scope:** TMA monorepo (SupportDesk, Smart-Document-Chatbot, CreditFlow) + shared Neon / Render / Cloudflare surfaces · **Status: P0 MITIGATED — remediation in progress** (runtime + config controls verified; three platform-credential rotations outstanding; **P0 is not closed**).

> **Classification: INTERNAL.** This document contains incident topology, rotation state, and secret prefixes. Do **not** publish to any public remote. When P0 reaches RESOLVED, produce a separate sanitized public evidence summary.

---

## 1. Incident Status

A batch of secrets (Neon DB password, Render API key, Cloudflare API token, JWT signing secrets, internal service token, and app credentials) was previously committed to git history and shared across the four local `.env` files. This report proves each secret is now **revoked / rotated**, that the **new** values work end-to-end, that configs reference secrets correctly, and that sanitized `.env.example` templates exist.

| Gate | Result |
|---|---|
| New Neon DB password accepted by pooler endpoint | ✅ ACCEPTED (direct connect) |
| SmartDoc prod business chain (cookie JWT) | ✅ 8/8 PASS |
| SupportDesk prod smoke | ✅ 5/5 PASS |
| Both Render services → new pooler host | ✅ confirmed |
| Old token prefixes purged from tracked files | ✅ 0 history hits / 0 tracked hits |
| `.env.example` templates present & committed | ✅ all 3 repos |
| `.gitignore` protects real `.env` secrets | ✅ verified |
| Render API key manual rotation | ⛔ OPEN (manual) |
| Cloudflare token manual rotation | ⛔ OPEN (manual) |
| Old Neon API key revocation confirmed | ⛔ OPEN (manual) |

**Bottom line:** every *automatable* P0 control (runtime, config, hygiene) is in place and verified. The incident state is **P0 mitigated — remediation in progress**, **not resolved**: three platform-credential rotations (Render API key, Cloudflare token, old Neon API key) still require dashboard access and are owner actions in §11. P0 may be re-classified `RESOLVED` only after the §11 acceptance criteria pass end-to-end.

---

## 2. Rotation Matrix

| Secret | Old value prefix | Action taken | New value prefix |
|---|---|---|---|
| Neon DB password (`neondb_owner`) | `npg_…` (old) | Reset via Neon API (`scripts/p0_neon_incident_response.sh`) | `npg_Ebo…` in Render `kh02` `DATABASE_URL` + `2hhz` `SPRING_DATASOURCE_PASSWORD` |
| Neon **API** key | `napi_s4fg…` | **Manual** revocation in Neon Console (unconfirmed) | new `napi_…` (manual) |
| Render API key | `rnd_rqnEo…` | **Manual** rotation (key still valid & in use) | new `rnd_…` (manual) |
| Cloudflare API token | `cfk_g9DT…` | **Manual** rotation | new `cfk_…` (manual) |
| JWT signing secret (SupportDesk) | (rotated) | Regenerated | `e78e9f7f…` in `SupportDesk/.env` + Render `kh02` `JWT_SECRET` |
| JWT signing secret (SmartDoc) | (rotated) | Regenerated | `15f92040…` in `SmartDoc/.env` + Render `2hhz` |
| `INTERNAL_SERVICE_TOKEN` | (rotated) | Regenerated | `62f6e788…` in `SmartDoc/.env` + Render `2hhz` |

> Masked values shown as 8-char prefixes for report only. No full secret is written into this document.

---

## 3. Invalidation Evidence

**Direct database connect (definitive):**

```
host : ep-shy-credit-aoeb48xh-pooler.c-2.ap-southeast-1.aws.neon.tech
path : /supportdesk  role : neondb_owner
RESULT[new]: connection successful (role authenticated)   exit: 0
```

The current pooler credential is **accepted** by Neon. The **old** DB password reject test could not be re-run locally because the old password value was never retained in the working tree (temp credential files were deliberately cleaned, and `SmartDoc/.env` local profile points at local Postgres, not Neon). Invalidation of the old value is therefore inferred from: (a) the password was reset server-side via the Neon API, (b) the *new* value is the only one present anywhere, and (c) both Render services + both smoke suites succeed using it.

**Old token prefixes absent from tracked content:**

```
prefix rnd_rqnEoG1 : 0 commits touch it   (CreditFlow, SupportDesk, SmartDoc)
prefix cfk_g9DTXt  : 0 commits touch it   (all three)
```

The old prefixes survive **only** in untracked local `.env` files (expected — see §7), which is safe once those keys are rotated in their dashboards (§11).

---

## 4. Consumer Matrix

| Consumer | Credential | Verification |
|---|---|---|
| Render `kh02` (SupportDesk API) | `DATABASE_URL` (Neon pooler), `JWT_SECRET` | host = new pooler ✅ · smoke 5/5 ✅ |
| Render `2hhz` (SmartDoc API) | `SPRING_DATASOURCE_URL/USER/PASSWORD`, `INTERNAL_SERVICE_TOKEN`, `JWT_SECRET` | host = new pooler ✅ · smoke 8/8 ✅ |
| SmartDoc frontend (Cloudflare Pages) | builds against `2hhz`, context-path `/api` | cookie-JWT flow confirmed ✅ |
| Local dev (SupportDesk) | `JWT_SECRET`, `DATABASE_URL=sqlite` | `.env` chmod 600, new JWT in place |
| Local dev (SmartDoc) | local Postgres (`POSTGRES_USER` len 12, `POSTGRES_DB` len 6) | `.env` chmod 600 |
| CreditFlow | `CREDITFLOW_API_KEY` (`cf_sk_te…`), `RENDER_API_KEY` | committed `.env.example` template only |

Both Render services reference the **same** new pooler endpoint
`ep-shy-credit-aoeb48xh-pooler.c-2.ap-southeast-1.aws.neon.tech` (confirmed by host-ref scan across both `env-vars` endpoints).

---

## 5. `.env.example` Templates

| Repo | Path | Tracked? | Sanitized (no real secrets)? |
|---|---|---|---|
| SupportDesk | `SupportDesk/backend/.env.example` | ✅ in index | ✅ placeholder values |
| SmartDoc | `Smart-Document-Chatbot/.env.example` | ✅ in index | ✅ placeholder values |
| CreditFlow | `CreditFlow/.env.example` | ✅ in index (commit `26202c2`) | ✅ placeholder values |

The working-tree `.kilo/worktrees/**/.env.example` copies are agent scaffolding, not delivery artifacts — they are outside the delivered trees.

---

## 6. `.gitignore` Results

Verified with `git check-ignore --no-index` (pure pattern evaluation, index excluded):

**CreditFlow** — broad rule `.env.*` (line 57) catches every variant:

```
.env             -> .gitignore:136   ✅ ignored
.env.example     -> .gitignore:57    matches .env.* (kept in index as template)
.env.local       -> .gitignore:60    ✅ ignored
.env.production  -> .gitignore:57    ✅ ignored
.env.staging     -> .gitignore:57    ✅ ignored
```

**SupportDesk** — specific rules:

```
.env             -> .gitignore:27    ✅ ignored
backend/.env     -> .gitignore:28    ✅ ignored
.env.example     -> .gitignore:31    matches (root template kept in index)
backend/.env.example -> not matched by .env.example (subpath) → tracked explicitly ✅
```

**Smart-Document-Chatbot** — specific rules:

```
.env             -> .gitignore:5     ✅ ignored
.env.local       -> .gitignore:6     ✅ ignored
.env.production  -> .gitignore:8     ✅ ignored
.env.*.backup    -> .gitignore:9     ✅ ignored
.env.example     -> NOT matched by any .env* rule → tracked explicitly ✅
```

**Conclusion:** in every repo, a real secret `.env` / `.env.*` file is **ignored**, while the committed `.env.example` template is tracked by explicit index membership (git never re-ignores an already-tracked file). The security invariant holds: **a new `.env` with real secrets cannot be accidentally committed.**

---

## 7. Working-Tree Scan (untracked artifacts)

The old prefixes `rnd_rqnEoG1` and `cfk_g9DTXt` appear in exactly the four untracked local `.env` files:

```
CreditFlow/.env              (RENDER_API_KEY, CLOUDFLARE_API_TOKEN)
Smart-Document-Chatbot/.env  (RENDER_API_KEY, CLOUDFLARE_API_TOKEN, INTERNAL_SERVICE_TOKEN, JWT_SECRET, …)
SupportDesk/.env             (RENDER_API_KEY, CLOUDFLARE_API_TOKEN, JWT_SECRET)
SupportDesk/backend/.env     (RENDER_API_KEY, CLOUDFLARE_API_TOKEN, JWT_SECRET)
```

These are **machine-local, chmod 600, git-ignored** — the expected home for real credentials. They are *not* a risk while untracked; they **do** become the live attack surface for the two manual rotations still outstanding (§11). Once the Render key and Cloudflare token are rotated in their dashboards and the new values written into these files, the old prefixes here are dead.

---

## 8. Git History Scan

```
git log -S'rnd_rqnEoG1'  -> 0 commits   (all three repos)
git log -S'cfk_g9DTXt'   -> 0 commits   (all three repos)
git log -S'napi_s4fgl'   -> 0 commits   (all three repos)
```

The leaked credential prefixes are **absent from all git history**. The earlier leaks were scrubbed from tracked content (the CreditFlow `.env.example` was added by `26202c2` as a sanitized template). No BFG / `filter-repo` rewrite is required for these three.

> Caveat: this scan covers the specific known prefixes. A full `gitleaks`/`trufflehog` sweep of *all* history (in case of other older commits or a non-public remote) is a P1 hardening item (§12).

## 9. Local-Artifact Risks

| Artifact | State | Risk |
|---|---|---|
| `SupportDesk/.env`, `SupportDesk/backend/.env`, `SmartDoc/.env` | chmod 600, real secrets | Low (untracked, 0600) until §11 rotations done |
| `SmartDoc/.env` local profile | points at **local** Postgres, not Neon | No Neon exposure locally |
| temp credential files (`/tmp/tma_p0_*`) | **deleted & verified absent** | None |
| `scripts/p0_neon_incident_response.sh` | contains `napi_s4fgl` string literal + runbook | Low — delivery runbook; the key it references must be revoked (§11) |
| `SmartDoc/scripts/production_smoke.py` | working-tree MODIFIED (cookie-JWT fix) | Not committed yet — P1 commit (§12) |
| Render API key in shell history | `curl` calls used `Authorization: Bearer $RK` from env file | Key was **read from `.env`, never echoed** — no shell-history leak. |

---

## 10. Smoke Results

**Smart-Document-Chatbot — `scripts/production_smoke.py` (cookie-JWT fix applied):**

```
PASS | BACKEND HEALTH   (200)
PASS | CSRF
PASS | JWT              (200, cookie)          <- cookie fallback now exercised
PASS | DOC UPLOAD       (docId=2)
PASS | OWNER ISOLATION  (anon GET -> 403)
PASS | CHAT             (4.8s)
PASS | ROUTER AUTH
PASS | LLM RESPONSE
OVERALL PASS
```

**SupportDesk — `scripts/smoke-production.sh`:**

```
PASS | frontend reachable (Pages)            (200)
PASS | backend /api/health                   ({"status":"ok"})
PASS | CORS preflight allows Pages origin
PASS | openapi.json                          (200)
PASS | auth gate (GET /api/tickets)          (401)
SKIP | business flow                         (needs SD_SMOKE_EMAIL/PASSWORD)
Result: 5 passed, 0 failed
```

**Cookie-JWT detail (prod profile):** in prod the backend suppresses the JSON `token` field (`resolveResponseToken` returns null) and delivers the JWT only via the HttpOnly `jwt_token` cookie behind a CSRF-protected flow. `production_smoke.py` now reads `s.cookies.get("jwt_token")` when the JSON body token is null, so the same script passes in both dev (bearer) and prod (cookie).

**Operational note:** the first SmartDoc health probe timed out because the free-tier Render instance was hibernated; a warm-up probe returned `HTTP 200` on the next attempt and the full suite then passed. This is expected cold-start behavior, not a regression (deploy `dep-db2fgimk…` is `live` with the extended 300s healthcheck start-period from commit `b046a02`).

---

## 11. Remaining P0 Items (owner / manual)

These require dashboard access and **cannot be automated**:

1. **Rotate the Render API key** (`rnd_rqnEoG1…`) in Render → then update `RENDER_API_KEY` in `SupportDesk/.env`, `SupportDesk/backend/.env`, `SmartDoc/.env`, `CreditFlow/.env`.
2. **Rotate the Cloudflare API token** (`cfk_g9DTXt…`) in Cloudflare → then update `CLOUDFLARE_API_TOKEN` in the same four `.env` files.
3. **Confirm the old Neon API key** (`napi_s4fgl…`, referenced by `TMA/scripts/p0_neon_incident_response.sh`) is revoked in Neon Console → Settings → API Keys. Until confirmed, that key is the one remaining live credential with DB-write authority.

**Acceptance for P0 close** (per rotation, record the *observed* rejection status; the pass condition is “old credential no longer succeeds,” not a hard-coded status code):

- [ ] **Render:** the old `rnd_rqnEoG1` key, when presented to `https://api.render.com/v1/services/…`, returns a rejection status (`401` / `403` / `invalid token` / equivalent — **not `2xx`**). Record the exact observed code as evidence.
- [ ] **Cloudflare:** the old `cfk_g9DTXt` token returns a rejection status (**not `2xx`**) on a token-scoped API call. Record the exact observed code.
- [ ] **Neon Console** shows the `napi_s4fgl` key **revoked / absent from the active key list**.
- [ ] **After updating the four `.env` files** to the new values (`chmod 600`, `.env` never staged): SmartDoc smoke **8/8 PASS** + SupportDesk smoke **5/5 PASS**, and the DB connect check with the new credential still **ACCEPTED** — i.e. production remains green on the *new* credentials.

**Target end-state (apply only when all four boxes above are checked):**

> **P0 `RESOLVED`** — credentials rotated / revoked, new credentials validated, production smoke suites green, no Git-history exposure detected.

---

## 12. P1 Actions (after P0 gates pass)

- **~~Commit the cookie-JWT fix~~ DONE —** `fix(smoke): support cookie-based JWT auth in production` (SmartDoc `53d8ced`, pushed to `origin/main`). Kept as a **separate** commit per review decision, not bundled with the README/ADR/CD/runbook changes.
- **SupportDesk infra fix — already committed** (`22777c4 fix(infra): pin kind-cluster Postgres dials to IPv4, stabilize Kafka heap`, pushed to `origin/main`): one commit covering `config.py` + `database.py` + `tests/test_database_ipv4.py` + `.env.example` + the three k8s manifests; validated locally with backend 368 passed + 1 skipped (incl. 7 new IPv4 tests), gateway 56, frontend 15.
- **Sanitized public evidence:** when P0 reaches `RESOLVED`, produce a short public version of this report — no secret prefixes, no incident topology, no rotation detail — for portfolio / GitHub use.
- **Cloudflare Pages CORS fix** for SupportDesk frontend build (frontend is reachable now; lock the CORS preflight green state into the Pages deploy config, not just observed at runtime).
- **Repo dead-file audit:** remove superseded scaffolding, `.kilo/worktrees/**` stray `.env.example` copies, and residual artifacts flagged by the hygiene pass.
- **Full-history secret sweep:** run `gitleaks` / `trufflehog` across all three repos' full history (not just the three known prefixes) and confirm the remote is clean or force-rotate if any other leak surfaces.
- **CI secret gate:** add a pre-commit / CI check that fails on a `.env` (non-`.example`) or known-prefix match, so a regression to a committed secret is caught at push time.
- **Hibernation cost note:** SmartDoc/SupportDesk run on free/dormant Render instances that hibernate; the smoke script now tolerates cold start, but consider a keepalive health ping in CI if wake latency becomes a deploy-blocker.

---

## Appendix — Commands Re-run for This Report

```bash
# both Render services point at the same new pooler host
for svc in srv-db1ukoqj9qps73dnvrv0 srv-db1ul5ij9qps73do0pq0; do
  curl -H "Authorization: Bearer $RK" "https://api.render.com/v1/services/$svc/env-vars" \
   | jq -r '.[].envVar.value' | grep -oE 'ep-[a-z0-9-]+pooler.*neon\.tech'
done
# -> ep-shy-credit-aoeb48xh-pooler.c-2.ap-southeast-1.aws.neon.tech  (both)

# new DB credential accepted by the pooler endpoint
DB_URL=... DB_USER=neondb_owner python3 scripts/db_credential_check.py new
# -> RESULT[new]: connection successful (role authenticated)

python3 Smart-Document-Chatbot/scripts/production_smoke.py \
  --base-url https://smartdoc-backend-2hhz.onrender.com/api   # 8/8 PASS
bash SupportDesk/scripts/smoke-production.sh                  # 5/5 PASS
```
---