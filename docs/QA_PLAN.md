# QA PLAN — OpsBridge (Đối soát & Hệ thống vận hành)

**Repository:** `/Users/mainguyenbinhtan/Downloads/OpsBridge`  
**Bounded context:** OpsBridge (event-driven operations: ingestion, 3-way reconciliation, discrepancies, staff decisions, DLQ replay audit)  
**Ngôn ngữ:** tiếng Việt  
**Phiên bản:** 1.0  
**Người soạn thảo:** QA Lead / Test Architect

---

## 1. Chiến lược kiểm thử (Test Strategy)

### 1.1 Mục tiêu QA
- Xác minh sự đúng đắn của luồng dữ liệu chính OpsBridge: **PO → GR → Invoice → Reconciliation → Discrepancy → Audit**.
- Đảm bảo invariant kinh doanh: **không oversell (FlashSale)**, **không đối soát lại hóa đơn đã khớp**, **sai lệch được ghi nhận không thay đổi (immutable audit)**.
- Phát hiện sớm các rủi ro bảo mật (RBAC bypass, API key rò rỉ qua `localStorage`) và sự cố UX on màn hình đối soát desktop (label chồng chữ, section bị ép hẹp).
- Cung cấp bằng chứng 기록 để phỏng vấn/xuất bản: số liệu đối soát backend phải khớp chính xác với hiển thị UI.

### 1.2 Phạm vi (OpsBridge bounded context)
Trong phạm vi:
- `ThreeWayMatchingEngine` — khớp 3 bên (PO ↔ GR ↔ Invoice).
- `ReconcileInvoiceUseCase` — việc đối soát hóa đơn.
- `ReviewDiscrepancyUseCase` — quy trình HITL duyệt/từ chối sai lệch.
- Mock data `INV-2026-8803`, `PO-2026-1003`, `GR-2026-5003`, `SKU-BOBA-BLACK-20K`.
- AI Copilot widget (Gemini + heuristic fallback).
- Màn hình đối soát desktop, audit timeline, StatusBadge.
- FlashSale zero-oversell invariant (cross-context).

Ngoài phạm vi (không kiểm thử trong plan này): FlashSale order processing chi tiết (có plan riêng), thanh toán real production, triển khai Kubernetes/Azure production.

### 1.3 Mô hình kiểm thử (Test Pyramid)

| Tầng | Công cụ | Mục tiêu | Vị trí |
|------|---------|----------|--------|
| **Unit (.NET)** | xUnit + `dotnet test` | Logic nghiệp vụ khép kín: matching engine, RBAC guard, HMAC verifier, tolerance policy, heuristic replies | `tests/OpsBridge.UnitTests/` |
| **Integration** | Testcontainers PostgreSQL + .NET | Luồng end-to-end trong cùng process với DB thực: reconcile → discrepancy → audit event trong 1 transaction | `tests/OpsBridge.IntegrationTests/` |
| **Contract** | .NET contract tests + `node --test contract/*.test.mjs` | Shape API, mã trạng thái, schema payload giữa backend và frontend | `tests/OpsBridge.ContractTests/`, `src/OpsBridge.Web/contract/` |
| **E2E** | Playwright | Luồng người dùng thực trên trình duyệt: login → reconciliation → review discrepancy → audit timeline | `src/OpsBridge.Web/e2e/` |
| **Frontend unit** | Vitest / `node --test` | Thành phần React: StatusBadge, reconciliation grid, audit timeline, AI Copilot heuristic | `src/OpsBridge.Web/` |

Quy tắc ưu tiên: **Unit > Integration > Contract > E2E**. Giữ E2E tối thiểu, chỉ cho các luồng quan trọng business.

### 1.4 Kiểm thử phi chức năng (Non-functional)
- **Hiệu năng:** Đo throughput của reconciliation (số hóa đơn đối soát/giây) trên Testcontainers PG; xác định ngưỡng chấp nhận được trước khi xuất bản.
- **Bảo mật:** Kiểm thử RBAC (Admin/Operator/FinanceReviewer vs Auditor read-only vs quyền khác nhận 403), verifier HMAC webhook, không lưu khóa nhạy cảm (Gemini API key) trong `localStorage`.
- **Khả năng tiếp cận (Accessibility):** Kiểm tra màn hình đối soát desktop — label trạng thái không chồng chữ, phần hóa đơn không bị ép hẹp, màu badge đúng theo `StatusBadge`.

