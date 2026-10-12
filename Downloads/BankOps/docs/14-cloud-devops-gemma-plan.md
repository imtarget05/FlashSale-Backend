# 14 — Cloud / DevOps + Gemma on-device + Đối soát AI-assisted (Plan)

> Grounded: compose `postgres:16-alpine/api:8000/worker/frontend-web:8080`; migration duy nhất `001_initial` 23 tables; `backend/app/seed.py:22-23` `drop_all` destructive; worker stub `sleep 30`; SSE heartbeat-only; gaps: CI/migration/backup/monitoring/secrets/rollback. Invariant: không auto-post từ staging (docs/09:18-20).

## 1. Kiến trúc cloud / DevOps (local → staging → demo)

### 1.1 Sơ đồ ASCII

```text
[Dev laptop] --push--> [GitHub Actions: lint/test/build/scan]
                            |
                            v
                   [Artifact Registry (pinned :sha)]
                            |
        +-------------------+-------------------+
        v                                       v
[Cloud Run: bankops-api]              [Cloud Run job: bankops-worker]
        |                                       |
        +-----------> [Cloud SQL PG16] <--------+
        |                    | (PITR on)
        v                    v
[Secret Manager]     [Cloud Storage: pg_dump daily + GCS backup]
        |
[Cloud LB + CDN] --> frontend-web (static) --> API
        |
[Cloud Logging/Monitoring + Alertmanager]

ALT (rẻ): Fly.io / Railway — 1 app api + 1 worker + managed PG + volume backup.
```

Local giữ nguyên `docker-compose.yml` (postgres/api/worker/frontend-web). Staging = compose + `.env.staging`. Demo = Cloud Run + Cloud SQL.

### 1.2 CI/CD stages (GitHub Actions)

| Stage | Việc | Gate |
|---|---|---|
| 1. Lint/type | ruff, mypy, dart analyze | fail → block |
| 2. Test | pytest unit + PG integration + OpenAPI diff | coverage ≥80% |
| 3. Scan | pip-audit, gitleaks, trivy image | high/critical block |
| 4. Build | pinned image `:sha`, SBOM | push Artifact Registry |
| 5. Migrate staging | snapshot → `alembic upgrade head` → smoke E2E | fail → rollback snapshot |
| 6. Deploy demo | Cloud Run deploy pinned tag | health `/health`, `/ready` pass |
| 7. Verify | smoke: login→staging→approve→post→audit | fail → rollback image |

### 1.3 Migration (Alembic)

- Quy tắc expand → backfill → contract; mỗi revision 1 mục đích, có downgrade test trên staging.
- Hiện tại chỉ `001_initial` (23 tables) — M1 tách baseline + thêm test `upgrade/downgrade` vòng tròn.
- Guard seed: `seed_demo()` chứa `drop_all` (`backend/app/seed.py:22-23`) → chỉ chạy khi `APP_ENV=demo`; staging/prod gọi sẽ abort + alert. Seed demo phải idempotent (check-exists trước insert).
- Pre-deploy luôn snapshot Cloud SQL; expand migration chạy online, contract chạy ở release sau.

### 1.4 Backup / restore

| Hạng mục | Demo (GCP) | Alt Fly/Railway |
|---|---|---|
| pg_dump daily | Cloud Scheduler → Cloud Storage, giữ 14 bản | cron + volume snapshot |
| PITR | Cloud SQL PITR on, giữ 7 ngày | PG WAL / provider PITR |
| RPO / RTO | RPO ≤24h (dump) / ≤5ph (PITR), RTO ≤1h | RPO ≤24h, RTO ≤2h |
| Diễn tập | restore vào env riêng mỗi sprint | restore vào staging |

### 1.5 Monitoring

- Health: `/health/live` (không phụ thuộc DB), `/health/ready` (check DB pool), `/metrics` (latency p50/p95, err rate, outbox lag, SSE conns, recon exceptions, pool usage).
- Alerts: err>1% 5ph, outbox lag>5ph, ready fail 2 lần, disk>80%, job fail-rate>10%.
- Log structured JSON + trace ID; cấm log token/raw rows/secrets.

### 1.6 Secrets

- Ref pattern: `JWT_SIGNING_KEY_REF`, `CONNECTOR_SECRET_REF`, `BANKOPS_<ref>` env thực tế từ Secret Manager. Không hardcode `bankops/bankops` (hiện compose đang hardcode → M1 fix bằng `.env` + placeholder trong `.env.example`).
- Fix `ALLOWED_ORIGINS=*` (compose:32): staging/demo set domain explicit; `*` chỉ local.
- Rotation: signing key versioned, grace 2 key; session secret 32+ bytes từ Secret Manager.

