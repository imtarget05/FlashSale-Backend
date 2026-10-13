# KẾ HOẠCH HOÀN THIỆN PRODUCTION — HANDOFF CHO AI AGENT TIẾP THEO

Bản kế hoạch này là handoff trực tiếp: AI nhận việc chạy đúng sprint thứ tự,
không làm ngoài phạm vi, trả bằng chứng thật (test/log/curl) cho từng nhận định.
Mọi quyết định nền tảng/SLO cần con người tại **Sprint 0** — không suy diễn thay.

---

## 0. BỐI CẢNH (đọc trước, không phải khám phá lại)

Ba dự án trong `/Users/mainguyenbinhtan/Downloads/TMA/`:
**CreditFlow** (FastAPI + sklearn/XGBoost + LangGraph, credit decision support),
**Smart-Document-Chatbot** (Spring Boot 3.2 + React + FastAPI agent + LangGraph + Qdrant),
**SupportDesk** (FastAPI + TS gateway + React + Kafka/Temporal/Qdrant, ticket workflow).

Đã hoàn tất (đừng làm lại): PHASE0_ASSESSMENT.md, PHASE1/2/3_REPORT.md,
docs/render-readiness.yml (shared), gitleaks HARD, JaCoCo gate 65%,
CreditFlow coverage gate 75%, ADR pack pointer, PHASE4_REPORT.md
(hardening: audit-honesty, /ready degraded, durable checkpointer SmartDoc,
Redis rate-limit SupportDesk, Trivy block SmartDoc, delete-lifecycle prove,
README contradictions fixed). Kiểm chứng `git status` trước khi sửa file;
một số thay đổi đã được commit bởi tiến trình song song — KHÔNG ghi đè.

---

## 1. SPRINT 0 — QUYẾT ĐỊNH NỀN TẢNG (human-gate, làm trước)

AI nhận việc **KHÔNG tự chọn**. Soạn một file quyết định
`docs/DECISIONS.md` (mẫu bên dưới), hỏi con người, chờ câu trả lời từng dòng
trước khi chạy Sprint 1+. Nếu con người bảo "tự quyết", dùng giá trị mặc định.

| # | Quyết định | Mặc định đề xuất | Ghi chú |
|---|---|---|---|
| D1 | CreditFlow production target | **Azure Container Apps** (đã canonical, README + TF hiện tại) | Bỏ hoặc ghi rõ Render mirror là demo |
| D2 | SmartDoc production target | **Render (backend Spring + llm-router + agent) + Cloudflare Pages (frontend)** | Azure TF giữ làm capability evidence; K8s kustomize là dev-stub |
| D3 | SupportDesk production target | **Render + Pages** | Azure workflow hiện chỉ warn — đổi fail-closed hoặc gỡ |
| D4 | CreditFlow disbursement | **Lock sản phẩm = decision support** (KHÔNG giải ngân thật) hoặc mua cổng tín dụng thật | Không có cổng thật trong tay → mặc định lock |
| D5 | LLM provider thật | SmartDoc: Ollama local (đã có) hoặc CF Workers AI đã dùng. SupportDesk: provider trả phí + **budget cap** | Bỏ AI_PROVIDER=stub ở render.yaml |
| D6 | SLO/RPO/RTO từng dự án | Ghi 3 giá trị/1 dự án, kèm uptime %, giờ bảo trì | SupportDesk nhỏ nhất (demo) |
| D7 | Dữ liệu nhạy cảm + pháp lý | Ghi loại data, lưu trữ region, retention, ai vận hành | Nhất là CreditFlow bên trong |
| D8 | Ngân sách hạ tầng | Số $/tháng mỗi dự án | Ảnh hưởng scale/autoscaling |

Nghiệm thu Sprint 0: `docs/DECISIONS.md` có D1–D8, mỗi dòng người duyệt hoặc
"auto: default". ADR mới (nếu quyết định ≠ hiện trạng) ghi theo
docs/adr/ pattern có sẵn.

---

## 2. SPRINT 1 — HẠ TẦNG PRODUCTION-CRITICAL (3 wave × 2-3 agent song song)

### Wave 1.1 — SmartDoc backend triage (CAO NHẤT)
**Bằng chứng sự cố:** `docs/PHASE2_REPORT.md` — backend 2hhz TCP connect
OK nhưng 0 bytes × 4 trong 30–90s (asleep/suspended hoặc deploy hỏng).
**Việc:**
1. Xác định endpoint canonical: `smart-document-chatbot...onrender.com` hay
   host từ `render.yaml`; test `curl -v` + `/actuator/health` với timeout.
2. Nếu render dashboard không thể bật: ghi RECORD trong PHASE5_REPORT.md,
   KHÔNG khẳng định backend live.
