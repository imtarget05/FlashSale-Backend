# State Machines (OpsBridge)

## 1. Dead-letter event lifecycle (replay)

```text
NEW (IsReplayed=false)
   --[operator replay, conditional UPDATE WHERE !IsReplayed]-->
   REPLAYED (IsReplayed=true, ReplayedAt, ReplayedBy)
      --> chèn OutboxMessage (Status=Pending)

Cùng eventId replay lại sau khi đã REPLAYED:
   --> trả AlreadyReplayed (không tạo OutboxMessage mới)

Event không tồn tại:
   --> trả NotFound
```

**Ranh giới:** Chỉ `Operator`/`Admin` được gọi `POST /api/v1/events/replay/{eventId}`. Replay có điều kiện (conditional update) đảm bảo idempotency dưới tải đồng thời (FailureDrill6: 12 request đồng thời → 1 `ReplayQueued`, 11 `AlreadyReplayed`, đúng 1 `OutboxMessage`).

## 2. Discrepancy lifecycle (đối soát)

```text
UNDER_REVIEW
   --[FinanceReviewer/Admin: ApprovedWithTolerance hoặc Adjusted, tất cả discrepancy trên kết quả đều Resolved]-->
   RESOLVED --> commit allocation đến GoodsReceiptLine; invoice = ApprovedWithVariance
   --[FinanceReviewer/Admin: Rejected]-->
   REJECTED --> release allocation; invoice = Rejected
   --[Auditor gọi review endpoint]-->
   403 Forbidden (không thay đổi trạng thái)

Invoice đã Matched hoặc ApprovedWithVariance:
   --> không được chạy lại đối soát (guarded)
```

**Ranhiverse:** Chỉ `Admin`/`FinanceReviewer` được approve/reject discrepancy (`Review` policy). `Operator` có quyền trong use-case code nhưng route-level `Review` policy gắn Admin+FinanceReviewer (ADR-017 đã điều chỉnh: FinanceReviewer thêm vào read policy để tránh 403 do AND semantics).

## 3. Reconciliation match status

```text
Chạy đối soát (ReconcileInvoiceUseCase)
   --> MATCHED (không sai lệch): invoice = Matched, allocate hàng
   --> DISCREPANCY (có sai lệch): invoice = Discrepancy, tạo Discrepancy records
      --> cần ReviewDecision mới chuyển sang RESOLVED/REJECTED
```

## 4. Outbox message lifecycle

```text
PENDING (chèn trong cùng txn nghiệp vụ)
   --[OutboxPublisherHostedService, RabbitMQ Publisher Confirms]-->
   PROCESSED (ProcessedAt được ghi)
   --[publisher crash trước khi confirm]-->
   vẫn PENDING → được xuất lại lần tới (at-least-once, consumer dedupe qua Inbox)
```

## 5. Webhook ingestion outcome

```
Hợp lệ (HMAC OK, timestamp trong cửa sổ, contract hợp lệ)
   --> 202 Accepted; inbox + outbox commit trong 1 txn
Trùng eventId (đã xử lý)
   --> 200 OK Duplicate; không effect hai lần
Sai chữ ký / hết thời gian / sai contract
   --> 401 / 400 / 409 (typed domain exception, không fake 202)
Lỗi persistence
   --> không trả 2xx; giao dịch rollback
```
