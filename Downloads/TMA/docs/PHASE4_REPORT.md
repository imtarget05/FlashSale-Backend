# Phase 4 Report — Phase 2 Hardening Slices (2026-10-09)

Session tiếp nối từ PHASE0_ASSESSMENT / PHASE1 / PHASE2 / PHASE3.
Phạm vi lượt này: các slice Phase 1–3 có thể đóng trong một buổi
(hardening code + honesty docs + gate blocking). KHÔNG có luôn sản
nào được chứng minh production-ready; xem bảng cuối.

Nguyên tắc chấp hành: giữ nguyên working tree có sẵn; mỗi slice do
subagent riêng thực thi, test thật, báo cáo file:line + output.
Một số thay đổi đã bị commit bởi tiến trình song song giữa chừng —
nội dung kiểm chứng nằm trong file hiện tại (xem Verify).

---

## 4A. CreditFlow

### 4A.1 — Decision-support honesty (Phase 1 stub)
- `pipeline/agent/nodes.py:558` — audit entry của node `execute()`:
  `"approved application {app_id} — disbursement pending — no real transfer
  executed (stub)"`. Logic (VietQR + loan agreement) không đổi.
- `README.md:83-90, 109` — note block ngay dưới architecture diagram:
  `execute()` chỉ generate VietQR/loan-agreement + ghi audit, KHÔNG chuyển
  tiền thật → sản phẩm hiện là decision support.
- Verify: `pytest tests -k "execute or audit or disbursement" -m "not integration" -q`
  → **14 passed** (gồm e2e disbursement + LangGraph slow tests).

### 4A.2 — Shared-key limitation documented (Phase 2 identity gap)
- `README.md:39, 218` — mọi route nhạy cảm dùng MỘT shared API key
  (`CREDITFLOW_API_KEY`); không có user table / OIDC / RBAC;
  `approver_id` là giá trị cấu hình toàn deployment, không chứng minh
  danh tính người bấm duyệt. Kèm 4 yêu cầu trước production:
  per-user identity, RBAC mapping roles, audit theo user thật,
  revoke/rotate per-user.
- `backend/security.py:8` — docstring khớp README (chỉ docstring, không logic).
- Verify: `pytest tests/test_api_auth.py -m "not integration" -q` → **32 passed**.

### 4A.3 — Shared checkpoint storage (Phase 2 durability)
- `pipeline/agent/checkpointer.py:49,75-207` — `create_checkpointer()`:
  chọn backend theo `DATABASE_URL` (postgres / sqlite), lazy-import driver,
  giữ `FileCheckpointSaver` làm fallback; `CREDITFLOW_CHECKPOINT_SHARED_REQUIRED=1`
  để fail-closed thay vì im lặng hạ cấp single-process.
- `backend/app.py:266,466-498`, `pipeline/agent/graph.py:91,147` — wiring +
  `/health/ready` expose `checkpoint_backend: postgres|sqlite|file|unset`.
- `tests/test_checkpoint_shared_storage.py` mới (20 fast + 1 live/infra).
- **Chưa verify live Postgres**: driver `langgraph-checkpoint-postgres`
  KHÔNG được cài (đúng chỉ dẫn) → đường thật chỉ chứng minh qua khi
  `LIVE_TESTS=1`.
- Verify: `pytest tests -k "checkpoint" -m "not integration" -q` →
  **28 passed, 1 skipped**; regression `pytest tests -m "not integration" -q`
  → **450 passed, 3 skipped** (~11 phút).

### 4A.4 — Fix: FileCheckpointSaver hang khi snapshot state tích lũy
- **Triệu chứng**: từ sau phiên 4A.3, `test_e2e_disbursement.py` HANG
  vô hạn (~307/476, deterministic) khi `data/creditflow_checkpoints.pkl`
  chứa state tích lũy (9 threads / 97 writes / 510 blobs). Lỗi TÁI HIỆN
  chỉ với file cũ: xóa file → cùng test **pass 11s**; restore file → hang lại.
- **Root cause** (faulthandler dump `/tmp/cf_tb.txt`): thread
  `ThreadPoolExecutor-1_9` kẹt CPU-bound trong
  `put_writes → _snapshot → _encode_obj` (checkpointer.py:402/404/322/260)
  — `_encode_obj` không có cycle/memo guard → re-walk chia sẻ/subtree
  theo cấp số nhân; `_snapshot_lock` (RLock) giữ suốt lúc encode → mọi
  `put`/`put_writes` khác (11 thread LangGraph executor) chờ
  `_checkpointer_put_after_previous` → convoy. Không phải vấn đề
  Postgres/DATABASE_URL (vẫn hang khi `unset DATABASE_URL`).
- **Fix**: `pipeline/agent/checkpointer.py:250-309` — `_encode_obj` giờ
  memo hóa container/bytes theo `id()` (mỗi node chia sẻ encode 1 lần)
  và giữ `_active` = đường đệ quy hiện tại; cycle thật → fail-fast
  `ValueError` thay vì `RecursionError`/treo. Format giữ nguyên
  (`__bytes__`/`__tuple__`/`__tuple_key__`) → round-trip với `_decode_obj`
  không đổi; `_snapshot`/`_decode_obj` không động đến.
