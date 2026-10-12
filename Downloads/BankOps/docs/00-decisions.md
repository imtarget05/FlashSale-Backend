# 00 — Quyết định kiến trúc và ranh giới

| Mục | Quyết định |
|---|---|
| UI | Một Flutter monorepo app, giao diện adaptive theo kích thước màn hình; Android/iOS là ưu tiên MVP, web/desktop hỗ trợ cùng luồng nghiệp vụ |
| Backend | FastAPI modular monolith, REST JSON, SSE cho tiến trình; worker riêng dùng cùng codebase |
| Database | PostgreSQL, UUID, UTC timestamp, numeric/Decimal; Alembic migrations |
| Quỹ | Chỉ `cash_sim`, tiền mô phỏng VND, không API ngân hàng hoặc payment rail |
| Kho | `inventory` quản lý vật tư có lượng và tài sản có mã định danh; không ghi vào ledger quỹ |
| Giao dịch | maker/checker khác người; bất biến sau khi posted, sửa bằng reversal/adjustment mới |
| Tích hợp | Chỉ bản sao dữ liệu mô phỏng; xuất/nhập qua staging + xác nhận, không ghi trực tiếp ledger |
| Realtime | SSE cho trạng thái job và activity; REST làm nguồn dữ liệu chuẩn |
| Quyền | quyền trên server là nguồn chuẩn, Flutter chỉ ẩn/hiện khả năng tương ứng |

## Invariants

1. `created_by != approved_by` với nghiệp vụ cần duyệt. Người tạo không thể tự duyệt, kể cả admin.
2. Một yêu cầu có đúng một kết quả posted hoặc rejected; retry không tạo bản ghi ledger thứ hai.
3. Ledger quỹ dùng bút toán đối ứng: tổng debit = tổng credit trên từng journal; balance không âm trừ khi policy mô phỏng cho phép rõ ràng (MVP: không cho phép).
4. Tồn kho chỉ đổi qua stock movement đã posted; tài sản serial là duy nhất trong trạng thái active.
5. Mỗi event và audit record có actor, thời điểm UTC, request/correlation ID; bản ghi audit append-only.
6. Báo cáo luôn nói rõ `DEMO / SIMULATED`; không dùng tên thương hiệu ngân hàng thật.

## Ngoài phạm vi MVP

Core banking, tài khoản khách hàng, thanh toán thật, dữ liệu PII thật, accounting general ledger, SSO doanh nghiệp, offline write, AI quyết định phê duyệt, OCR chứng từ thật, tự động ghi số dư từ tích hợp.

## Điều cần xác nhận trước production

Chính sách xác thực/MDM, lưu trữ dữ liệu, phân quyền tổ chức, quy trình tuân thủ và kết nối được phê duyệt. Mặc định chỉ triển khai môi trường demo riêng.