3. Sửa `cd.yml`/`render-readiness.yml` để bước verify-readiness fail LOUD
   (không im lặng 0-byte) — đã có shared workflow, chỉ cần đúng health-path
   + retry.
4. Frontend Pages hiện build against **retired Express backend** (README:26) —
   chuyển `VITE_API_URL` sang backend Spring canonical như `cd.yml` mô tả.
**Nghiệm thu:** 1 đoạn curl log 200 OK trên backend Spring + frontend
Pages gọi API thành công trong browser (hoặc 1 đoạn Playwright log).

### Wave 1.2 — SupportDesk AI provider thật
1. `render.yaml:23` `AI_PROVIDER=stub` → provider thật (theo D5) + model,
   api key từ Render env secret (KHÔNG commit key).
2. `backend/app/config.py` — thêm cost cap (max tokens/tháng, max $/request
   fallback stub) và log per-request token/cost.
3. Eval trên dữ liệu thật: `evaluation/evaluate.py` + `eval_suite.py --check`
   hiện chạy stub offline — bổ sung chạy với provider thật trên fixture
   (cần nút budget: số lượng ca eval tối thiểu 20, đo precision/cost).
4. Kiểm soát: nếu cost cap chạm → trả 429 + alert; không tự retry vô hạn.
**Nghiệm thu:** 1 run eval thật in ra bảng precision/latency/cost +
README cập nhật bỏ "stub".

### Wave 1.3 — CreditFlow: lock hoặc gateway
- Nếu D4 = lock: xoá/bôi đậm mọi chuỗi "disbursement" gây hiểu lầm
  (`docs/REPAIR-PLAN.md:178` tick checkbox), API `execute()` trả
  `disbursement.status=pending:not-implemented` (đã rõ), README position
  = "decision support, tài liệu quyết định + audit; giải ngân là roadmap".
- Nếu D4 = mua gateway: tích hợp sandbox cổng, idempotency key, đối soát
  cuối ngày, retry/compensation — việc này ≥ 1 tuần, tách plan riêng.
**Nghiệm thu:** README + API doc khớp code; E2E test decision-support vẫn
xanh (đã có 14 test section 4A).

---

## 3. SPRINT 2 — STAGING CHẠY THẬT (mỗi dự án 1 môi trường staging)

Mục tiêu: hết "chạy local" → từng service chạy cùng DB/queue/vector/LLM thật.

1. CreditFlow staging: ACA môi trường staging (TF param env name) hoặc
   docker-compose full với Postgres thật + models SHA256-gated.
2. SmartDoc staging: `docker/` hoặc Render preview: Postgres (Neon) +
   Qdrant (cloud hoặc container) + llm-router + backend + agent + memory
   DB file bền vững (checkpoint sqlite path = volume shared).
3. SupportDesk staging: compose full (pg16 + kafka KRaft + outbox + workers
   + qdrant + temporal + gateway) — chứng minh Kafka outbox ≠ fallback
   in-memory; chạy `alembic upgrade head` trên pg thật.
4. Mọi staging dựng bằng 1 lệnh/script được tài liệu hoá (`deploy/` hoặc
   compose profile) — đây là nghiệm thu DR ở mức nhẹ.

**Nghiệm thu mỗi dự án:** table service→status→URL→bằng chứng (curl/smoke)
trong `docs/STAGING_STATUS.md`.

---

## 4. SPRINT 3 — E2E + API/UI TEST TRÊN STAGING

Mỗi dự án 1 hành trình chính, chạy trên staging (không mock):
- CreditFlow: submit application → model score → workflow approve (HITL)
  → execute (audit) → ledger; verify idempotency (retry request → 1 lần).
- SmartDoc: upload PDF → parse → index (Qdrant thật) → chat hỏi → citation
  → delete → verify retrieve trả rỗng (test đã có ở unit — chạy lại trên
  staging).
- SupportDesk: ticket → classify → evidence → draft → human approve →
  send/audit (Temporal worker chạy thật — xem Sprint 5).
Bổ sung frontend: Playwright (SmartDoc đã có `frontend/playwright.config.ts`,
SupportDesk/CreditFlow dùng `frontend-patterns`/`browser-qa` skill).
Lỗi mỗi bước: mã lỗi + trạng thái + cách retry rõ (grep `HTTPException`,
`ApiError`, status codes).

**Nghiệm thu:** 1 bảng journey→step→request→response→assert; tối thiểu 1
CI job (SMOKE) chạy staging suite tự động; screenshot/artifact kèm.

---

## 5. SPRINT 4 — SECURITY & DevSecOps GATE THẬT (cho CẢ BA dự án)

