# 13 — Task backlog ưu tiên

Mỗi task là một PR reviewable; test/contract/migration là phần của task, không dồn cuối kỳ. P0 bắt buộc trước demo, P1 hoàn thiện MVP.

| ID | Ưu tiên | Phụ thuộc | Task / bằng chứng hoàn thành |
|---|---|---|---|
| B01 | P0 | — | repo skeleton, CI, compose; boot/health chạy |
| B02 | P0 | B01 | Alembic + seed giả; migrate/seed hai lần không trùng |
| B03 | P0 | B02 | auth demo + RBAC + object scope; 401/403 tests |
| B04 | P0 | B03 | Flutter shell + tokens + adaptive nav; 3 breakpoint |
| B05 | P0 | B02,B03 | inventory catalog/balances; API + screen + tests |
| B06 | P0 | B05 | inventory request/approval/post; A1-A4,I1-I2 |
| B07 | P0 | B05 | asset serial/assignment; I3 |
| B08 | P0 | B03 | cashbox + cash request/journal; C1-C3 + DEMO label |
| B09 | P0 | B06,B08 | audit/outbox/activity + SSE; E1 |
| B10 | P1 | B09 | integration connection sandbox and secret refs; test connection |
| B11 | P1 | B10 | Sheets import preview/staging/mapping/dedupe; G1 |
| B12 | P1 | B10 | CSV/Excel staging + safe export; invalid file tests |
| B13 | P1 | B10 | Apps Script signed webhook/outbound; G2 |
| B14 | P1 | B06,B08 | reconciliation jobs + exception UI; R1 |
| B15 | P1 | all | cross-platform E2E, security review, backup restore, demo deploy |

## Giao việc cho AI coding agent

Chỉ giao một task ID mỗi lần. Cung cấp toàn bộ docs và yêu cầu agent nêu: files changed, migration, API diff, UI screenshots/test targets, test results, unresolved risks. Nếu cần đổi invariant/hợp đồng, cập nhật docs trước hoặc cùng PR và yêu cầu review.