---

## 2. Môi trường kiểm thử

- **.NET 10 SDK** (tương thích `net10.0`).
- **PostgreSQL** qua **Testcontainers** (mỗi lần chạy test dùng CSDL độc lập, xóa sau khi xong).
- **Redis** (caching/reservation cho FlashSale).
- **RabbitMQ** (messaging cho ingestion & DLQ replay).
- **Cloudflare Pages Functions** (frontend edge API, Wrangler) và dev local với **Vite**.
- **Isolated test DB per test run:** mỗi test class/run tạo schema/database riêng trên container PG, tránh va chạm dữ liệu.
- Biến môi trường đọc từ `.env.example`, không commit secret vào git.

---

## 3. Danh sách test case ưu tiên (P0/P1/P2)

Tổng cộng **26 test case** (≥ 20 yêu cầu).  
**Mức độ:** P0 = chặn xuất bản, P1 = quan trọng, P2 = cải thiện.

### 3.1 Đối soát / khớp 3 bên (Reconciliation / 3-way matching)

| ID | Mức | Test case | Kết quả mong đợi |
|----|-----|-----------|------------------|
| REC-01 | P0 | **INV-2026-8803:** 200 BAO invoiced vs 160 BAO received → variance 12.000.000 VND, status `Open`, severity `High`, type `QuantityMismatch` | Số liệu trên backend và UI khớp chính xác (200/160, 12M VND). |
| REC-02 | P0 | Tolerance policy: price 1.0%, qty 0.0%, absolute 0 | Hóa đơn ngoài ngưỡng được đánh dấu sai lệch; trong ngưỡng được duyệt tự động. |
| REC-03 | P0 | Concurrent allocation: atomic conditional UPDATE ngăn over-allocation | Khi nhiều worker reconcile cùng invoice, chỉ một phép cập nhật thành công, không vượt số lượng GR. |
| REC-04 | P1 | Currency mismatch (invoice VND, PO USD) | Đánh dấu `CurrencyMismatch`, chặn khớp tự động. |
| REC-05 | P1 | Supplier mismatch (invoice supplier ≠ PO supplier) | Đánh dấu `SupplierMismatch`, yêu cầu review nhân viên. |
| REC-06 | P1 | Missing PO (invoice không có PO tương ứng) | Đánh dấu `MissingPO`, chuyển về discrepancy queue. |
| REC-07 | P1 | Missing GR (không có GR để khớp) | Đánh dấu `MissingGR`, chặn duyệt. |
| REC-08 | P1 | Greedy non-overlapping allocation across multiple GRs | Phân phối số lượng invoiced theo thứ tự GR, không chồng chéo, tổng phân phối ≤ invoiced. |
| REC-09 | P0 | Block re-reconciliation of already-matched invoices | Hóa đơn đã `Matched`/status cuối không thể được reconcile lại, trả lỗi nghiệp vụ. |

### 3.2 Duyệt sai lệch (Discrepancy review)

| ID | Mức | Test case | Kết quả mong đợi |
|----|-----|-----------|------------------|
| DISC-01 | P0 | RBAC: `Admin`, `Operator`, `FinanceReviewer` có quyền review | Các endpoint review trả 200 cho vai trò này. |
| DISC-02 | P0 | RBAC: `Auditor` chỉ read-only | Gọi endpoint review (POST/PATCH) trả 403; GET audit timeline vẫn 200. |
| DISC-03 | P0 | RBAC: vai trò khác (ví dụ `Customer`, `Guest`) nhận 403 | Tất cả endpoint discrepancy trả 403. |
| DISC-04 | P0 | `ApprovedWithTolerance` → `Resolved`, commits allocations, invoice `ApprovedWithVariance` | Phân phối được ghi permanent, trạng thái hóa đơn cập nhật, AuditEvent ghi trong cùng transaction. |
| DISC-05 | P0 | `Rejected` → `Rejected`, releases allocations, invoice `Rejected` | Phân phối được giải phóng, hóa đơn trạng thái `Rejected`. |
| DISC-06 | P0 | Immutable `AuditEvent` written in same transaction | Mỗi hành động review sinh AuditEvent, không thể sửa/xoá, cùng transaction với nghiệp vụ. |

