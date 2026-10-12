# 01 — PRD và phạm vi

## Mục tiêu

Nhân viên theo dõi kho vật tư/tài sản và quỹ mô phỏng trên điện thoại, web, desktop; tạo yêu cầu, duyệt hai người, xem tiến trình đồng bộ và lịch sử hoạt động. Demo phải dễ thao tác khi di chuyển nhưng không cho phép giao dịch tiền thật.

## Vai trò và nhu cầu

| Vai trò | Nhu cầu |
|---|---|
| Staff (maker) | xem tồn, tạo đề nghị nhập/xuất/chuyển vật tư; tạo phiếu thu/chi quỹ mô phỏng; xem trạng thái |
| Approver (checker) | xem hàng chờ, kiểm tra chi tiết, duyệt/từ chối với lý do; không duyệt phiếu mình tạo |
| Inventory manager | quản lý danh mục vật tư/tài sản và kiểm kê/đối soát kho |
| Cash simulation operator | đối soát ledger quỹ mô phỏng, xem sai lệch, tạo đề nghị điều chỉnh |
| Integration admin | thiết lập connector sandbox, mapping, lịch, chạy thử và xem lỗi |
| Auditor | xem audit, báo cáo, export dữ liệu demo ở chế độ chỉ đọc |
| System admin | quản lý người dùng demo và cấu hình; không vượt maker/checker invariant |

## Luồng chính

1. Đăng nhập demo → dashboard theo quyền → activity center.
2. Tạo phiếu kho → lưu draft → submit → người khác duyệt → post stock movement → tồn cập nhật.
3. Tạo phiếu thu/chi quỹ mô phỏng → submit → người khác duyệt → post balanced journal → báo cáo quỹ cập nhật.
4. Kết nối Sheets sandbox → test kết nối → preview import → xác nhận mapping → chạy job → theo dõi progress → kết quả staging; chỉ tạo draft sau xác nhận thủ công.
5. Đối soát: snapshot dự kiến từ ledger/movements so với read model; sai lệch tạo exception để xử lý, không tự sửa.

## Yêu cầu chức năng MVP

- Danh mục location, vật tư SKU, tài sản serial; tạo phiếu kho, phê duyệt, sổ movement, tồn theo location.
- Quỹ mô phỏng: cashbox, phiếu thu/chi, approval, journal kép, số dư và đối soát.
- Dashboard, search/filter, activity feed, job progress, thông báo lỗi có thể thử lại.
- RBAC, audit append-only, export CSV demo, idempotency cho POST/approve.
- Integration hub: Google Sheets import/export dữ liệu demo, Apps Script/webhook sandbox, Excel/CSV upload qua staging.

## Chất lượng và giới hạn

- Responsive tại 360, 768, 1280 px; thao tác chính bằng bàn phím trên desktop/web.
- API p95 mục tiêu <500 ms cho đọc thông thường trên dữ liệu demo; job dài chạy nền.
- Mỗi thay đổi có trace ID; job có trạng thái và phần trăm chỉ khi biết tổng công việc.
- Không đưa secret ra client/log, không cache dữ liệu nhạy cảm tùy tiện, không nhúng token connector vào app.
- Mốc tải thử nghiệm: 100 người dùng đồng thời, 100k movement, 100k journal; đo và điều chỉnh trước triển khai rộng.

## Chỉ số chấp nhận

100% nghiệp vụ thay đổi tồn/số dư được duyệt khác người và tạo đúng một movement/journal; retry không nhân đôi; đối soát xác định được mọi sai lệch cố ý trong test; các luồng chính chạy trên Android, iOS simulator, web và một desktop target.
