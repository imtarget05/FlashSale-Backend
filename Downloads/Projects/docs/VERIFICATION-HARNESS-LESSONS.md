# VERIFICATION HARNESS LESSONS

Hệ thống phát hiện lỗi trong chính công cụ kiểm chứng của nó mạnh hơn một
dự án có lịch sử test hoàn hảo một cách giả tạo. Mỗi mục dưới đây đều
là lỗi thật đã xảy ra, đã phát hiện, đã sửa, và đã có regression check.

## H1 — Oracle sai, hệ thống đúng (RTO/RPO)

| | |
|---|---|
| **Giả định sai** | Restore về đúng trạng thái ngay trước khi hỏng |
| **Chế độ lỗi** | Báo hỏng khi hệ thống hoạt động đúng |
| **Phát hiện** | Restore trả `sold=2 stock=7` khớp thời điểm backup, nhưng probe đòi `stock=10` |
| **Oracle đúng** | RPO = lượng dữ liệu mất so với recovery point. Test chuẩn: 3 units ghi sau backup phải mất đúng 3 |
| **Regression** | `tools/e2e/gate2_rto_rpo.sh` đọc expected state từ DB ngay trước backup |

Lần thứ hai tôi so restore với trạng thái trước khi hỏng → MISMATCH dù restore
đúng, vì backup có nghĩa là đưa về **thời điểm backup**, và chênh lệch đó
chính là RPO. Hai lỗi này cùng hướng: dễ dẫn tới "sửa dữ liệu cho test xanh".

## H2 — Chỉ số không bao giờ báo động

`lost_orders_before_restore: 0` — bộ đếm chạy **sau** lệnh DELETE. Con số
luôn 0 và trông như một phát hiện tốt. Chỉ số không thể báo động còn tệ hơn
không có chỉ số.

## H3 — Probe hỏng trông như ứng dụng hỏng

`get("/predict")` không body → `httpx` ném exception → probe crash → dễ kết
luận "app hỏng". Và `EventProducer.close()` không tồn tại → `AttributeError`.

## H4 — Env rỗng tạo bằng chứng rỗng

Một lần chạy để lại giá trị lỗi trong biến môi trường → Azure không tạo
revision mới → probe vẫn báo "chạy thành công" với artifact rỗng.

## H5 — Poison topic dùng chung với control

Control đặt 1 message, poison đặt 2 message, cùng topic. Consumer mới đọc
`earliest`, thấy message cũ trước, `max_messages=1` → thoát trước khi tới
poison. Trông như "bounded retry không hoạt động" — nhưng vấn đề là thiết kế
scenario, không phải hệ thống. Sửa: topic riêng cho poison.

## H6 — `seek()` bị đổ tội oan

Giả thuyết đầu tiên: `seek()` không hoạt động nên retry không giao lại. Viết
script độc lập quan sát → `seek()` hoạt động hoàn hảo, 6 lần liên tiếp cùng
offset. Nguyên nhân thật là consumer đọc từ offset 1 vì `poll()` trong lúc
chờ rebalance đã tiêu thụ message đầu tiên.

Bài học: **đo trước khi sửa**. Sửa `seek()` theo giả thuyết sẽ che mất bug
thật nằm ở `__init__`.

## H7 — META-GATE bắt được chính probe

`return ok` bị mất khi cắt file → `meta_gate()` trả `None` → probe tự báo
`INVALID_EVIDENCE` thay vì chạy tiếp. Đây là hành vi đúng của thiết kế
verify-the-verifier.

## H8 — Test encode bug

4 test fail sau khi sửa `is_duplicate`. Xem lại: test giả định `is_duplicate`
vừa kiểm tra vừa đánh dấu — chính hành vi gây mất dữ liệu. Test bảo vệ bug.

Đây là ranh giới tinh tế: "không sửa test cho xanh" không có nghĩa "không bao
giờ sửa test". Nghĩa là **phải phân loại** test sai trước khi đổi nó. Ở đây
đã ghi lại lý do trong docstring của test thay vì xoá assertion.

## Nguyên tắc rút ra

> Test/probe không phải truth nến chính oracle của nó chưa được sanity-check.
> Expected state phải đến từ **observed baseline**, không phải hằng số tự đặt.
> Khi hệ thống hỏng, hãy hỏi trước: *oracle có đúng không?*
