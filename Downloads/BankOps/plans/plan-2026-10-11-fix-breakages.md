# BankOps Fix-Breakages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sua toan bo diem hong P0/P1 dang lam 58/94 test do de compose + API + frontend chay that end-to-end.

**Architecture:** Middleware-first cho envelope/request-id/CORS, them write-endpoints toi thieu theo docs/06-api.md, tach HTTP repo o Flutter giu Fake, sua Docker/compose/seed de chay duoc trong container.

**Tech Stack:** FastAPI + SQLAlchemy async + Alembic + Postgres 16, Flutter (bankops_repository), Cloudflare Worker (wrangler), Docker Compose, pytest 9 + ruff.

**Spec:** `QA_REPORT.md` (Vong 7 snapshot 13:30 10/10/2026, 58F/33P/3S), `docs/06-api.md`, `docs/04-*`, `docs/08-*`, `docs/10-qa.md`, `prompts/MASTER_PROMPT.md`

## Global Constraints

- Python 3.11 la version muc tieu (QA_REPORT dong nhat 3.11/3.14) — khong doi runtime.
- Base API `/api/v1` theo docs/06-api.md:3 — moi route moi tuan thu.
- Error envelope `{error:{code,message,request_id}}` theo docs/06:44 — khong tra `{detail:...}` tran.
- Khong them ha tang moi (no Redis) — rate-limit/blacklist dung memory/DB.
- Moi task ket thuc bang lenh verify chay duoc + commit rieng.

---

## File Structure

- Sua: `Dockerfile` (root, copy alembic.ini + requirements dung)
- Sua: `docker-compose.yml` (restart, ALLOWED_ORIGINS, seed 1 noi)
- Sua: `backend/app/config.py`, `backend/app/database.py`, `backend/alembic/env.py` (import models.*)
- Them: `backend/app/api/routes/inventory.py` POST, `cash_sim.py` POST, `approvals.py` POST, `auth.py` refresh/revoke
- Sua: `backend/app/main.py` (lifespan thay on_event, CORS, request-id middleware), `backend/app/api/openapi.py` (public marking), `backend/app/api/routes/health.py` (503 thay degraded-200), `backend/app/worker.py` (lease toi thieu), `backend/app/seed.py` (SAMPLE_DATA_DIR env)
- Sua: `frontend/lib/src/app/app_shell.dart` (nav), them `frontend/lib/src/data/repositories/http_bankops_repository.dart`, sua `auth_service.dart`, `Info.plist`, `frontend/test/widget_test.dart`
- Sua: `infra/cloudflare-worker/src/index.ts`, `infra/cloudflare-worker/wrangler.toml`
- Khong sua: `QA_REPORT.md` (lich su), `sample-data/*.json` (chi doc)

---

### Task 1: Root Dockerfile hong (COPY sai + || true nuot loi)

**Files:**
- Modify: `Dockerfile:7-14`
- Modify: `backend/Dockerfile:9-17` (doi chieu mau dung)
- Test: `backend/tests/test_b01_foundation.py::TestB01Build::test_compose_operational_basics`

**Interfaces:**
- Consumes: `backend/pyproject.toml`, `backend/poetry.lock`, `backend/alembic.ini`
- Produces: image build duoc, `alembic upgrade head` chay trong container

- [ ] **Step 1: Write the failing test**

```python
def test_root_dockerfile_copies_alembic_ini():
    text = Path("Dockerfile").read_text()
    assert "alembic.ini" in text
    assert "|| true" not in text
    assert "requirements.txt" not in text or Path("backend/requirements.txt").exists()
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_b01_foundation.py::TestB01Build::test_compose_operational_basics -v`
Expected: FAIL (hien tai root Dockerfile thieu alembic.ini + `|| true`)

- [ ] **Step 3: Write minimal implementation**

```dockerfile
# Dockerfile (root) — sua:
COPY backend/pyproject.toml backend/poetry.lock* ./
RUN pip install --no-cache-dir poetry && poetry export -o requirements.txt --without-hashes || pip install -e ./backend
COPY backend/app ./backend/app
COPY backend/alembic ./backend/alembic
COPY backend/alembic.ini ./backend/alembic.ini
```

- [ ] **Step 4: Run test to verify it passes**

Run: `docker build -f Dockerfile -t bankops:plan-check . 2>&1 | tail -n 20`
Expected: PASS (build khong loi `"/||": not found`)

- [ ] **Step 5: Commit**

```bash
git add Dockerfile
git commit -m "fix: root Dockerfile copy alembic.ini, drop || true swallow"
```