- `tests/test_checkpointer_encode_cycles.py` mới (6 tests): RED trước fix
  (3 cycle tests `RecursionError`), GREEN sau fix.
- Verify: `pytest tests/test_e2e_disbursement.py -v` (file 1.2MB hiện hữu)
  → **5 passed in 23.30s**; `tests/test_checkpointer_encode_cycles.py`
  → **6 passed**; `test_checkpoint_shared_storage.py` → **22 passed, 1 skipped**;
  round-trip `_load`/`_snapshot` trên file cũ 923KB → reload 922942 B
  == backup gốc (không mất dữ liệu).
- **Full fast suite lần cuối**: `pytest tests -m "not integration" -q -p no:warnings`
  → **480 passed, 3 skipped, 3 deselected in 42.57s** (không còn hang).
- Residual: `_snapshot` vẫn ghi lại TOÀN store mỗi lần put (memo làm
  encode O(unique), không exponential) — ổn cho demo single-process;
  multi-replica nên dùng Postgres/sqlite backend (đã có qua `DATABASE_URL`).

## 4B. Smart-Document-Chatbot

### 4B.1 — generate_report signature (Phase 1 bug)
- `agent/routers/actions.py:39` — gọi đúng chữ ký
  `generate_pdf_report(title=req.title, content=req.content, user_id=req.user_id)`
  (trước đó lệch 8 tham số so với `report_agent.py:85` → TypeError khi gọi).
- `agent/tests/test_report_endpoint.py` mới: assert kwargs đúng.
- Verify: `pytest agent/tests/test_report_endpoint.py -x -q` → **1 passed**.

### 4B.2 — Readiness phản ánh degraded (Phase 1 startup masking)
- `agent/routers/health.py:24-118` — `GET /ready` mới: check workflow
  (`state._workflow is None`), long-term/graph memory fallback RAM,
  rate_limiter, a2a hub, MCP server, LLM factory. Lỗi → HTTP 503
  `{"status":"degraded","components":{...},"errors":[...]}`. `/health`
  (liveness) giữ nguyên.
- `agent/tests/test_health_readiness.py` mới (5 tests).
- Verify: 5 passed; chạy chung `test_route_security.py` → **14 passed**.
- **Follow-up còn lại**: probe manifest (`k8s/`, `render.yaml`) vẫn trỏ
  `/health` — cần đổi sang `/ready` để tận dụng gate này.

### 4B.3 — Durable checkpointer (Phase 2 durability)
- `agent/settings.py` — `langgraph_checkpoint_db` (`LANGGRAPH_CHECKPOINT_DB`).
- `agent/graph/workflow.py:36-291, 462` — `_build_checkpointer()`:
  `APP_ENV=test` → MemorySaver; production → **AsyncSqliteSaver** bền vững
  (lazy import, graceful MemorySaver nếu ImportError/IOError);
  `close_checkpointers()` + atexit hook. Đổi sáng suốt sync→async saver vì
  mọi caller dùng `ainvoke` (sync `SqliteSaver.aget_tuple` raise
  NotImplementedError).
- `agent/tests/test_workflow_checkpointer.py` — +5 tests gồm **restart
  durability** (ghi bằng saver này, đọc lại bằng saver mới).
- Verify: `pytest agent/tests -k "workflow" -q` → **12 passed**;
  full `agent/tests` → **266 passed**; repo-wide → **304 passed**.
- Lưu ý: dependency `langgraph-checkpoint-sqlite` hiện đã pin
  ==2.0.11 trong `agent/requirements.txt:16` (thêm bởi tiến trình song song,
  có comment rõ vì sao pin 2.x).

### 4B.4 — Delete lifecycle (Phase 2): ĐÃ ĐỦ, chỉ chứng minh
- Kiểm tra toàn luồng: `DocumentController.java:282-296` (single),
  `:261-280` (batch) → `DocumentService.deleteDocument` (blob + chunks
  Postgres + versions + row) → `DocumentPurgeClient` POST
  `/v1/agent/documents/{id}/purge` → `agent/document_purge.py:207-260`
  với `COMPONENT_ORDER = (vector_store, cache, memory, graph_memory)`
  → Qdrant drop collection/tombstone (`qdrant_tool.py:368-458`), cache
  invalidation (`:139-165`, long_term.py:413-461, graph_memory.py:833-890).
  → **không cần sửa code**.
- `agent/tests/test_document_purge.py:562+` — 2 tests mới với FakeQdrant
  có state: (1) sau delete, hybrid search trả `[]`; (2) end-to-end purge
  qua wiring component thật — chunk/cache key/turn/fact/mention của doc
  mất, entry không liên quan còn nguyên. Có mutation-check bằng tay.
- Verify: `pytest agent/tests/test_document_purge.py -q` → **26 passed**;
  full repo → **304 passed**.

