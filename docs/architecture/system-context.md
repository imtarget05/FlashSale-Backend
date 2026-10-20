# C4 Level 1 — System Context (OpsBridge bounded context)

> Grounded in actual implementation under `src/OpsBridge.{Api,Application,Domain,Infrastructure}`. Complementary to `docs/C4.md` (portfolio-level sketch); this file scopes the OpsBridge boundary per ADR-017.

## Các actor

| Actor | Mô tả |
|---|---|
| Kho staff (Operator) | Tạo PO/GR, chạy đối soát, yêu cầu replay DLQ |
| Kế toán (FinanceReviewer) | Phê duyệt / từ chối sai lệch đối soát |
| Kiểm soát viên (Auditor) | Xem chỉ đọc reconciliation, audit trail, DLQ |
| Quản lý vận hành (Admin) | Toàn quyền OpsBridge staff |
| Hệ thống FactoryFlow (ngoại bộ) | Gửi webhook `GoodsReceiptConfirmed v1` (HMAC/Svix) |
| Khách hàng cuối (FlashSale) | Đặt hàng (thuộc Order API, ngoài ranh giới OpsBridge) |

## Hệ thống bên ngoài và ranh giới tin cậy (trust boundary)

| Hệ thống | Mối quan hệ | Ranh giới |
|---|---|---|
| FactoryFlow | Gửi sự kiện nhập kho qua webhook | API edge; bảo vệ bằng HMAC/Svix signature, không bearer token |
| FlashSale (Order API) | Cùng repository solutions; OpsBridge KHÔNG trực tiếp ghi bảng FlashSale | Ran ranh ngữ cảnh (ADR-017); giao thông qua versioned contract / owner-controlled command |
| PostgreSQL (OpsBridgeDb) | Nguồn verità nội bộ OpsBridge | Chỉ truy cập qua `OpsBridgeDbContext` trong Infrastructure; API endpoint không trực tiếp DbContext (FR-C11, `ApiEndpoints_MustNotDependOnInfrastructureDbContext`) |
| RabbitMQ | Broker sự kiện outbox → consumer | Outbox publisher (Publisher Confirms), DLX/DLQ cho poison message |
| React dashboard (`src/OpsBridge.Web`) | Giao diện vận hành | Gọi API OpsBridge qua JWT bearer |

## Ranh giới phân quyền (ADR-017)

- `CUSTOMER` là role của FlashSale customer API, **không phải** role OpsBridge staff.
- OpsBridge chấp nhận claim `Operator`, `Admin`, `Auditor`, `FinanceReviewer` từ JWT tin cậy.
- `POST /api/v1/auth/login` là demo persona selector, chỉ映射 trong Development/Test; Production yêu cầu `Jwt:SigningKey` riêng ≥32 UTF-8 bytes và từ chối built-in dev key.

## Tương tác chính

```
Operator/Admin  -->  OpsBridge API  -->  PostgreSQL (OpsBridgeDb)
                                    -->  RabbitMQ (outbox)
FactoryFlow     -->  OpsBridge API (webhook, HMAC) --> Inbox + Outbox (txn)
RabbitMQ        -->  GoodsReceiptConsumerHost --> PostgreSQL (txn + inbox dedupe)
OpsBridge API   -->  (không trực tiếp ghi FlashSale bảng)
```
