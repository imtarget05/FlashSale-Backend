# 07 — Flutter design system, responsive, motion

## Nguyên tắc

Giao diện tiếng Việt mặc định, sẵn cấu trúc i18n. Mọi màn quỹ hiển thị nhãn **DEMO — QUỸ MÔ PHỎNG**; màu sắc không là tín hiệu duy nhất. Mobile tập trung thao tác nhanh; desktop/web có bảng và filter đầy đủ. Accessibility: semantic labels, text scale, focus visible, keyboard navigation, contrast hợp lý, reduced motion.

## Design tokens đề xuất

Material 3 làm nền; primary navy `#17365D`, accent teal `#007F86`, success `#207A50`, warning `#9A6200`, danger `#B42318`, surface `#F6F8FA`. Spacing 4/8/12/16/24/32; radius 8/12/16; typography 12/14/16/20/28. Token trong `ThemeExtension`, light/dark; kiểm tra tương phản và không hardcode mỗi widget.

## Breakpoints và shell

| Width | Navigation | Layout |
|---|---|---|
| <600 | bottom nav 4 mục + More, FAB theo màn | một cột, card, filter sheet |
| 600–1199 | navigation rail | master/detail khi đủ chỗ |
| >=1200 | sidebar + topbar | dashboard grid, bảng, detail side panel |

Các mục: Tổng quan, Kho, Quỹ demo, Phê duyệt, Tích hợp, Hoạt động, Báo cáo, Cài đặt. Capability quyết định mục hiển thị, server vẫn kiểm quyền.

## Màn chính

- Dashboard: thẻ số việc chờ, tồn cảnh báo, quỹ demo, job đang chạy; mỗi thẻ mở danh sách tương ứng.
- Kho: tab Vật tư/Tài sản; search, location filter, tồn; chi tiết có movement timeline.
- Quỹ demo: số dư, danh sách phiếu, nhật ký bút toán cân; banner DEMO cố định.
- Tạo phiếu: stepper 2–3 bước, autosave draft, validation tại field, review trước submit.
- Approval inbox: loại, maker, tác động, chi tiết trước nút duyệt; xác nhận có hành động rõ.
- Integration hub: card connector, trạng thái, test, mapping preview, lịch sử run và staging errors.
- Activity center: nhóm Today/Earlier, live status, retry action khi hợp lệ, link đến đối tượng.

## Progress và animation

State chung `idle → validating → queued → running → succeeded|failed|cancelled`; job có `processed/total` thì determinate, không có tổng thì indeterminate. Hiện tên tác vụ, bước hiện tại, elapsed, trạng thái cuối và lỗi có cách khắc phục. Không hiển thị phần trăm giả. Event SSE chỉ cập nhật view model, REST reconcile khi reconnect. Transition 150–250 ms, skeleton khi tải, subtle success confirmation; tắt/giảm motion theo hệ điều hành. Không dùng animation che nút hoặc làm trễ nghiệp vụ.

## State management

Feature modules với repository typed, DTO/codegen từ OpenAPI, state immutable; tách server state và form draft. Không dựa vào optimistic update để xác nhận posted; sau 409 hiển thị dữ liệu mới và cho tải lại. Client không tự tính balance cuối cùng.
