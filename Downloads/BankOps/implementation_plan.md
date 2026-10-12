# Implementation Plan — BankOps: Báo cáo biểu đồ + Import dữ liệu ngoài (iOS + Web)

## Overview

Mục tiêu: đưa BankOps từ demo click-through (FakeRepository, màn list tĩnh) thành app dùng thử được cho nhân viên ngân hàng trên iOS + Web, với 2 năng lực mới — báo cáo biểu đồ ngày/tháng và kéo dữ liệu ngoài (CSV/JSON upload, Google Sheets pull, Apps Script webhook push) — đồng thời nối Flutter sang backend thật song song thay vì làm tuần tự.

Phạm vi bao phủ: backend FastAPI (reports aggregation, integrations module, jobs/SSE hoàn thiện), models + migration 003, Flutter (fl_chart, file_picker, repository mở rộng, 3 màn hình nâng cấp + Activity Center với progress), và QA gate theo docs/10-qa.md. Nền tảng verify duy nhất: iOS simulator + Chrome/Web; thư mục android/ giữ nguyên không đụng tới.

Cách tiếp cận: song song 2 track — Track A nối HttpBankOpsRepository và Track B làm tính năng mới — gặp nhau ở repository interface mở rộng. Mọi write đi qua maker-checker, Idempotency-Key, audit + outbox cùng transaction (kế thừa invariant docs/00 từ inventory.py/cash_sim.py hiện tại). Import ngoài không bao giờ ghi thẳng ledger: luôn qua staging, preview, human confirm, draft, approval, post.

## Types

### Backend (Pydantic schemas — mới, trong reports.py và integrations.py)

- ReportBucket: bucket (day "2026-10-12" hoặc month "2026-10"), inbound_qty, outbound_qty, net_qty (Decimal string), receipt_count, issue_count.
- CashFlowBucket: bucket, total_in, total_out, net (Decimal string VND), receipt_count, payment_count.
- StagnationItem: item_id, sku, name, quantity, days_since_last_issue (int hoặc null), severity ("normal"|"watch"|"stagnant"|"critical").
- ConnectionOut: id, type ("csv_upload"|"json_upload"|"google_sheets"|"apps_script"), name, status ("connected"|"error"|"disabled"), last_sync_at, simulation=True.
- StagingPreviewRow: row_index, source_row_key, data (dict), valid (bool), errors (list[str]).
- StagingPreviewOut: run_id, valid_count, invalid_count, rows, mapping_version=1.
- ConfirmIn: run_id, mode (v1 chỉ nhận "all_valid").
- JobOut: id, type, status (queued/running/succeeded/failed/cancelled), processed, total, error_code.

### Flutter (DTOs — mới trong frontend/lib/src/data/models/report_dtos.dart)

- ReportBucket, CashFlowBucket, StagnationItem, StagingPreview, StagingRow, SyncJob (có getter progress = processed/total).
- Severity mapping UI: normal (xám), watch (vàng), stagnant (cam), critical (đỏ); ngưỡng 30/60/90 ngày, config được qua query param.


## Files

### Backend — files mới

- `backend/app/models/integration.py` — IntegrationConnection, IntegrationRun, IntegrationStagingRow (bảng integration_connections, integration_runs, integration_staging_rows theo docs/04 REQUIRED_TABLES).
- `backend/app/models/reconciliation.py` — ReconciliationRun, ReconciliationException (bảng reconciliation_runs, reconciliation_exceptions; R1 hiện skip vì 404).
- `backend/app/api/routes/reports.py` — GET /reports/inventory-daily, /reports/inventory-monthly, /reports/cashflow-daily, /reports/cashflow-monthly, /reports/stagnation.
- `backend/app/api/routes/integrations.py` — connections CRUD tối thiểu, CSV/JSON upload, Google Sheets pull (preview), staging confirm tạo draft, Apps Script webhook receiver.
- `backend/app/api/routes/reconciliations.py` — POST /reconciliations + GET, expected từ ledger/movements so read model, ghi exception không tự vá (R1).
- `backend/alembic/versions/003_integrations_reconciliation.py` — tạo 5 bảng mới (upgrade/downgrade đầy đủ).
- `backend/tests/test_reports.py` — aggregation ngày/tháng + stagnation.
- `backend/tests/test_integrations.py` — upload CSV/JSON, preview/confirm, webhook HMAC, dedup.

