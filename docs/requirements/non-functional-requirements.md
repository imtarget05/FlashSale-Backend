# Non-Functional Requirements — Commerce + OpsBridge (OpsBridge bounded context)

> Mã định dạng `NFR-Cxx`. Các giá trị mục tiêu là **giả lập để thiết kế**, chưa phải cam kết vận hành production. Chỉ đánh dấu `VERIFIED` khi có restore drill / test thực.

| ID | Yêu cầu | Mục tiêu chấp nhận | Cơ sở chứng minh | Trạng thái |
|---|---|---|---|---|
| NFR-C01 | Không overselling (inventory conservation) | 0 vi phạm trong concurrency test | `oversell_demo.py` stock 10, 50 requests → 3 accepted / 47 rejected, DB stock 7 = 10−3 | PASS (F2 concurrency.txt) |
| NFR-C02 | Checkout độc lập với LLM (AI unavailable không chặn reservation) | Hoạt động khi AI provider down | (thuộc FlashSale checkout path, ngoài OpsBridge) | N/A cho OpsBridge |
| NFR-C03 | Khôi phục đơn hàng và outbox | RTO ≤ 2 giờ; RPO mục tiêu ≤ 15 phút | `docs/business/recovery-targets.md` (manual backup, RPO chưa guarantee); chưa có restore drill | Reference only / GAP drill |
| NFR-C04 | Checkout latency p95 < 500ms dưới workload benchmark | p95 < 500ms | `docs/benchmarks/` (chú ý: benchmark doc không phải test file, NOT A GATE) | UNVERIFIED (không phải gate) |
| NFR-C05 | Authorization: 0 successful unauthorized operations | Mọi route staff deny-by-default | `OpsBridgeAuthorizationGuardTests`, `OpsBridgeAuthBoundaryTests` | PASS (F2) |
| NFR-C06 | Idempotency: 0 duplicate business records sau retry | Replay idempotent, consumer dedupe eventId | `FailureDrill6`, `FailureDrill5`, `OutboxPoisonTests` | PASS (F2) |
| NFR-C07 | AI failure isolation (OCR/LLM lỗi không làm sai dữ liệu đã duyệt) | AI lỗi không chặn core business | (thuộc BankOps/AI; OpsBridge AI chỉ đề xuất, không tự kết luận) | N/A cho OpsBridge core |

## Ghi chú về RTO/RPO

Theo `docs/business/recovery-targets.md`: RPO ≤ 24h (một logical backup/ngày là tối thiểu), RTO ≤ 2 giờ. Hiện tại backup là **manual** (scripts/backup/*.sh), achievable RPO = thời gian từ lần backup cuối → **chưa đảm bảo**. Restore drill chưa được chạy → chưa đánh dấu VERIFIED.
