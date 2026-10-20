# Business Context — Commerce + OpsBridge (OpsBridge bounded context)

> **Simulated customer (fictional):** VietRetail Distribution — 4 kho, ~20.000 SKU, chạy các chương trình FlashSale với số lượng hàng giới hạn. Đây là **bản thiết kế giả lập** dùng để minh họa requirements-driven engineering, không phải thông tin của khách hàng thật và không phải cam kết vận hành thực tế.

## 1. Khách hàng và phạm vi

| Mục chi tiết | Nội dung |
|---|---|
| Khách hàng giả lập | VietRetail Distribution |
| Lĩnh vực | Thương mại và phân phối |
| Phạm vi kỹ thuật | Bounded context **OpsBridge**: ingestion sự kiện từ FactoryFlow, phiếu nhập kho (GoodsReceipt), hóa đơn nhà cung cấp (SupplierInvoice), đối soát 3-way (PO–GR–Invoice), quản lý ngoại lệ đối soát, replay dead-letter event, phân quyền nhân viên vận hành |
| Phạm vi TRONG OpsBridge | FactoryFlow webhook ingestion, durable inbox/outbox, transactional outbox → RabbitMQ, DLQ + replay có điều kiện, reconciliation engine, review discrepancy, audit trail |
| Phạm vi NGOẠI OpsBridge (thuộc FlashSale / Order API) | Đơn hàng cuối khách (Order), reservation tồn kho, thanh toán, lifecycle đơn hàng, checkout latency, no-oversell tại thời điểm đặt hàng |

## 2. Vai trò và persona

| Persona | Vai trò trong hệ thống | Quyền OpsBridge (claim) |
|---|---|---|
| Operations Manager | Quan hệ tổng thể fulfillment, ngoại lệ đơn hàng, trạng thái khắc phục | `Admin` |
| Kho staff (Operator) | Nhập phiếu nhập, chạy đối soát, yêu cầu replay DLQ | `Operator` |
| Kế toán (FinanceReviewer) | Phê duyệt / từ chối sai lệch đối soát (variance) | `FinanceReviewer` |
| Kiểm soát viên (Auditor) | Xem chỉ đọc reconciliation, audit trail, DLQ | `Auditor` |
| Hệ thống FactoryFlow (ngoại bộ) | Gửi webhook `GoodsReceiptConfirmed v1` | Không có bearer token; bảo vệ bằng HMAC/Svix signature |

## 3. Vấn đề kinh doanh (pain points)

- FlashSale tạo(order) quá tải: nhiều yêu cầu đặt hàng đồng thời trên cùng một SKU tồn kho hạn chế → rủi ro **overselling**.
- Fulfillment có đơn bị "kẹt": sự kiện từ FactoryFlow không được xử lý, outbox message bị treo, không có cơ chế phát hiện tự động.
- Đối soát 3-way (PO–GR–Invoice) thủ công, chậm, dễ sai số tiền/số lượng, thiếu audit trail của mỗi quyết định phê duyệt.
- Replay dead-letter message theo cách thủ công không kiểm soát: cùng sự kiện được nhập hai lần → double effect (giao hàng dopp, thanh toán dopp).
- Không có phân quyền rõ ràng: nhân viên không phù hợp có thể phê duyệt sai lệch tài chính.

## 4. Mục tiêu kinh doanh (BG) và KPI nghiệm thu đề xuất

| ID | Mục tiêu | KPI đề xuất (giả lập, chưa đo trên production) |
|---|---|---|
| BG-C01 | Tránh bán vượt tồn kho | 0 vi phạm overselling trong concurrency tests |
| BG-C02 | Giảm đơn hàng bị kẹt không được phát hiện | 100% injected stuck-order cases được phát hiện trong thời hạn test |
| BG-C03 | Giảm thời gian xử lý ngoại lệ | Giảm ≥30% so với quy trình thủ công mô phỏng |
| BG-C04 | Tăng độ tin cậy messaging | 0 duplicate business effects khi redelivery |

## 5. Ràng buộc và giả định

- Ba doanh nghiệp (BankOps, Azure SRE, Commerce+OpsBridge) **không dùng chung database, authentication tenant, secrets hay policy engine instance**. Điểm chung chỉ là engineering standards, reusable templates và observability conventions.
- OpsBridge là **modular monolith**, không tách microservices ngay ban đầu (ADR-017, ADR-010).
- Không claim exactly-once message processing; chỉ claim **at-least-once** với idempotency key và conditional-update (ADR-015, ADR-017).
- Production tokens phải do identity hệ thống distinctly cấp; OpsBridge không tự implement credential verification hay external IdP (ADR-017).