### Backend — files sửa

- `backend/app/main.py` — register 3 router mới (reports, integrations, reconciliations).
- `backend/app/api/routes/activity.py` — thay heartbeat-only bằng replay outbox + cursor + Last-Event-ID + resync_required + event job.updated/activity.created (đóng E1).
- `backend/app/api/routes/events.py` — path `/events/stream` lồng prefix thành `/events/events/stream`; sửa thành `/stream` + thêm auth + cursor query param.
- `backend/app/api/routes/cash_sim.py` — thêm `"simulation": true, "currency": "VND"` vào response còn thiếu (test demo-label đang đỏ).
- `backend/app/api/routes/inventory.py` — dòng 720 unreachable return sau `return body` trong create_asset: xóa.

### Flutter — files mới

- `frontend/lib/src/features/reports/widgets/inventory_chart.dart` — BarChart nhập/xuất/tồn theo bucket (fl_chart).
- `frontend/lib/src/features/reports/widgets/cashflow_chart.dart` — line/bar thu/chi/net.
- `frontend/lib/src/features/reports/widgets/stagnation_list.dart` — list tồn đọng với badge severity.
- `frontend/lib/src/features/reports/reports_filter.dart` — chọn day/month + khoảng từ-đến.
- `frontend/lib/src/features/integrations/import_screen.dart` — upload CSV/JSON (file_picker Web+iOS), dán Sheet ID + tab, preview table, nút confirm.
- `frontend/lib/src/features/activity/activity_center.dart` — SSE listener + progress bar job + timeline trạng thái.
- `frontend/lib/src/data/models/report_dtos.dart` — DTOs báo cáo (tách khỏi dtos.dart để diff nhỏ).

### Flutter — files sửa

- `frontend/pubspec.yaml` — thêm `fl_chart: ^0.68.0`, `file_picker: ^8.0.0` (cả 2 hỗ trợ Web + iOS; KHÔNG thêm googleapis — Sheets gọi qua backend).
- `frontend/lib/src/data/repositories/bankops_repository.dart` — thêm 8 method: getInventoryDaily, getInventoryMonthly, getCashflowDaily, getCashflowMonthly, getStagnation, uploadImport, previewRun, confirmRun, getJobs, watchJob.
- `frontend/lib/src/data/repositories/fake_repository.dart` — implement stub các method mới (data mẫu) để UI dev không vỡ.
- `frontend/lib/src/data/repositories/http_bankops_repository.dart` — implement thật + parse envelope lỗi docs/06.
- `frontend/lib/src/app/app_shell.dart` — chuyển RepositoryScope sang HttpBankOpsRepository (giữ Fake cho test); thêm Activity Center icon + route import_screen.
- `frontend/lib/src/features/reports/reports_screen.dart` — TabBar 4 tab (Biểu đồ kho / Dòng tiền / Tồn đọng / Đối soát) + filter ngày/tháng.
- `frontend/lib/src/features/integrations/integrations_screen.dart` — thêm nút Upload CSV/JSON, Connect Sheet, staging runs + job progress inline.

### Files xóa — không có (giữ android/, giữ events.py để tương thích).

### Cấu hình

- `backend/app/config.py`: thêm `google_sheets_api_key` (optional, sandbox), `apps_script_webhook_secret` (bắt buộc nếu bật webhook), `staging_ttl_hours=24`, `sheets_timeout_seconds=15`. Không commit secret thật.
- CORS đã có — Web upload dùng cùng origin API, không cần thêm.


## Functions

### Backend mới

