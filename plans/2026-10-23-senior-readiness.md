# OpsBridge / FlashSale — kế hoạch chứng minh Senior Software và Architecture

**Trạng thái:** đề xuất, chưa triển khai. **Baseline:** `main@033dd03` ngày 23/10/2026. Checkout hiện có WIP ở `Dockerfile.api`, `.github/workflows/load-test.yml` và `load-tests/k6/flash-sale.js`; kiểm tra diff/owner trước khi mở task, không ghi đè hoặc commit lẫn WIP. Đọc `AGENTS.md`, `plans/current.md`, ADR-017/018/019/020 và `docs/evidence/portfolio-audit-2026-10-21.md` trước khi sửa.

## Sản phẩm và ranh giới

FlashSale xử lý tranh mua hàng tồn giới hạn; Redis là fast-fail, PostgreSQL là nguồn tồn kho chuẩn, queue/worker xử lý bất đồng bộ. OpsBridge là bounded context riêng cho webhook nhận hàng, ba bên PO/GR/invoice, discrepancy và DLQ; không biến nó thành tích hợp thật với QuanLyKho hoặc MiniERP khi chưa có contract. Giữ dependency direction Domain ← Application ← Infrastructure ← API/Worker. Bằng chứng benchmark phải từ run thực, không sao chép con số lịch sử.

## O0 — Đóng lỗi release gate trên HEAD

**Bằng chứng hiện tại:** [CI run](https://github.com/imtarget05/FlashSale-Backend/actions/runs/39898614586) có unit, integration, browser E2E, concurrency, image/manifest jobs pass, nhưng Trivy fail do một Dockerfile thiếu non-root `USER`; [k6 run](https://github.com/imtarget05/FlashSale-Backend/actions/runs/39898615635) fail vì action tìm `test.js`. WIP hiện tại có thể đã sửa hai lỗi; chỉ coi là xong khi remote run trên successor SHA xanh.

1. Review WIP, xác định Dockerfile nào bị DS-0002; chạy image bằng UID không phải root, `readOnlyRootFilesystem`/volume/probe phù hợp. Không chèn `USER` hình thức khiến API không ghi được thư mục cần thiết.
2. Chạy k6 với đúng `load-tests/k6/flash-sale.js`, cùng một service startup đã chứng minh healthy. Script phải có threshold và verdict non-zero khi fail; lưu summary/JSON artifact với SHA, quy mô tải, số accepted/rejected, stock cuối và p95. Kiểm tra câu chuyện 50 người tranh 10 suất và không oversell.
3. Chạy lại full CI `ci-gate` sau security/k6 fix. Tách deployment secret/OIDC từ build/test gate; không gọi release xanh nếu release-push/gitops-update còn skipped do upstream failure.

**Gate O0:** required CI + k6 pass trên cùng SHA. Nếu Azure deploy chưa chạy, ghi rõ `LOCAL/CI_VERIFIED`, không ghi `PRODUCTION_READY`.

## O1 — Tính đúng hệ thống phân tán

1. Dùng `tests/IntegrationTests/`, `tests/OpsBridge.IntegrationTests/` và `load-tests/concurrency/` để chứng minh transaction/outbox/inbox, idempotency, retry/DLQ và stock conservation. Ca lỗi bắt buộc: queue reject sau Redis reserve, broker confirm/nack hoặc connection loss trước ACK, worker crash sau DB commit, duplicate webhook khác payload, hai payment/reconciliation attempts cùng khóa.
2. Mỗi ca ghi trạng thái bền vững trong PostgreSQL, Redis/broker state và API response; không dựa vào mocked success. Kiểm thử migration Job trên DB cũ và DB mới trước rollout ứng dụng; rollback app không được làm hỏng schema còn dùng.
3. Chạy một load/resilience profile có baseline và giới hạn tài nguyên. Phân tích p95, error/timeout, queue depth, DB connection pressure, lag và recovery. Không dùng một local run làm SLA.

**Gate O1:** full suite Testcontainers trên exact SHA, failure drills có log/trace và một benchmark có thể lặp lại. Docs/ADR-019 mô tả at-least-once chính xác; không claim exactly-once transport.

## O2 — Bằng chứng kiến trúc và vận hành

1. Cập nhật `docs/architecture/current-architecture.md` để phản ánh cả FlashSale lẫn OpsBridge hiện tại; giữ Phase 5 như lịch sử. ADR nêu tại sao Redis/queue được đưa vào sau baseline đơn giản, tại sao hai bounded context khác nhau, trade-off của async `202 Accepted`, migration, auth và web token boundary.
2. Hoàn thiện một hành trình browser → API → DB → event/worker → dashboard, kèm runbook khi broker/DB/worker lỗi. `docs/interview-notes/` có quyết định, dữ liệu đo, lỗi đã gặp và điều chưa được chứng minh.
3. Workload repo sở hữu image, Kubernetes overlays, health/readiness, migration và metrics. AKS repo chỉ tiêu thụ release source/digest, đặt platform policy, GitOps/observability. Khi platform chuyển Argo từ `main` sang release SHA/tag, render và kiểm tra chính xác ref trước sync.

**Gate O2:** README/CV chỉ trỏ run theo commit; live Azure/Service Bus/Key Vault chỉ được claim khi có runtime evidence. Không thêm agent/LLM feature chỉ để tăng stack list.
