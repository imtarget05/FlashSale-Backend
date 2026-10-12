# 02 — Roadmap và vertical slices

Mỗi slice gồm migration + endpoint + Flutter screen + test + seed. Chỉ chuyển slice tiếp khi acceptance đạt. Ước lượng là thứ tự, không phải cam kết thời gian.

| Slice | Kết quả chạy được | Cổng nghiệm thu |
|---|---|---|
| S0 Foundation | repo, CI, Docker, config, migration, demo login, shell Flutter, design tokens | boot một lệnh; health + login; không secret trong repo |
| S1 Kho vật tư | SKU/location, receipt/issue draft-submit-approve-post, tồn | maker ≠ checker; không âm; retry không nhân đôi |
| S2 Tài sản | asset serial, assign/return/transfer, lịch sử | serial duy nhất; vị trí/trách nhiệm nhất quán |
| S3 Quỹ mô phỏng | cashbox, receipt/payment, journal, balance | cân bút toán; không chi âm; nhãn DEMO mọi màn |
| S4 Activity | outbox, worker, SSE, progress/activity center | reconnect không mất sự kiện; terminal state đúng |
| S5 Tích hợp | Sheets sandbox + CSV/Excel staging; webhook/Apps Script adapter | preview, dedupe, mapping, xác nhận; không auto-post |
| S6 Đối soát & hardening | reconciliation, audit export, QA đa nền tảng, deploy demo | phát hiện lệch; restore DB thử; accessibility cơ bản |

## Sau MVP

SSO/MDM nếu tổ chức phê duyệt; quản lý workflow có cấu hình; scan barcode; push notification; offline read; báo cáo nâng cao. Không thêm kết nối tiền thật từ roadmap này.

## Quy tắc chia PR

PR nhỏ theo vertical slice; contract và migration đi cùng code. Không merge khi migration chưa rollback/test được, hoặc hành vi phê duyệt/idempotency chưa có test cạnh tranh.