- `reports.py::aggregate_inventory(bucket, date_from, date_to, db)` — group Movement theo ngày/tháng (date_trunc), tách inbound (qty>0)/outbound (qty<0); receipt/issue count từ InventoryRequest theo kind + created_at.
- `reports.py::aggregate_cashflow(bucket, date_from, date_to, db)` — group CashJournal + CashEntry (join entries theo SIM_CASH account), tách thu/chi.
- `reports.py::compute_stagnation(watch_days=30, stagnant_days=60, critical_days=90, db)` — mỗi (location,item) có balance>0: max posted_at issue gần nhất thành days_since_last_issue; severity theo ngưỡng; sort days desc.
- `integrations.py::parse_csv(content)` — csv.DictReader, strip BOM, giới hạn 5MB/10k rows; lỗi → AppError 422 IMPORT_PARSE_ERROR.
- `integrations.py::parse_json_payload(data)` — chấp nhận list hoặc {"rows": [...]}; validate list of objects.
- `integrations.py::fingerprint(rows)` — sha256 canonical JSON để dedup run.
- `integrations.py::validate_item_rows(rows, db)` — check sku tồn tại, quantity>0, location_code tồn tại; trả preview rows kèm errors từng dòng.
- `integrations.py::confirm_run(run_id, user, db)` — tạo InventoryRequest draft (kind=receipt, maker=user) từ valid rows trong cùng transaction + audit + outbox; run.status=confirmed. V1 chỉ mode all_valid: có invalid row → 409 CONFIRM_HAS_ERRORS.
- `integrations.py::fetch_sheet(sheet_id, tab, db)` — gọi Sheets API v4 (httpx, timeout 15s, API key sandbox); allowlist sheet_id (regex + config); lỗi quota → 502 SHEETS_UNAVAILABLE (job failed/retryable).
- `integrations.py::verify_webhook(raw_body, timestamp, signature)` — HMAC SHA-256 + check timestamp ±5 phút chống replay + dedup external_event_id (unique constraint).
- `reconciliations.py::run_reconciliation(scope, db)` — scope cash: sum entries theo account vs CashBalance; scope inventory: sum movements theo (location,item) vs Balance; chênh lệch → exception{expected, actual}; status matched/exception; KHÔNG tự vá (auto_fixed=False).

### Backend sửa

- `activity.py::event_generator` — đọc OutboxEvent từ cursor/Last-Event-ID, yield job.updated/activity.created, heartbeat 15s; cursor quá cũ (> retention 7 ngày) → yield resync_required.
- `events.py::events_stream` — path `/events/stream` thành `/stream` (prefix /events đã gắn ở main.py), thêm Depends(get_current_user), cursor là query param.
- `cash_sim.py` responses — bổ sung key simulation nơi thiếu (giữ nguyên ledger logic).
- `inventory.py::create_asset` — xóa dòng unreachable cuối hàm.

### Flutter mới

- `http_bankops_repository::getInventoryDaily/Monthly`, `getCashflowDaily/Monthly`, `getStagnation()` — GET kèm query, parse Decimal string sang double tại client.
- `uploadImport(fileBytes, filename, type)` — POST multipart /integrations/uploads; Web dùng bytes, iOS dùng path.
- `previewRun(runId)` / `confirmRun(runId)` — GET preview, POST confirm kèm Idempotency-Key uuid v4.
- `watchJob(jobId, onEvent)` — SSE stream parse event:/data: lines, fallback polling 3s khi platform không hỗ trợ stream.
- `activity_center.dart::ActivityCenter` — StatefulWidget listen SSE, render LinearProgressIndicator + timeline check/wait/pending.

### Flutter sửa

- `fake_repository.dart` — thêm 8 method stub trả mẫu.
- `reports_screen.dart::build` — TabBar 4 tab + filter; mỗi tab FutureBuilder riêng (tab lỗi không kéo sập tab khác).
- `integrations_screen.dart` — thêm 3 nút action + preview DataTable (50 dòng đầu + dénonination còn lại).

### Functions xóa — không có.


## Classes