### 3.3 AI Copilot (heuristic fallback)

| ID | Mức | Test case | Kết quả mong đợi |
|----|-----|-----------|------------------|
| AI-01 | P0 | INV-2026-8803: answer matches mock data exactly (200/160 BAO, 12M VND) | Trả lời heuristic trùng khớp dữ liệu mock, không sai số. |
| AI-02 | P1 | Unknown invoice (INV-2026-9999): returns `không xác định`, không bịa dữ liệu | Không suy đoán số lượng/số tiền; phản hồi rõ ràng "không xác định". |
| AI-03 | P1 | Gemini live path unavailable → heuristic fallback works | Khi Gemini lỗi/không có key, widget vẫn phản hồi bằng heuristic, không hiển thị lỗi خام đến người dùng. |

### 3.4 Frontend UX (critical — feedback từ nhà tuyển dụng)

| ID | Mức | Test case | Kết quả mong đợi |
|----|-----|-----------|------------------|
| UX-01 | P0 | Reconciliation desktop screen: status labels không chồng chữ | Trên màn hình desktop, các nhãn trạng thái hiển thị rõ ràng, không bị chồng lấp. |
| UX-02 | P0 | Invoice section không bị ép hẹp | Phần chi tiết hóa đơn có đủ không gian, nội dung không bị cắt/én. |
| UX-03 | P1 | Badge/status colors đúng theo `StatusBadge` component | Màu Status/Open/Resolved/Rejected tuân thủ bảng màu thiết kế, không hardcode màu riêng. |
| UX-04 | P1 | Audit timeline: read-only cho `Auditor` | Auditor chỉ xem timeline, không có nút hành động sửa/xoá. |
| UX-05 | P1 | Audit timeline: immutable entries | Các dòng timeline không cho phép chỉnh sửa nội dung sau khi ghi. |

### 3.5 FlashSale (zero-oversell invariant)

| ID | Mức | Test case | Kết quả mong đợi |
|----|-----|-----------|------------------|
| FS-01 | P0 | Concurrent orders don't oversell inventory | Nhiều đơn đặt hàng đồng thời không làm tồn kho âm; tổng bán ≤ tồn kho ban đầu. |
| FS-02 | P1 | Payment lock | Đơn hàng đang xử lý thanh toán bị khóa, không cho phép đặt hàng trùng cho cùng khoá idempotency. |
| FS-03 | P1 | Outbox poison message handling | Tin nhắn lỗi (poison) trong outbox được chuyển DLQ, không làm dừng toàn bộ pipeline xử lý. |

---

## 4. Quản lý dữ liệu kiểm thử (Test Data Management)

### 4.1 Mock data chuẩn
- **Hóa đơn:** `INV-2026-8803` (status `Discrepancy`, subtotal 60.000.000 VND, tax 6.000.000, total 66.000.000 VND).
- **PO:** `PO-2026-1003`.
- **GR:** `GR-2026-5003` (chấp nhận 160 BAO).
- **SKU:** `SKU-BOBA-BLACK-20K` (Trân Châu Đen Dẻo Cao Cấp, bao 20kg), unitPrice 300.000 VND, invoicedQuantity 200, matchedQuantity 160.
- Nguồn: `src/OpsBridge.Web/functions/api/v1/[[path]].ts` (mock invoice data) và `docs/EVIDENCE.md`.

### 4.2 Seed data
- Tạo tổ hợp PO/GR/Invoice cho các kịch bản: khớp chuẩn, sai số lượng, sai đơn giá, sai tiền tệ, thiếu PO, thiếu GR, nhiều GR phân phối.
- Mỗi test run seed dữ liệu mới vào Testcontainers PG, xóa sạch sau khi hoàn tất (không chia sẻ trạng thái giữa các test).

