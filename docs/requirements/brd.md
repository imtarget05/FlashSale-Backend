# BRD — Commerce + OpsBridge (VietRetail Distribution)

> **Tính chất:** Business Requirement Document giả lập cho bounded context OpsBridge. Mục tiêu là minh họa requirements-driven engineering theo Phần E, không phải BRD của doanh nghiệp thật.

## 1. Bối cảnh doanh nghiệp

VietRetail Distribution vận hành 4 kho và khoảng 20.000 SKU. Các chương trình FlashSale gây ra đợt truy cập劇烈 trên số lượng hàng giới hạn. Order Service (FlashSale) nhận đơn hàng và reservation tồn kho; OpsBridge phụ trách fulfillment, ngoại lệ và đối soát sau khi đơn hàng được tạo.

## 2. Mục tiêu kinh doanh và KPI

| ID | Mục tiêu | KPI nghiệm thu đề xuất | Trạng thái chứng minh |
|---|---|---|---|
| BG-C01 | Tránh bán vượt tồn kho | 0 overselling trong concurrency tests | Đã chứng minh qua harness local (F2: 3 accepted / 47 rejected, stock 10→7, 0 transport failure) |
| BG-C02 | Giảm đơn hàng bị kẹt không được phát hiện | 100% injected stuck-order cases được phát hiện trong thời hạn test | Chưa có test injected stuck-order → GAP |
| BG-C03 | Giảm thời gian xử lý ngoại lệ | Giảm ≥30% so với quy trình thủ công mô phỏng | Chưa đo → GAP |
| BG-C04 | Tăng độ tin cậy messaging | 0 duplicate business effects khi redelivery | Đã chứng minh qua replay idempotency (F2 FailureDrill6: 12 concurrent → 1 Queued + 11 AlreadyReplayed, 1 outbox) |

## 3. Phạm vi trong BRD này

**Trong phạm vi (OpsBridge):**
- FR-C01..FR-C04, FR-C06 (xem functional-requirements.md)
- NFR-C01, NFR-C03, NFR-C04 (xem non-functional-requirements.md)

**Ngoài phạm vi BRD này (thuộc FlashSale / Order API, đã có docs riêng):**
- Checkout latency p95, no-oversell tại thời điểm đặt hàng (xem docs/requirements/requirements.md FlashSale)
- AI exception assistant (FR-C05) — Could, chưa implement
- Dự báo nhu cầu theo SKU (FR-C07) — Could, chưa implement

## 4. Ràng buộc

- Không deploy LiteLLM Gateway lên production trước khi kiểm chứng bảo mật (CVE-2026-35029 / CVE-2026-59823, bản vá 1.83.9, SSRF còn là lo ngại).
- lakeFS ≥1.87 chuyển BSL-1.1, không coi là open-source tương đương DVC (Apache-2.0).
- OpsBridge không trực tiếp ghi bảng FlashSale (ADR-017).