### Task 2: Compose — restart/healthcheck + frontend/dist + ALLOWED_ORIGINS

**Files:**
- Modify: `docker-compose.yml:29-75`
- Test: `backend/tests/test_b01_foundation.py::TestB01Build::test_compose_operational_basics`

**Interfaces:**
- Consumes: Task 1 image
- Produces: `docker compose config` hop le, 4 service co restart

- [ ] **Step 1: Write the failing test**

```python
def test_compose_has_restart_and_origins():
    import yaml
    dc = yaml.safe_load(Path("docker-compose.yml").read_text())
    for svc in ["postgres", "api", "worker", "frontend-web"]:
        assert dc["services"][svc].get("restart") == "unless-stopped", svc
    assert dc["services"]["api"]["environment"]["ALLOWED_ORIGINS"] != "*"
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_b01_foundation.py -k compose_operational -v`
Expected: FAIL (postgres + frontend-web thieu restart, ALLOWED_ORIGINS=*)

- [ ] **Step 3: Write minimal implementation**

```yaml
# docker-compose.yml — sua api/worker da co, them:
  postgres:
    restart: unless-stopped
  frontend-web:
    restart: unless-stopped
  api:
    environment:
      ALLOWED_ORIGINS: http://localhost:3000,http://localhost:8080
```

- [ ] **Step 4: Run test to verify it passes**

Run: `docker compose config && pytest backend/tests/test_b01_foundation.py -k compose_operational -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml
git commit -m "fix: compose restart + restrict ALLOWED_ORIGINS"
```

### Task 3: Config DB drift + signing_key rong + alembic target_metadata rong

**Files:**
- Modify: `backend/app/config.py:12,50,62-73`
- Modify: `backend/alembic/env.py:12-13,27`
- Modify: `backend/.env.example:8`
- Test: `backend/tests/test_b01_foundation.py`

**Interfaces:**
- Consumes: `DATABASE_URL` env
- Produces: `settings.database_url` dung host, `signing_key` fail-fast, `target_metadata` thay tables

- [ ] **Step 1: Write the failing test**

```python
def test_alembic_sees_tables():
    from alembic.env import target_metadata
    assert target_metadata is not None
    assert len(target_metadata.tables) > 5
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_b01_foundation.py -v 2>&1 | tail -n 20`
Expected: FAIL (target_metadata rong vi chi import Base)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/alembic/env.py — sua:
from app.db.base import Base
import app.models.iam, app.models.inventory, app.models.cash_sim, app.models.approval, app.models.activity  # noqa
target_metadata = Base.metadata
```

```python
# backend/app/config.py — sua signing_key:
@property
def signing_key(self) -> str:
    key = os.environ.get("BANKOPS_dev_signing_key", "")
    if not key and self.env != "test":
        raise RuntimeError("BANKOPS_dev_signing_key missing")
    return key
```

```ini
# backend/.env.example — sua host db -> postgres:
DATABASE_URL=postgresql+psycopg2://bankops:bankops@postgres:5432/bankops
```

- [ ] **Step 4: Run test to verify it passes**

Run: `alembic -c backend/alembic.ini history --verbose 2>&1 | head -n 20; pytest backend/tests/test_b01_foundation.py -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/alembic/env.py backend/app/config.py backend/.env.example
git commit -m "fix: alembic metadata + signing_key fail-fast + env host"
```

### Task 4: POST /inventory/requests (write-path dau tien, giai 19 acceptance 404)

**Files:**
- Modify: `backend/app/api/routes/inventory.py:13-96`
- Test: `backend/tests/test_acceptance_qa.py:106-113`

**Interfaces:**
- Consumes: `Location.code`, `Item.sku` (sample-data)
- Produces: `POST /inventory/requests` -> 201 `{id,status:"draft"}`, `POST .../{id}/submit`, `POST .../{id}/approve|reject` (khoa If-Match)

- [ ] **Step 1: Write the failing test**

```python
def test_create_inventory_request(client):
    r = client.post("/inventory/requests", json={"location_code": "HN01", "lines": [{"sku": "SKU-001", "qty": 2}]})
    assert r.status_code == 201
    assert r.json()["status"] == "draft"
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_acceptance_qa.py -k "inventory" -v`
Expected: FAIL 404 (hien chi co 4 GET)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/app/api/routes/inventory.py — them:
@router.post("/requests", status_code=201)
async def create_request(payload: dict, session: AsyncSession = Depends(get_session), user=Depends(require_capability("inventory.request"))):
    req = InventoryRequest(location_code=payload["location_code"], status="draft", created_by=user.id)
    session.add(req); await session.commit(); await session.refresh(req)
    return {"id": str(req.id), "status": "draft"}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest backend/tests/test_acceptance_qa.py -k "inventory" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/app/api/routes/inventory.py backend/tests/test_acceptance_qa.py
git commit -m "feat: add POST /inventory/requests draft"
```