### 1.7 Rollback

- App: redeploy pinned image trước (`:sha` cũ) — forward-fix, không `latest`.
- DB: không downgrade mù; dùng forward migration fix hoặc restore snapshot đã diễn tập. Mọi rollback ghi audit.

### 1.8 Chi phí ước tính (demo/tháng)

| Hạng mục | GCP (~20-60 USD) | Fly.io/Railway (~15-35 USD) |
|---|---|---|
| Compute api+worker | Cloud Run 2×0.5vCPU/512MB ~10-20 | 2×shared ~6-12 |
| DB | Cloud SQL db-f1-micro ~10-15 / db-g1-small ~25-35 | Managed PG ~7-15 |
| Registry | Artifact Registry ~1-2 | built-in ~0 |
| Secrets | Secret Manager ~1 | env vault ~0 |
| LB/logs/storage | LB ~5-10 + logs/GCS ~2-5 | egress/vol ~2-8 |
| **Tổng** | **~20-60** | **~15-35** |

Khuyến nghị: demo GCP nếu cần PITR/LB chuẩn; tiết kiệm → Fly.io/Railway.

## 2. Phân công AI vs Rules (T1–T8)

Nguyên tắc: AI chỉ summarize/explain/suggest; Rules/DB tính toán và post. Human maker≠checker gate mọi post. Invariants: no auto-post từ staging, balanced ledger, append-only audit, maker≠checker.

| ID | Task | AI (Gemma) | Rules/DB | Human gate |
|---|---|---|---|---|
| T1 | Tóm tắt chênh lệch đối soát | summarize diff đã redacted | tính diff, balanced check | maker review |
| T2 | Giải thích discrepancy | explain nguyên nhân khả dĩ + conf | rule classify loại lệch | maker xác nhận |
| T3 | Gợi ý bước xử lý | suggest checklist (không ID thật) | validate bước hợp lệ | checker duyệt |
| T4 | Mapping import staging | suggest mapping cột | dedup fingerprint/row-key | admin confirm |
| T5 | Phân loại job lỗi | explain log lỗi | retry/backoff/dead-letter | operator retry |
| T6 | Tóm tắt audit | summarize theo filter | append-only query | auditor đọc |
| T7 | Soạn export/summary | draft text + redacted | snapshot + schema ver | maker ký |
| T8 | Trả lời FAQ demo | answer từ docs, banner DEMO | không chạm ledger | không cần gate |

AI không bao giờ: post ledger, approve thay người, tạo user/cấp quyền, gửi outbound webhook, đọc secret.

## 3. Gemma on-device

### 3.1 Matrix nền tảng

| Nền tảng | Model pin | Runtime preferred | Alt | RAM |
|---|---|---|---|---|
| Android | Gemma 3n E2B-IT int4 | `flutter_gemma_litertlm` 1.8.5 | `.task` qua `flutter_gemma_mediapipe` / `tasks-genai` 0.10.29 | ~2 GB |
| iOS | Gemma 3n E2B-IT int4 | `flutter_gemma_litertlm` | `.task` mediapipe | ~2 GB (E4B ~3 GB) |
| Web | Gemma 3n E2B `.web.task` | transformers.js/MediaPipe | GPU-only, không CPU | theo VRAM |
| Desktop | `.litertlm` (M3/Win11 DX12) | `dart:ffi` LiteRT | GGUF Q4_K_M via llama.cpp/Ollama | ≤4 GB |
| Fallback | Gemma 3 1B-IT | cùng runtime | rule-only mode (không model) | <1 GB |

E4B chỉ khi thiết bị ≥4 GB; mặc định pin E2B.

### 3.2 Constraints & fallback

- Context 32K, practical 4096 tokens; maxOutput 512–1024; temp 0.1–0.3 (T1/T2 0.1, T3 0.2–0.3); top-p 0.9.
- Airplane-offline bắt buộc chạy được T1–T3 (model bundled, không fetch).
- Input redacted trước khi vào model (mask tên/SĐT/STK/số tiền nhạy cảm → `[REDACTED]`); output qua validator (regex ID/URL/secret → chặn).
- Fallback 2 tầng: Gemma 3n E2B → Gemma 3 1B-IT → rule-only templates + server summary (redacted). Hết model vẫn đối soát được bằng rules.

## 4. Benchmark B