Hiện trạng: SmartDoc Trivy nay chặn merge (ci.yml:330-385). CreditFlow +
SupportDesk **chưa có** Trivy/Sonar (grep rỗng). Chưa dự án nào có
PR-cố-ý-vi-phạm để chứng minh gate chặn.

1. **Trivy image scan** ở 3 repo: fail khi CRITICAL/HIGH (unfixed → ignore
   qua config có ngày hết hạn, KHÔNG continue-on-error chung). Copy pattern
   từ `Smart-Document-Chatbot/.github/workflows/ci.yml:330-385`.
2. **SonarQube/SonarCloud**: SmartDoc đã có `sonar-project.properties`
   (backend + frontend) nhưng thiếu job — thêm job scan + quality gate
   chặn PR. CreditFlow/SupportDesk: khởi tạo config + job.
3. **Coverage gate**: SupportDesk hiện chưa có (chỉ eval-gate) — quyết
   định theo `docs/PHASE3_REPORT.md` (đã cố ý bỏ; nếu staging phát hiện
   lỗi → thêm 70%).
4. **PR chứng minh gate**: tạo 1 PR throwaway mỗi repo chứa (a) test fail,
   (b) secret giả, (c) lỗ hổng mẫu (vd `find . -name "*.py" | xargs` — dùng
   một vuln HQL lỗi thời rõ ràng). Yêu cầu: CI đỏ ĐÚNG chỗ → merge blocked
   → chụp log → đóng PR. Ghi kết quả vào PHASE REPORT.
5. **Secret scan / SBOM**: gitleaks đã HARD ở 3 repo; thêm SBOM
   (`syft`/`trivy --format sbom`) pub trên release + image digest immutable
   (đã build-tag SHA ở một số workflow — chuẩn hoá cả 3).
6. **Signed artifacts** (nếu yêu cầu chuỗi cung ứng): cosign sign image
   trên main — optional, ghi quyết định trong DECISIONS.md.

**Nghiệm thu:** table repo→gate→cấu hình→đã chứng minh chặn? (cột bắt buộc);
PR vi phạm log link.

---

## 6. SPRINT 5 — DURABILITY & DISASTER RECOVERY (chạy trên staging)

Mục tiêu không mất state, không hành động 2 lần.

1. **Live Redis** (SupportDesk): chạy redis-server (container), test
   rate-limit chia sẻ 2 instance cùng key → đúng 429 chung. Gỡ note
   "unverified live redis" trong PHASE4_REPORT (hoặc ghi test đã chạy).
2. **Postgres checkpointer live** (CreditFlow): cài `langgraph-checkpoint-postgres`
   vào môi trường staging, `DATABASE_URL` trỏ pg thật, chạy test
   `tests/test_checkpoint_shared_storage.py` với `LIVE_TESTS=1`; verify
   resume sau kill process giữa chừng (kill -9 agent → restart → trạng thái
   workflow cũ). Client: ghi kết quả đúng file.
3. **Temporal run-or-cut** (SupportDesk): chạy temporal dev server trên
   staging (docker compose đã có `temporal-auto-setup`), `TEMPORAL_ENABLED=1`,
   approval **sống qua restart**: gửi approval → kill worker → start worker
   → approve → send. Nếu không thành công trong Sprint này → **cut Temporal**:
   ghi rõ "local_runner là canonical, không durable" trong README + bỏ
   Temporal khỏi stack claim (tránh "code exists" thành lời quảng cáo).
   (Lưu ý: README đã sửa trung thực — phase này là quyết định giữ hay cắt.)
4. **Process-kill / crash tests**: kill mid-run ở mỗi dự án → verify
   idempotency (outbox không gửi 2 lần, workflow resume).
5. **Backup/restore drill**: backup Postgres + Qdrant snapshot + Redis AOF
   → restore vào DB mới → kiểm tra dữ liệu đủ. Ghi runbook
   (`deploy/runbooks/RESTORE.md` mỗi dự án).
6. **Không mất trạng thái**: SmartDoc memory file checkpointer phải nằm
   volume shared giữa replicas (đã có settings `LANGGRAPH_CHECKPOINT_DB`).

**Nghiệm thu:** mỗi item có log kill→restart→resume + bảng "trạng thái
không mất / hành động chạy đúng 1 lần".

---

## 7. SPRINT 6 — OBSERVABILITY, SLO, LOAD, CANARY

1. **Tracing 1 request xuyên stack** (mỗi dự án): OpenTelemetry —
   frontend → API → queue → agent → LLM → DB. Ít nhất: trace id format
   chung, span log LLM token/cost/latency.