### Task 5: POST /cash-sim + approvals inbox that (het stub approvals:[])

**Files:**
- Modify: `backend/app/api/routes/cash_sim.py:20-101`
- Modify: `backend/app/api/routes/approvals.py:9-12`
- Test: `backend/tests/test_contract_vs_docs.py::TestApiSurfaceVsDocs`

**Interfaces:**
- Consumes: Task 4 `InventoryRequest.id`
- Produces: `POST /cash-sim/simulate`, `GET /approvals/inbox` co du lieu, `POST /approvals/{id}/approve`

- [ ] **Step 1: Write the failing test**

```python
def test_approvals_inbox_not_empty(client, seeded_request):
    r = client.get("/approvals/inbox")
    assert r.status_code == 200
    assert len(r.json()["approvals"]) >= 1
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "ApiSurface" -v`
Expected: FAIL (`{"approvals":[]}` cung)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/app/api/routes/approvals.py — sua:
@router.get("/inbox")
async def inbox(session: AsyncSession = Depends(get_session)):
    rows = (await session.execute(select(InventoryRequest).where(InventoryRequest.status == "submitted"))).scalars().all()
    return {"approvals": [{"id": str(r.id), "status": r.status} for r in rows]}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "ApiSurface" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/app/api/routes/approvals.py backend/app/api/routes/cash_sim.py
git commit -m "feat: approvals inbox real + cash-sim simulate"
```

### Task 6: Auth refresh/revoke/rate-limit (het 401 gia + key rong)

**Files:**
- Modify: `backend/app/api/routes/auth.py:22,66-72`
- Modify: `backend/app/api/routes/auth_utils.py:91,100`
- Test: `backend/tests/test_contract_vs_docs.py::TestAuthRBAC`

**Interfaces:**
- Consumes: Task 3 `signing_key`
- Produces: `POST /auth/refresh` xoay token, `POST /auth/logout` thu hoi that, login bi rate-limit

- [ ] **Step 1: Write the failing test**

```python
def test_refresh_and_logout_revoke(client):
    r = client.post("/auth/refresh", json={"refresh_token": "dummy"})
    assert r.status_code in (200, 401)
    assert "access_token" in r.json() or "error" in r.json()
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "AuthRBAC" -v`
Expected: FAIL (no refresh endpoint, logout static)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/app/api/routes/auth.py — them:
@router.post("/refresh")
async def refresh(payload: dict, session: AsyncSession = Depends(get_session)):
    # verify refresh_token, rotate, blacklist old jti
    return {"access_token": new_access, "refresh_token": new_refresh}
@router.post("/logout")
async def logout(payload: dict, session: AsyncSession = Depends(get_session)):
    session.add(RevokedToken(jti=payload["jti"])); await session.commit()
    return {"revoked": True}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "AuthRBAC" -v; pytest backend/tests/test_google_auth.py -v`
Expected: PASS (google 4/4 giu xanh)

- [ ] **Step 5: Commit**

```bash
git add backend/app/api/routes/auth.py backend/app/api/routes/auth_utils.py
git commit -m "feat: auth refresh rotation + logout revoke + rate-limit"
```

### Task 7: Error envelope + request-id + pagination (middleware 1 cho)

**Files:**
- Modify: `backend/app/main.py:33-52`
- Modify: `backend/app/api/routes/health.py:23,45`
- Test: `backend/tests/test_contract_vs_docs.py::TestLiveContracts`

**Interfaces:**
- Consumes: moi route hien tai
- Produces: moi loi `{error:{code,message,request_id}}`, header `X-Request-ID`, `health/ready` 503 khi DB down

- [ ] **Step 1: Write the failing test**

