# 08 — Authentication, RBAC, approval, audit, security

## Identity và phiên

MVP dùng tài khoản demo riêng; password hash bằng thuật toán hiện hành được thư viện uy tín hỗ trợ, cấu hình cost, rate limit login, khóa tạm khi brute force. Access token ngắn hạn, refresh rotation và revoke/logout; token trong secure storage trên mobile/desktop, web dùng cơ chế cookie an toàn nếu phù hợp triển khai. Bật TLS ngoài local. Không đưa token vào log, URL hoặc crash report. Khi đi production, thiết kế lại theo IdP/SSO tổ chức.

## RBAC

Capabilities `inventory.read/write/approve`, `assets.manage`, `cash.read/write/approve`, `integrations.admin`, `reconciliation.run/read`, `audit.read`, `users.manage`. Scope location/cashbox áp dụng trên server, deny by default; danh sách/API object-level authorization. `admin` không miễn quy tắc maker/checker. Bảng role-capability seed demo: staff(read/write), approver(read/approve), operator(read/reconciliation), integration_admin(integrations), auditor(read/audit), admin(users + config). User có thể nhiều role nhưng không tự duyệt.

## Approval & audit

Server xác minh khác người dựa user ID ổn định, không dựa role hay tên. Decision ghi người, thời điểm, lý do; reject bắt buộc lý do; approve lưu snapshot hash của yêu cầu đã duyệt. Audit append-only cho login, CRUD draft, submit, approve/reject, posting, connector config, export, reconciliation. Cấp quyền đọc audit riêng, hạn chế export. Giữ request/correlation ID xuyên API-job.

## Input và integration security

Schema validation, giới hạn body/file size, MIME + parser validation; file Excel/CSV xử lý như dữ liệu không tin cậy, chặn công thức khi xuất CSV, không thực thi macro. Webhook verify signature HMAC, timestamp/replay window, secret rotation, allowlist endpoint/outbound network khi triển khai; Google OAuth/service credential chỉ ở backend secret store, scope tối thiểu, chỉ sandbox. Apps Script URL phải allowlist; không nhận code script từ client để chạy. Rate limit writes và webhook.

## Dữ liệu và vận hành

Seed dùng danh tính giả; cấm customer PII thật. Mã hóa lưu trữ qua hạ tầng, backup có hạn lưu, quyền DB tối thiểu, dependency scan, security headers/CORS cụ thể, CSRF nếu cookie auth. Không ghi raw spreadsheet rows vào log; staging có TTL. Môi trường production demo hiển thị watermark và chặn mọi connector không nằm trong danh sách sandbox. Kiểm tra threat model trước mở rộng.