### 4.3 Idempotency-Key
- Các thao tác ghi (create invoice, reconcile, review discrepancy) phải hỗ trợ `Idempotency-Key`; gửi lại cùng key không tạo bản ghi trùng/lệnh side-effect kép.
- Kiểm thử: gửi 2 request cùng Idempotency-Key → chỉ có 1 tác dụng, phản hồi thứ hai trùng với lần đầu.

---

## 5. Tự động hóa & CI/CD

### 5.1 Backend (.NET)
- `dotnet test tests/OpsBridge.UnitTests/...`
- `dotnet test tests/OpsBridge.ContractTests/...`
- `dotnet test tests/OpsBridge.IntegrationTests/...` (dùng Testcontainers PG)

### 5.2 Frontend
- `npm run test:contract` (7 tests) trong `src/OpsBridge.Web/`
- `npm run test:e2e` (Playwright) trong `src/OpsBridge.Web/`
- `npm run lint` → `oxlint`

### 5.3 CI baseline đã ghi nhận
- **70/70 OpsBridge tests passing** tại commit `1ecd302`. Mục tiêu: duy trì hoặc vượt qua con số này; bất kỳ giảm sút nào phải được điều tra trước khi merge.

### 5.4 Pipeline đề xuất (GitHub Actions)
1. Lint (`dotnet format --verify-no-changes`, `oxlint`).
2. Build (.NET + Vite).
3. Unit tests (.NET).
4. Integration tests (Testcontainers PG/Redis/RabbitMQ).
5. Contract tests (.NET + `node --test`).
6. E2E Playwright (headless).
7. Thất bại ở bất kỳ giai đoạn nào → chặn merge.

---

## 6. Tiêu chí xuất bản (Release Criteria)

- Tất cả test case **P0 pass**.
- Số liệu đối soát trên backend **khớp chính xác** với hiển thị trên UI (ví dụ INV-2026-8803: 200/160 BAO, 12.000.000 VND).
- Không có **RBAC bypass** (tất cả vai trò không được phép nhận 403, Auditor chỉ read-only).
- Không có **overlapping labels** trên màn hình reconciliation desktop; invoice section không bị ép hẹp.
- **Evidence page** (`docs/EVIDENCE.md` hoặc tương ứng) được cập nhật với kết quả chạy test mới nhất, kèm chụp màn hình/nhật ký nếu có.
- Không có secret (API key, token) commit vào repository hoặc hiển thị trên UI.

---

## 7. Rủi ro & khắc phục (Risks & Mitigation)

| Rủi ro | Mức độ | Khắc phục |
|--------|--------|-----------|
| **Hardcoded Gemini API key trong `localStorage`** (`opsbridge_gemini_key`) → security finding | Cao | Không lưu key nhạy cảm trên client; chuyển key vào backend edge function/environment variable, client chỉ gửi yêu cầu qua API đã xác thực. |
| **Mock data drift giữa heuristic và UI** → số liệu AI Copilot không khớp màn hình đối soát | Trung | Gom dữ liệu mock vào 1 nguồn đơn (single source of truth), test AI-01 phải chạy cùng lúc với REC-01. (Đã khử trong S1: heuristic trả lời trùng khớp mock INV-2026-8803.) |
| Testcontainers không khởi động được trong CI (dialect/port) | Trung | Pin phiên bản image PostgreSQL, dùng healthcheck trước khi chạy integration test. |
| E2E flaky do chờ phần tử React render | Trung | Dùng `waitFor`/locator chậm của Playwright, tránh `sleep` cứng, chụp screenshot khi thất bại. |
| Không có lịch sử SLA latency production | Thấp | Ghi nhận latency test local/Testcontainers làm baseline, không claims production readiness mà chưa có dữ liệu thực. |

---

## 8. Lập kế hoạch thực thi (ngắn hạn)

1. Tạo file `docs/QA_PLAN.md` (nội dung ở trên).
2. Chạy lại baseline: `dotnet test` (70/70) + `npm run test:contract` (7 tests) + `npm run test:e2e`.
3. Triển khai các test case P0 chưa có automation (ưu tiên REC-01, DISC-01…06, AI-01, UX-01…02).
4. Cập nhật `docs/EVIDENCE.md` sau mỗi lần chạy CI thành công.

*Kết thúc QA plan.*