- Thiết bị: 1 phone (Pixel 8 / Galaxy S24, Android 14+) + 1 desktop (M3 / Win11 DX12).
- Tasks trên fixtures tổng hợp từ `sample-data/` (cash-sim/inventory/users): T1 summarize diff (n≥20), T2 explain discrepancy (n≥20), T3 suggest steps (n≥15). Không dữ liệu thật.
- Metrics: p50/p95 latency, tok/s decode, peak RSS, battery (%/10 lần chạy phone).
- Rubric 0–3/tiêu chí (đúng-factual, đủ-coverage, không-bịa-ID) + hallucination 0% ở T3 (bịa ID/bước cấm = fail).
- Pass thresholds: p50 ≤15s, p95 ≤30s; decode ≥10 tok/s phone, ≥18 tok/s desktop (tham chiếu M3 232 pref/27 dec); RSS ≤2.5 GB phone, ≤4 GB desktop; rubric mean ≥2.0.

| Metric | Phone pass | Desktop pass |
|---|---|---|
| p50 / p95 | ≤15s / ≤30s | ≤10s / ≤20s |
| decode | ≥10 tok/s | ≥18 tok/s |
| peak RSS | ≤2.5 GB | ≤4 GB |
| rubric | ≥2.0, hallu 0% (T3) | ≥2.0, hallu 0% (T3) |

Chạy `benchmark_b.sh` 3 lần/máy, báo cáo median + min/max; fail → xuống model nhỏ hơn hoặc giảm maxOutput.

## 5. Luồng đối soát AI-assisted (6 bước) + API

```text
(1) Trigger: job/import đóng staging batch (signed webhook/manual)
(2) Staging: rows vào staging + fingerprint/dedup + TTL 24h
(3) Rule checks: validate, mapping preview, balanced/dedup/bất thường → diff report
(4) AI explain: input redacted → Gemma T1/T2/T3 + confidence:
      ≥0.80 inline | 0.50–0.80 collapsed + "cần kiểm tra" | <0.50 suppressed
      + banner DEMO + audit ai.explain / ai_suppressed_low_confidence
(5) Maker/checker: maker≠checker duyệt draft → atomic post (idempotency key)
(6) Audit + re-run: append-only audit, SSE tiến trình, re-run cho phép draft chưa post
```

API additions:

| Method & path | Input | Output |
|---|---|---|
| `POST /api/v1/reconciliations` | staging_batch_id, scope | `{id, status: staged, diff_summary}` |
| `GET /api/v1/reconciliations/{id}` | — | diff + rule checks + ai_explain (theo ngưỡng hiển thị) |
| `POST /api/v1/reconciliations/{id}/ai-explain` | task T1/T2/T3, redacted payload | `{text, confidence, model_ver, suppressed?}` |

Mọi ai-explain lưu `{model_ver, prompt_hash, conf, redacted_in, suppressed}` vào audit.

## 6. Milestones, nghiệm thu, rủi ro

### 6.1 Milestones

| MS | Phạm vi | Exit |
|---|---|---|
| M1 hardening+CI | fix secrets/CORS, CI lint/test/scan/build, Alembic baseline test | pipeline xanh, không hardcode |
| M2 staging+backup | env staging, snapshot+PITR, restore drill, monitoring/alerts | RPO/RTO đo được, drill pass |
| M3 Gemma integrate | litertlm bundle E2B, redaction/validator, fallback 1B→rule-only, 3 API | offline T1–T3 chạy |
| M4 Benchmark B | fixtures n≥20/20/15, 2 máy, rubric+hallu | thresholds §4 pass |
| M5 demo hardening | banner DEMO, maker≠checker E2E, rollback drill, docs | E2E xanh, drill pass |

### 6.2 Tiêu chí nghiệm thu

AC-R1: balanced ledger mọi post; A1–A4: auth/maker≠checker/audit append-only/idempotency; C2: staging TTL+dedup; I2: webhook HMAC+replay-reject; G1: no auto-post staging; AI1: conf gating + suppress log; AI2: hallu 0% T3; E2E: staging→post→audit→re-run; MIG: upgrade/downgrade vòng tròn + seed guard.

### 6.3 Rủi ro + mitigation + rollback

| Rủi ro | Mitigation | Rollback |
|---|---|---|
| Hallucinated IDs (AI bịa) | validator regex + rubric, suppress T3 không ID | suppress + rule-only |
| Prompt leak / PII vào model | redaction trước infer, không log raw | xóa cache, rotate key |
| Auto-resolve pressure (bỏ gate) | G1 enforced code + test | revert commit, audit |
| Double-post | idempotency key + atomic txn | forward-fix reversal |
| Staging shortcut (post thẳng) | API từ chối khi chưa approve | restore snapshot |
| Model/drift (đổi weights) | pin weights + prompt_hash audit | pin về bản cũ |
| Scope creep | freeze M1–M5, backlog docs/13 | cắt scope về demo |

*File <400 dòng, chỉ plan — không sửa code.*