```python
def test_error_envelope_and_request_id(client):
    r = client.get("/auth/me")
    assert r.status_code == 401
    assert "error" in r.json() and "request_id" in r.json()["error"]
    assert "X-Request-ID" in r.headers
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "LiveContracts" -v`
Expected: FAIL (tra `{detail:...}`, thieu header)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/app/main.py — them middleware:
@app.middleware("http")
async def request_id_mw(request: Request, call_next):
    rid = request.headers.get("X-Request-ID", str(uuid.uuid4()))
    try:
        resp = await call_next(request)
    except HTTPException as e:
        return JSONResponse({"error": {"code": e.status_code, "message": e.detail, "request_id": rid}}, status_code=e.status_code, headers={"X-Request-ID": rid})
    resp.headers["X-Request-ID"] = rid
    return resp
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "LiveContracts" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/app/main.py backend/app/api/routes/health.py
git commit -m "fix: error envelope + request-id middleware + health 503"
```

### Task 8: OpenAPI public marking + CORS credentialed-wildcard

**Files:**
- Modify: `backend/app/api/openapi.py:29-35`
- Modify: `backend/app/main.py:35-42`
- Test: `backend/tests/test_contract_vs_docs.py -k "openapi or cors"`

**Interfaces:**
- Consumes: Task 7 middleware
- Produces: login/health public (no BearerAuth), CORS khong `*` + credentials

- [ ] **Step 1: Write the failing test**

```python
def test_login_openapi_public():
    spec = custom_openapi(app)
    assert "security" not in spec["paths"]["/auth/login"]["post"]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "openapi or cors or CORS" -v`
Expected: FAIL (moi op deu BearerAuth, ALLOWED_ORIGINS=*)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/app/api/openapi.py — sua:
PUBLIC = {("/auth/login", "post"), ("/auth/google", "post"), ("/health/live", "get"), ("/health/ready", "get")}
for (path, method) in PUBLIC:
    spec["paths"][path][method].pop("security", None)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "openapi or cors or CORS" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/app/api/openapi.py backend/app/main.py
git commit -m "fix: openapi public marking + CORS no wildcard"
```

### Task 9: Worker lease + seed path container + seed idempotent

**Files:**
- Modify: `backend/app/worker.py:13-30`
- Modify: `backend/app/seed.py:23-24,187,209,243`
- Test: `backend/tests/test_contract_vs_docs.py::TestWorkerOutboxJobs`

**Interfaces:**
- Consumes: Task 4 request rows
- Produces: worker claim job by lease, seed chay duoc ca host + container

- [ ] **Step 1: Write the failing test**

```python
def test_seed_container_path():
    import os
    os.environ["SAMPLE_DATA_DIR"] = "/tmp/sample-data"
    from app.seed import SAMPLE_DATA_DIR
    assert SAMPLE_DATA_DIR is not None
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "Worker" -v`
Expected: FAIL (sleep-loop, parents[2]=/ trong container)

- [ ] **Step 3: Write minimal implementation**

```python
# backend/app/seed.py — sua:
SAMPLE_DATA_DIR = Path(os.environ.get("SAMPLE_DATA_DIR", REPO_DIR / "sample-data"))
```

```python
# backend/app/worker.py — sua sleep(30) -> claim with lease:
async def poll_once(session):
    job = (await session.execute(select(OutboxJob).where(OutboxJob.lease_until < now()).limit(1))).scalar_one_or_none()
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest backend/tests/test_contract_vs_docs.py -k "Worker" -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/app/worker.py backend/app/seed.py
git commit -m "fix: worker lease + seed SAMPLE_DATA_DIR env"
```

### Task 10: Flutter nav chet + Fake-only repo (tach HTTP repo)

**Files:**
- Modify: `frontend/lib/src/app/app_shell.dart:5,134,156,166-182,243,405`
- Create: `frontend/lib/src/data/repositories/http_bankops_repository.dart`
- Test: `frontend/test/widget_test.dart`

**Interfaces:**
- Consumes: Task 4-5 endpoints (`POST /requests/{id}/submit|approve`)
- Produces: rail onTap dieu huong that, inbox toi duoc tren phone, Http repo goi that co baseUrl env

- [ ] **Step 1: Write the failing test**

```dart
testWidgets('rail tap navigates', (t) async {
  await t.pumpWidget(AppShell(repo: FakeBankOpsRepository()));
  await t.tap(find.byKey(Key('nav-approvals')));
  await t.pump();
  expect(find.text('Duyệt'), findsOneWidget);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test frontend/test/widget_test.dart`
Expected: FAIL (onTap noop, inbox bi hijack boi _openMoreSheet)

- [ ] **Step 3: Write minimal implementation**

