# PRD — Commerce + OpsBridge (VietRetail Distribution)

## 1. User stories

| ID | User story | Persona | Ưu tiên |
|---|---|---|---|
| US-C-01 | As a warehouse operator, I want to receive FactoryFlow `GoodsReceiptConfirmed` events and have them durably stored so that procurement snapshots are never lost. | Operator | Must |
| US-C-02 | As an operations manager, I want 3-way reconciliation (PO–GR–Invoice) to run automatically so that mismatches are surfaced as discrepancies. | Admin | Must |
| US-C-03 | As a finance reviewer, I want to approve or reject a discrepancy with a reason so that approved variances allocate inventory and rejected ones release it. | FinanceReviewer | Must |
| US-C-04 | As an operator, I want to replay a dead-letter event exactly once so that failed fulfillment events can be recovered without double effect. | Operator | Must |
| US-C-05 | As an auditor, I want to view reconciliation results, audit trail, and DLQ events (read-only) so that I can verify compliance. | Auditor | Must |
| US-C-06 | As a customer-facing system, I want checkout to keep working even when the AI exception assistant is unavailable. | Customer (FlashSale) | Should |
| US-C-07 | As an operations manager, I want to detect stuck orders (fulfillment not progressing) within a time limit. | Admin | Must |

## 2. Workflow chính

1. FactoryFlow gửi webhook `GoodsReceiptConfirmed v1` (HMAC/Svix signed) → `POST /api/v1/webhooks/factoryflow` → lưu inbox + outbox trong 1 transaction PostgreSQL → 202 Accepted (thành công ngay cả khi brokerDown).
2. OutboxPublisher đọc outbox → xuất RabbitMQ (publisher confirms).
3. `GoodsReceiptConsumerHost` xử lý message (manual ACK, retry 3 lần, poison → DLQ).
4. Nếu message lặp lại (cùng eventId) → inbox dedupe → không xử lý hai lần.
5. Reconciliation engine chạy 3-way matching PO–GR–Invoice → tạo discrepancy nếu sai lệch.
6. FinanceReviewer phê duyệt/từ chối discrepancy → cập nhật trạng thái invoice, allocate/release hàng, ghi audit.
7. DLQ event bị lỗi → operator replay qua `POST /api/v1/events/replay/{id}` → conditional update (chỉ 1 request thành công, các lần sau trả AlreadyReplayed) → chèn outbox message mới.

## 3. Giao diện chính (API OpsBridge)

| Endpoint | Phương án | Phạm vi |
|---|---|---|
| `POST /api/v1/webhooks/factoryflow` | Public (HMAC/Svix) | Ingestion sự kiện nhập kho |
| `POST /api/v1/purchase-orders` | OperatorOrAdmin | Tạo PO |
| `POST /api/v1/goods-receipts` | OperatorOrAdmin | Tạo phiếu nhập |
| `POST /api/v1/invoices` | OperatorOrAdmin | Tạo hóa đơn |
| `POST /api/v1/reconciliation/run` | OperatorOrAdmin | Chạy đối soát 3-way |
| `POST /api/v1/reconciliation/discrepancies/{id}/review` | Admin, FinanceReviewer | Phê duyệt/từ chối sai lệch |
| `POST /api/v1/events/replay/{eventId}` | OperatorOrAdmin | Replay DLQ (idempotent) |
| `GET /api/v1/events/dead-letter` | AuditorOrAbove | Xem DLQ |
| `GET /healthz` | Public | Liveness probe |
| `POST /api/v1/auth/login` | Chỉ Dev/Test | Demo persona login (không Production) |