## 4C. SupportDesk

### 4C.1 — Redis-backed shared rate limit (Phase 2 durability)
- `backend/app/rate_limit.py:76-239` — `check()` giữ nguyên chữ ký,
  dispatch Redis vs in-process; Redis dùng sorted-set sliding window,
  một pipeline round trip, "undo charge" khi deny, try/except → fallback
  per-call. `reset()` clear cache backend.
- `backend/app/config.py:99-114` — `REDIS_URL` (+ alias `UPSTASH_REDIS_URL`).
- `backend/requirements.txt` — `redis>=5.0.1` (lazy import), `fakeredis>=2.0` (test).
- `backend/tests/test_rate_limit_backends.py` — 13 tests (cả 2 nhánh, gồm
  một allowance chia sẻ cho 2 client + e2e 429 qua POST /api/tickets).
- Verify: `pytest backend/tests -k "rate" -q` → **24 passed**;
  full backend suite → **388 passed in 2:28**.
- **Chưa verify live redis-server** (không có binary/container) — chỉ qua
  fakeredis + unreachable URL. Lua script không dùng vì fakeredis không
  chạy được script; residual race ghi trong docstring (burst lệch tối đa
  1 request).
- Docs đã đồng bộ: `docs/JD-MAP.md:95,158-163`, `docs/ARCHITECTURE.md:202`,
  `docs/recruiter-snapshot.md:53-54`, `docs/interview-qa.md:933-937,1175-1177`.

### 4C.2 — README honesty (Phase 1 contradictions)
- `README.md:20-24, 71-83, 135-140, 146, 206, 229` — bỏ quảng cáo
  "durable workflows survive restart"; Temporal worker ghi rõ
  "present but not verified against a live cluster
  (`TEMPORAL_ENABLED=False`)"; SLA engine: code + unit test có, nhưng
  dùng in-memory Kafka fallback, chưa verify production; loại
  "no SLA engine" khỏi Not-implemented (đổi thành PII/RBAC/multi-tenancy
  out-of-scope).

## 4D. CI gate (Phase 3 parity)

- SmartDoc `.github/workflows/ci.yml:330-385` — Trivy giờ **chặn merge**:
  `exit-code: '1'`, `ignore-unfixed: true`, severity CRITICAL,HIGH,
  bỏ `continue-on-error`. Thêm pass SARIF + upload lên Security tab.
  Semgrep/CodeQL giữ `continue-on-error` có comment lý do.
- Verify: `python3 -c "yaml.safe_load(...)"` → YAML OK; chưa chạy
  workflow thật trên GitHub Actions (cần push) — **gate chưa được chứng
  minh chặn bằng một PR cố ý vi phạm**.
- CreditFlow + SupportDesk: **vẫn chưa có** Trivy image scan + Sonar
  quality gate (bằng chứng: grep rỗng trong 3 repo trước phiên này).
  Đây là hạng mục mở lớn nhất của Phase 3.

---

## Bảng chạy / chưa chạy

| Hạng mục | Chạy thật (test/staging) | Chưa chạy |
|---|---|---|
| CreditFlow execute() = audit-only | 14 test pass | Giao dịch thật: chưa có gateway |
| CreditFlow shared API key | 32 test auth pass | Per-user RBAC: chưa implement |
| CreditFlow shared checkpointer | 28 test pass (file fallback proven); **full fast suite 480 passed, 3 skipped, 3 deselected in 42.57s (sau fix hang 4A.4)** | Postgres/thực: driver chưa cài |
| SmartDoc /ready degraded | 5 test pass | Probe prod chưa trỏ /ready |
| SmartDoc checkpointer durable | 266 test pass + restart durability test | Prod Render verify (SQLite file trên shared disk) |
| SmartDoc delete lifecycle | 26 test pass (FakeQdrant có state + mutation check) | Production Qdrant deletion: chưa probe live |
| SupportDesk Redis rate limit | 24 test pass, 388 backend pass | Live redis-server; multi-instance |
| SmartDoc Trivy blocking | YAML valid | PR vi phạm giả — chưa chạy |
| SupportDesk Temporal worker | unit tests | Live Temporal cluster |

## Rủi ro còn lại (top blockers trước production)

1. **SmartDoc backend 2hhz** vẫn lỗi (PHASE2-RECORD: TCP connect, 0 bytes)
   — frontend Pages built against Express retired backend.
2. **SupportDesk AI_PROVIDER=stub** trên Render (`render.yaml:23`) và
   onboard AI chưa có eval trên dữ liệu thật.
3. **CreditFlow disbursement** chưa có cổng tín dụng/ngân hàng thật.
4. **Quality gate chưa chứng minh**: chưa có PR cố ý fail (secret/trivy/quality gate)
5. **Chưa có Argo CD / GitOps** ở dự án nào dù có Terraform + Kustomize.
6. Live verification nợ: Postgres saver, Redis server, Temporal cluster,
   backend SmartDoc, E2E Staging, load test, rollback test.
