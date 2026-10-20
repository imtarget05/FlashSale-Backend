# Test Strategy — Commerce + OpsBridge (OpsBridge bounded context)

> Dành cho bounded context OpsBridge (reconciliation, DLQ replay, RBAC, outbox/inbox). Không thay thế `docs/testing/TEST_PLAN.md` (FlashSale order domain, ngày 2026-09-27); hai file cùng tồn tại, mỗi cái phục vụ một bounded context.

## 1. Nguyên tắc

- Mỗi **functional requirement** (`FR-Cxx`) và **non-functional requirement** (`NFR-Cxx`) phải map tới ít nhất một test case có kết quả thực.
- Kết quả test được ghi chính xác: `PASS` (chạy xanh F2), `NOT_IMPLEMENTED` (chưa code), `GAP` (có ý định nhưng chưa có test), `UNVERIFIED` (chỉ là doc/benchmark, không phải gate), `NOT_RUN` (remote CI).
- Không fabricate benchmark, không claim production deployment, không đánh dấu requirement `DONE` nếu chưa có passing test.
- Test sử dụng dependency thật (Testcontainers PostgreSQL 15 + RabbitMQ 3) cho transaction/outbox/messaging, không mock toàn bộ persistence.

## 2. Test pyramid được áp dụng (OpsBridge)

| Tầng | Công cụ | Phạm vi | Ví dụ thực trong repo |
|---|---|---|---|
| Unit | xUnit + fakes | Business rules, state transitions, repository port delegation | `ReplayDeadLetterEventUseCaseTests`, `OpsBridgeArchitectureTests`, `OpsBridgeAuthorizationGuardTests`, `ReconciliationMatchingTests` |
| Contract | xUnit | JSON schema của sự kiện FactoryFlow | `GoodsReceiptContractTests` (`docs/contracts/goods-receipt-confirmed-v1.schema.json`) |
| Integration | xUnit + Testcontainers (PG 15, RabbitMQ 3) | Transaction, outbox, consumer, webhook, reconciliation, auth boundary | `ReconciliationIntegrationTests`, `FailureDrillIntegrationTests`, `OpsBridgeAuthBoundaryTests`, `WebhookIntegrationTests` |
| Load / concurrency | Python harness (`oversell_demo.py`) | No-oversell dưới tải đồng thời | `load-tests/concurrency/oversell_demo.py` (F2.T9) |
| Security | Trivy (fs/config/image), NuGet audit | Vulnerability, secrets, misconfig | F2.T10–T12 |

## 3. Môi trường và dữ liệu

- Integration tests chạy trên **Testcontainers PostgreSQL 15 + RabbitMQ 3** (real dependency, không mock persistence), qua `OpsBridgeFixture` (`IAsyncLifetime`).
- Mỗi test gọi `fixture.ResetAsync()` xóa toàn bộ bảng trước khi chạy → isolation.
- Dữ liệu là synthetic (PO-INT-100, INV-CSV-01, PO-1001, v.v.), không chứa PII thật.
- JWT trong test do `OpsBridgeJwtTokenIssuer` phát hành (test-only signing key), không phải credential thật.

## 4. Entry / Exit criteria (trước F3)

| Entry (trước F3) | Trạng thái |
|---|---|
| Baseline commit đã ghi nhận | PASS (fd7eb3e local) |
| FR/NFR đã xác định | PASS (E1 docs này) |
| Acceptance criteria đã phê duyệt (phạm vi mô phỏng) | PASS (acceptance-criteria.md) |
| Dependencies và test environment sẵn sàng | PASS (Testcontainers PG/RabbitMQ) |
| Test data hợp lệ | PASS (synthetic) |
| Rủi ro chính đã ghi nhận | PASS (traceability-matrix gates chưa hoàn thành) |

| Exit (trước F3) | Trạng thái |
|---|---|
| Required tests PASS | PASS (304/304 local) |
| Không có P0 defects chưa xử lý | GAP (FR-C04 stuck-order detection, NFR-C03 restore drill) |
| Không có unauthorized business action thành công | PASS (auth boundary PASS) |
| Traceability matrix cập nhật | PASS (file này) |
| Evidence gắn với commit và môi trường | PASS (docs/evidence/F2/66dca40/) |
| ADR cập nhật nếu thay đổi kiến trúc | PASS (ADR-017) |
| Rollback/recovery procedure nếu thay đổi quan trọng | GAP (chưa có restore drill) |

**F3 vẫn BLOCKED cho tới khi:** commit + push + remote CI + human acceptance được ghi nhận.