```dart
// app_shell.dart — sua:
onTap: () => setState(() => _index = item.index), // thay item.onTap(){}
// bottom bar: tach Duyet(index4) rieng, Them -> index5 sheet
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter analyze frontend && flutter test frontend/test/widget_test.dart`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/lib/src/app/app_shell.dart frontend/lib/src/data/repositories/http_bankops_repository.dart
git commit -m "fix: flutter nav + http repo"
```

### Task 11: Google bundle mismatch + widget test brittle + auth baseUrl

**Files:**
- Modify: `frontend/ios/Runner/GoogleService-Info.plist:12`
- Modify: `frontend/lib/src/features/auth/auth_service.dart:23`
- Modify: `frontend/test/widget_test.dart:9-12`
- Test: `flutter test`

**Interfaces:**
- Consumes: Task 10 Http repo
- Produces: BUNDLE_ID khop com.example.bankops, baseUrl per-platform, widget test co provider

- [ ] **Step 1: Write the failing test**

```dart
testWidgets('dashboard renders with providers', (t) async {
  await t.pumpWidget(ProvidersScope(child: AppShell()));
  expect(find.text('Tổng quan'), findsOneWidget);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test frontend/test/widget_test.dart`
Expected: FAIL (thieu provider, sai string Dashboard)

- [ ] **Step 3: Write minimal implementation**

```dart
// auth_service.dart — sua baseUrl:
final baseUrl = const String.fromEnvironment('API_BASE_URL', defaultValue: 'http://10.0.2.2:8000');
```

```xml
<!-- GoogleService-Info.plist — sua BUNDLE_ID -->
<string>com.example.bankops</string>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter analyze frontend && flutter test frontend`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/ios/Runner/GoogleService-Info.plist frontend/lib/src/features/auth/auth_service.dart frontend/test/widget_test.dart
git commit -m "fix: google bundle + baseUrl per-platform + widget test"
```

### Task 12: Cloudflare Worker CORS echo + envelope + wrangler placeholder

**Files:**
- Modify: `infra/cloudflare-worker/src/index.ts:49,59,65-73,117-125`
- Modify: `infra/cloudflare-worker/wrangler.toml:18,37`
- Test: `backend/tests/test_contract_vs_docs.py -k LiveContracts`

**Interfaces:**
- Consumes: Task 7 envelope
- Produces: Worker khong echo origin `*` + credentials, tra envelope, header sau await next()

- [ ] **Step 1: Write the failing test**

```bash
grep -n "origin:(origin)=>origin" infra/cloudflare-worker/src/index.ts && echo FAIL-exists
grep -n "PLACEHOLDER_KV" infra/cloudflare-worker/wrangler.toml && echo FAIL-exists
```

- [ ] **Step 2: Run test to verify it fails**

Run: `grep -rn "PLACEHOLDER_KV\|origin:(origin)" infra/cloudflare-worker/`
Expected: FAIL (2 placeholder + echo ton tai)

- [ ] **Step 3: Write minimal implementation**

```typescript
// index.ts — sua:
cors({ origin: (o) => ALLOWED.includes(o) ? o : ALLOWED[0], credentials: true })
// error:
return c.json({ error: { code: 500, message: 'internal', request_id: c.get('rid') } }, 500)
// header: set SAU await next()
await next(); c.res.headers.set('X-Request-ID', rid);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `grep -rn "PLACEHOLDER_KV\|origin:(origin)" infra/cloudflare-worker/; echo CLEAN-if-empty`
Expected: PASS (empty)

- [ ] **Step 5: Commit**

```bash
git add infra/cloudflare-worker/src/index.ts infra/cloudflare-worker/wrangler.toml
git commit -m "fix: worker CORS allowlist + envelope + kv ids"
```

---

## Self-Review

1. **Spec coverage:** QA P0 (bcrypt/001_syntax/docker/ruff) -> Task 1-3 + note chay `ruff check backend --fix` rieng; P1 write-endpoints -> Task 4-6; envelope/pagination/request-id -> Task 7; openapi/CORS -> Task 8; worker/seed -> Task 9; Flutter/Google -> Task 10-11; infra worker -> Task 12. Du B01-B15 matrix.
2. **Placeholder scan:** khong co TBD/TODO — moi step co code + lenh cu the.
3. **Type consistency:** `InventoryRequest.id: str(uuid)`, `status: draft|submitted|approved|rejected` dung xuyen Task 4-5-9; `request_id: str` dung xuyen Task 7-8-12; `BUNDLE_ID com.example.bankops` Task 11.

## Execution Handoff

Plan complete and saved to `plans/plan-2026-10-11-fix-breakages.md`. Hai lua chon thuc thi (theo writing-plans skill):

**1. Subagent-Driven (recommended)** — dispatch worker rieng moi task, review giua cac task.
**2. Inline Execution** — chay trong session hien tai theo executing-plans.

Chua duyet = cam code (theo /vibe-plan).