- Mới backend: IntegrationConnection, IntegrationRun, IntegrationStagingRow (models/integration.py, kế thừa Base + TimestampMixin); ReconciliationRun, ReconciliationException (models/reconciliation.py).
- Mới Flutter: ReportBucket, CashFlowBucket, StagnationItem, StagingPreview, StagingRow, SyncJob (report_dtos.dart).
- Sửa: không đổi class hiện tại nào ngoài implement thêm method ở 2 repository implementation.
- Xóa: không có.

## Dependencies

- Backend: thêm `httpx` (gọi Sheets API; kiểm tra pyproject.toml trước — có rồi thì pin version hiện tại). Không thêm google-api-python-client (nặng, không cần — gọi REST trực tiếp). Không thêm celery/redis: jobs chạy inline + outbox hiện tại đủ demo; worker lease đã có ở worker.py.
- Flutter: thêm `fl_chart: ^0.68.0`, `file_picker: ^8.0.0` (Web+iOS ok; KHÔNG config Android). Không thêm googleapis (Sheets đi qua backend, client không cần scope mới).
- Verify tương thích trước khi chốt: flutter pub deps + poetry check + test hiện tại xanh mới tiếp.

## Testing

- Backend mới: tests/test_reports.py — seed movement/journal mẫu, assert bucket day/month đúng inbound/outbound/net (Decimal string), severity đúng ngưỡng, filter date_from/to có tác dụng. tests/test_integrations.py — CSV hợp lệ thành preview valid; CSV sai thành 422; confirm tạo draft + maker=user + run confirmed; confirm lần 2 cùng key replay; webhook sai HMAC thành 401; trùng external_event_id dedup không tạo run mới; Sheets fetch mock httpx (không gọi Google thật).
- Backend sửa: chạy lại test_acceptance_qa.py (A1–A4/I1–I3/C1–C3/E1/S1/R1) + test_contract_vs_docs.py sau mỗi thay đổi routes; mục tiêu đóng R1 (hiện skip 404), E1 (SSE replay), demo-label, missing-module-endpoints.
- Flutter: test/reports_chart_test.dart (render chart với data mẫu, không crash khi rỗng), test/import_preview_test.dart (preview hiển thị valid/invalid rows). flutter analyze 0 error + flutter test trên Chrome + build ios --no-codesign + build web.
- E2E staging demo (data giả): maker upload CSV → preview → confirm → draft → checker approve → movement posted → chart cập nhật; Sheets sandbox pull → staging → confirm; webhook test bằng curl HMAC → run created → SSE progress → Activity Center hiển thị.
- Không dùng dữ liệu ngân hàng thật ở bất kỳ bước nào; Sheet test là sandbox do team sở hữu.

## Implementation Order

1. Track A nền: models/integration.py + reconciliation.py + migration 003 (upgrade/downgrade, test trên DB trống).
2. Track A: reports.py (5 endpoint read-only) + tests/test_reports.py xanh.
3. Track A: sửa activity.py SSE + events.py path/auth + cash_sim.py demo-label + inventory.py unreachable — đóng E1 + contract tests.
4. Track A: reconciliations.py + test R1 xanh (không tự vá).
5. Track B: integrations.py upload CSV/JSON + staging preview/confirm + tests/test_integrations.py (offline, chưa cần Google).
6. Track B: Sheets pull (fetch_sheet + allowlist + mock test) rồi webhook receiver (HMAC + replay + dedup) — cần config secret ở bước này.
7. Flutter deps: thêm fl_chart + file_picker, flutter pub get, fix analyze.
8. Flutter repository: mở rộng interface + fake stub + http implement (reports + integrations + jobs/SSE).
9. Flutter UI reports: filter + 3 widget chart/list + TabBar ở reports_screen.
10. Flutter UI integrations: import_screen (upload + Sheet ID + preview + confirm) + nút action ở integrations_screen.
11. Flutter Activity Center: SSE watchJob + progress + timeline, gắn vào app_shell.
12. AppShell chuyển HttpBankOpsRepository + QA tổng: pytest full, flutter analyze/test, build ios --no-codesign + build web, E2E staging demo maker→checker.

- `backend/app/seed.py` — thêm capability mapping cho reports/integrations.
