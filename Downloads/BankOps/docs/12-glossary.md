# 12 — Từ điển và quy ước

| Thuật ngữ | Nghĩa |
|---|---|
| Maker | người tạo/submit yêu cầu |
| Checker | người duyệt, phải khác maker |
| Posted | nghiệp vụ đã ghi movement/journal bất biến |
| Stock movement | thay đổi lượng vật tư tại location, có dấu +/- |
| Asset | tài sản có serial và lịch sử vị trí/trách nhiệm |
| Cash simulation | sổ quỹ giả lập độc lập với ngân hàng thật |
| Journal/entry | bút toán và dòng debit/credit của quỹ demo |
| Balance | read model cập nhật giao dịch, phải đối soát với ledger |
| Staging | vùng dữ liệu ngoài chờ validate và xác nhận |
| Outbox | event ghi cùng transaction để worker phát sau commit |
| Activity | thông báo tiến trình/hành động, không là nguồn sự thật |
| Reconciliation | so nguồn ledger/movements với balance/read model |

API và DB dùng tiếng Anh ASCII, UI tiếng Việt. Timestamp UTC ở server, render theo múi giờ người dùng. `VND` là đơn vị mô phỏng; mọi màn/CSV liên quan tiền ghi rõ DEMO. UUID trong sample chỉ là ví dụ.