2. **Metrics + alerts**: lỗi 5xx, latency p95, queue backlog, cost/token,
   retrieval quality (SmartDoc: retrieval hit-rate; SupportDesk: ticket
   resolution), budget burn. Alert có người nhận (email/webhook) — KHÔNG
   để alert vào folder trống.
3. **Runbook cho từng sự cố phổ biến** (backend down, DB reconnect,
   LLM timeout, queue stuck) — 1 trang, vào `deploy/runbooks/`.
4. **Load test**: k6 (SmartDoc đã có `load-test.yml`) chạy staging:
   ghi throughput, p95, error%. Nếu < mục tiêu → autoscale policy
   (đã có HPA k8s manifest / ACA http-scale).
5. **Canary/rollback**: deploy mới theo percentage hoặc staging→prod smoke
   → rollback bằng image digest cũ (ghi lệnh thật). Test rollback thật 1
   lần trên mỗi dự án.
6. **Pentest/security review** với dữ liệu thực tế: dùng
   `security-review`/`security-bounty-hunter` skill hoặc quét phụ thuộc
   (pip-audit/npm audit/trivy) — ghi findings + triage.

**Nghiệm thu:** dashboard/grafana (hoặc endpoint /metrics) + 1 alert đã
trigger thật + 1 runbook đã dùng thật + 1 rollback đã chạy thật.

---

## 8. SPRINT 7 — CỔNG CUỐI CÙNG & QUYẾT ĐỊNH BÁN GÌ

Checklist bắt buộc TRƯỚC khi gọi bất kỳ feature nào "production ready":
- [ ] E2E staging xanh (Sprint 3)
- [ ] Security gates chặn thật = PR vi phạm bị chặn (Sprint 4)
- [ ] AI eval đúng ngưỡng (Sprint 1/2): groundedness, citation, prompt
      injection, thiếu evidence, tool lỗi, timeout, chi phí, latency
- [ ] Load test đạt (Sprint 6)
- [ ] Restart/restore test xanh (Sprint 5)
- [ ] Production smoke đủ (curl + 1 user journey thật)

Nguyên tắc: KHÔNG cần "mọi feature" hoàn hảo — chỉ công bố/bán **luồng nào
vượt cổng**. Mỗi luồng đi qua cổng ghi 1 dòng trong
`docs/PRODUCTION_READY_MATRIX.md`: feature → cổng nào pass → bằng chứng →
ngày → người duyệt.

**Nghiệm thu:** matrix có ≥1 luồng PASS toàn cổng mỗi dự án (không bắt buộc
3/3 dự án cùng ngày).

---

## 9. QUY TẮC THỰC THI CHO AI NHẬN VIỆC

1. Mỗi turn: 1 sprint cho 1 dự án (hoặc 1 wave song song 2-3 slice độc lập).
2. Trả về mỗi slice: `RESULT: DONE/PARTIAL/BLOCKED` + file:line + lệnh +
   output thật. PARTIAL/BLOCKED = nêu blocker và bước tiếp theo, không
   tuyên bố xong.
3. KHÔNG "deploy thành công" khi thiếu secret hoặc chỉ build — phải smoke
   thật trên môi trường chạy.
4. KHÔNG dùng README/manifest làm bằng chứng chạy.
5. `git status` kiểm tra TRƯỚC khi sửa; giữ nguyên working tree người khác.
6. Không self-host Argo CD nếu không có đích K8s; GitOps chỉ khi D1-D3 chọn
   K8s.
7. Mọi gate: ko dùng `continue-on-error` chung cho security; ngoại lệ ghi
   ngày hết hạn.
8. Ghi nhật ký tổng hợp vào `docs/PHASE5_REPORT.md`, `PHASE6_REPORT.md`,
   `PHASE7_REPORT.md` tương ứng (pattern giữ nguyên PHASE1-4).
9. Sử dụng Jev (tool) cho các phân loại/routing khi cần (test fail thật vs
   flaky, file liên quan), KHÔNG dùng cho viết code/plan.
10. Nếu một slice > 2-3 phút chờ → tách nhỏ, chạy song song tối đa 2-3 agent.

---

## 10. NGUỒN LỰC CẦN TỪ CON NGƯỜI (gửi 1 lần, để khỏi đứng hình)

- Quyết định D1–D8 (Sprint 0).
- Secret/credential: Render API key + service_id (3 repo), Neon/Postgres
  DATABASE_URL staging, Qdrant cloud API key (hoặc để container), LLM API
  key + budget, Redis URL (hoặc để container), Azure credentials (nếu giữ ACA).
- Quyền tạo PR/release branch để chạy PR-vi-phạm-giả (Sprint 4).
- Chấp thuận chi phí: model API, Neon/Qdrant cloud, Render, possible
  SonarCloud free.