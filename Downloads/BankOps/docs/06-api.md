# 06 — REST contracts / OpenAPI outline

Base `/api/v1`; JSON UTF-8; UTC ISO-8601; UUID string; money decimal as string (`"150000.00"`), quantity decimal as string. FastAPI sinh `/openapi.json` từ schema được test snapshot. Auth Bearer token demo; cookie secure/httpOnly có thể chọn cho web theo threat model. Mọi write có `X-Request-ID`; create/submit/approve/post-like commands cần `Idempotency-Key` UUID, state transitions cần `If-Match: <version>`.

## Endpoints

| Method/path | Permission | Nội dung |
|---|---|---|
| POST `/auth/login`, POST `/auth/refresh`, POST `/auth/logout`, GET `/me` | public/session | phiên demo, roles, capabilities |
| POST `/auth/google` `{"id_token": "..."}` | public | Google Sign-In: verify ID token (aud = client ID), upsert user theo email (lần đầu = signup role `staff`), trả access/refresh JWT + `is_new_user`; lỗi theo envelope |
| GET `/inventory/items`, `/inventory/locations`, `/inventory/balances`, `/inventory/assets` | inventory.read | filter/paginate |
| POST `/inventory/requests`, GET `/inventory/requests/{id}` | inventory.write/read | draft |
| POST `/inventory/requests/{id}/submit` | owner | pending |
| POST `/inventory/requests/{id}/approve`, `/reject` | inventory.approve | decision; checker khác maker |
| GET `/inventory/movements` | inventory.read | ledger kho |
| GET `/cash-sim/boxes`, `/cash-sim/balances`, `/cash-sim/journals` | cash.read | luôn DEMO |
| POST `/cash-sim/requests`, GET `/cash-sim/requests/{id}` | cash.write/read | draft |
| POST `/cash-sim/requests/{id}/submit`, `/approve`, `/reject` | owner/cash.approve | state transition |
| GET `/approvals/inbox` | *.approve | pending, loại, maker |
| GET `/activity`, GET `/events/stream?cursor=` | session | feed + SSE |
| POST `/integrations/connections`, POST `/integrations/{id}/test` | integration.admin | sandbox connector |
| POST `/integrations/{id}/runs`, GET `/integration-runs/{id}`, GET `/integration-runs/{id}/staging` | integration.admin | job/preview |
| POST `/integration-runs/{id}/confirm` | integration.admin | tạo draft từ staging, không post |
| POST `/reconciliations`, GET `/reconciliations/{id}` | operator/auditor | chạy/xem |
| GET `/audit-events` | audit.read | lọc; không edit |
| GET `/health/live`, `/health/ready` | infra | readiness DB/worker tùy ngữ cảnh |

Pagination `limit` 1..100 + opaque `cursor`; filter có whitelist; sort ổn định `created_at,id`. Không trả raw secret, password hash hoặc raw exception.

## Request/response mẫu

```http
POST /api/v1/cash-sim/requests
Idempotency-Key: 59c39c65-b6a9-4b47-afb9-70404f4b6c64
Content-Type: application/json

{"cashbox_id":"11111111-1111-4111-8111-111111111111","kind":"payment","amount":"250000.00","reason":"Chi phí demo"}
```

```json
{"id":"22222222-2222-4222-8222-222222222222","status":"draft","version":1,"simulation":true,"currency":"VND"}
```

```json
{"error":{"code":"SELF_APPROVAL_FORBIDDEN","message":"Người tạo không được tự duyệt","request_id":"...","details":{}}}
```

Status: 200 read/transition, 201 create, 202 queued job, 400 validation syntax, 401 unauthenticated, 403 forbidden, 404 not found, 409 state/version/idempotency conflict, 422 schema validation, 429 rate limit, 503 dependency unavailable. `POST /approve` trả 200 với trạng thái `posted`, không trả 202 khi DB chưa post.

## SSE contract

`GET /events/stream?cursor=<last_event_id>`; `text/event-stream`, `id`, `event: job.updated|activity.created|heartbeat`, `data` JSON `{event_id, occurred_at, subject_id, status, processed, total, message_key}`. Server replay theo cursor trong cửa sổ retention, client dùng `Last-Event-ID` khi reconnect rồi GET REST để đồng bộ. Không gửi token trong URL; xác thực header/cookie. Nếu cursor quá cũ, gửi `resync_required` và client tải lại REST.
