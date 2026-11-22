# Recruiter Evidence — Sacombank Portfolio (Backend + Cloud/DevOps)

> **Tài liệu kiểm chứng năng lực kỹ thuật thực tế (Recruiter Evidence Matrix & Demo Runbooks)**  
> **Cập nhật:** 2026-11-21 | **Môi trường kiểm chứng:** macOS (Apple M1 Pro, 8 Cores, 16GB RAM), Docker Desktop 29.6.1, Local Kind / Testcontainers

---

## 1. FlashSale-Backend (Java 17, Spring Boot 4.0.9)

### Bài toán & Bối cảnh kỹ thuật (Problem)
Xử lý thanh toán flash-sale với thông lượng cao, giải quyết triệt để vấn đề double-charging khi các yêu cầu cùng `idempotencyKey` đến đồng thời; mô phỏng các kết quả thanh toán từ cổng trung gian (success / declined / timeout); duy trì tính toàn vẹn trạng thái thanh toán bền vững trong PostgreSQL và phát hành sự kiện bất đồng bộ tin cậy đến downstream systems qua RabbitMQ thông qua mẫu thiết kế **Transactional Outbox**.

### Kiến trúc & Năng lực đã kiểm chứng (Verified Capabilities)
- **Ports & Adapters (Hexagonal Architecture):** Cô lập `PaymentProvider` port; mô phỏng deterministic local qua `SimulatedPaymentProvider`.
- **Durable Database Idempotency:** PostgreSQL là Single Source of Truth; bảng `payment_records` sử dụng `INSERT ... ON CONFLICT DO NOTHING` dựa trên ràng buộc duy nhất của `idempotencyKey`. Xử lý conflict trả về HTTP 409 khi cùng key nhưng payload khác nhau.
- **Cache-Aside & Graceful Fallback:** Redis cache (`@Cacheable` khi tra cứu `getPayment`, `@CacheEvict` khi tạo `processPayment`). Khi Redis ngừng hoạt động (outage), hệ thống tự động fallback truy vấn trực tiếp từ PostgreSQL mà không làm gián đoạn API.
- **Transactional Outbox & Dead Letter Queue (DLQ):** Lưu trữ outbox event cùng transaction với bản ghi thanh toán. Outbox relay gửi sang RabbitMQ kèm publisher confirms; các sự kiện lỗi/malformed tự động đưa vào Dead Letter Exchange/Queue.
- **Fail-Closed Security & RBAC:** OIDC resource server từ chối các token thiếu/sai issuer/audience; JWT validation chặn token hết hạn/bị giả mạo (401); phân quyền nghiêm ngặt theo vai trò (Customer gọi API thanh toán bị 403, Staff/Admin được cấp quyền 200).

| Năng lực (Capability) | Bằng chứng kiểm chứng thực tế (Local Executable Evidence) |
|---|---|
| **Idempotent Payment** (cùng key + payload → 1 dòng DB, 1 outbox event) | `PaymentServiceIntegrationTest.concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed`<br>`PaymentServiceIntegrationTest.processPayment_idempotentReplay_returnsSameResult` |
| **Idempotency Conflict** (cùng key, khác payload → HTTP 409) | `PaymentServiceIntegrationTest.concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment` |
| **Gateway Outcome Simulation** (cents `.02` → Declined, `.03` → TimedOut) | `PaymentServiceIntegrationTest.processPayment_declined_whenCentsIsTwo`<br>`PaymentServiceIntegrationTest.processPayment_transientTimeout_whenCentsIsThree` |
| **Transient Timeout Retry Protection** (retry sau timeout trả về kết quả đã ghi nhận) | `IdempotencyReliabilityIntegrationTest.retrySameKeyAfterTransientTimeout_returnsRecordedResultWithoutDuplicate` |
| **Outbox Atomicity & Exact Once Delivery** (persisted sau commit, publish 1 lần) | `IdempotencyReliabilityIntegrationTest.outboxEventPersistedAfterDbCommitAndPublishedExactlyOnce` |
| **Consumer Duplicate Event Handling** (chỉ tạo 1 payment/outbox) | `IdempotencyReliabilityIntegrationTest.consumerReceivesDuplicateOrderEvent_processedOnce` |
| **Concurrent Same-Key Race Protection** (10 request đồng thời → 1 payment + 1 outbox) | `IdempotencyReliabilityIntegrationTest.tenConcurrentRequestsSameIdempotencyKey_onePaymentAndOneOutboxEvent` |
| **Redis Cache-Aside Operations** (hit/miss/evict) | 5 tests trong `PaymentCacheRedisTest` (`testCacheMiss_...`, `testCacheHit_...`, `testCacheEviction_...`) |
| **Graceful Cache Fallback** (Redis down → vẫn đọc từ DB) | `CacheFailureFallbackTest`, `RedisOutageIntegrationTest`, `BrokerCacheFailureDrillTest` |
| **OIDC Fail-Closed Security** (chặn cấu hình thiếu issuer/audience) | `ProductionOidcConfigTest` |
| **JWT Validation & RBAC** (401 invalid/expired token; 403 unauthorized role) | `AuthSecurityIntegrationTest` |
| **Transactional Outbox & DLQ** (RabbitMQ confirms + DLQ routing) | `PaymentOutboxRelay`, `PaymentServiceIntegrationTest.malformedOrderEvent_isDeadLettered` |
| **Local CI Gate** (compile, unit tests, Docker build, k8s manifest validate) | `scripts/ci-gate.sh`, `.github/workflows/ci.yml` |

### Kết quả đo kiểm tải thực tế (k6 Load Test Measurement)
Chạy trực tiếp k6 kịch bản `load-tests/k6/flash-sale.js` trên máy Apple M1 Pro (8 cores, 16GB RAM) với Docker container stack (Spring Boot 4.0.9 + PostgreSQL 15 + Redis 7 + RabbitMQ 3 + MongoDB 7):
- **Cấu hình tải:** `ramping-arrival-rate` từ 2 req/s → 5 req/s → 0 req/s trong 25 giây (max 30 VUs).
- **Kết quả đo lường thực tế:**
  - **Checks:** **100.00%** (194/194 checks passed, ngưỡng `rate > 0.98` đạt).
  - **HTTP Request Duration p(95):** **92.11ms** (ngưỡng `p(95) < 1000ms` đạt; avg = 32.02ms, med = 18.85ms, p(90) = 39.60ms).
  - **HTTP Failure Rate:** **0.00%** (0 request thất bại trên 98 requests, ngưỡng `rate < 0.02` đạt).
  - **Tổng Iterations:** 97 iterations hoàn tất (4.01 iters/s).

### CV Bullet Points (Honest & Verified)
- Thiết kế và phát triển payment microservice xử lý flash-sale bằng Java 17 / Spring Boot 4: PostgreSQL source of truth với `ON CONFLICT DO NOTHING` deduplication, Redis look-aside cache với graceful fallback khi cache outage, và Transactional Outbox pattern với RabbitMQ publisher confirms + DLQ.
- Tự động hóa bộ kiểm thử toàn diện với **187 automated tests pass (0 failures)** bao gồm 135 pure unit/slice tests và 38+ Testcontainers integration tests (PostgreSQL 15, Redis 7, RabbitMQ 3, MongoDB 7).
- Triển khai fail-closed JWT/OIDC authentication với role-based authorization (Customer/Staff/Admin); bảo đảm kiểm soát truy cập và cô lập dữ liệu.
- Đo kiểm hiệu năng thông qua k6 load test cục bộ: đạt p95 = 92.11ms, tỷ lệ lỗi 0.00% dưới tải ramping burst.

### Giới hạn & Điểm không tuyên bố (Not Claimed)
- Không kết nối live cổng thanh toán thật (Stripe live API) — môi trường kiểm thử dùng deterministic simulation.
- Không tuyên bố live AKS / Render production cluster đang chạy — toàn bộ cấu hình hạ tầng k8s/Helm được kiểm chứng cục bộ trên Docker/Kind.
- Không sử dụng Kafka / Azure Service Bus — RabbitMQ là message broker duy nhất được tích hợp và kiểm chứng.

---

## 2. QuanLyKho (Python 3.11, FastAPI, SQLAlchemy 2, PostgreSQL 16)

### Bài toán & Bối cảnh kỹ thuật (Problem)
Hệ thống quản lý vòng đời kho hàng và vận hành ngân quỹ đa chi nhánh (Enterprise Inventory & Treasury Operations Platform). Giải quyết rủi ro chênh lệch tiền mặt, chống thất thoát kho do overselling/race condition, ngăn chặn hành vi gian lận tự phê duyệt (self-approval) và bảo vệ an toàn giao dịch qua sổ cái kép cân bằng tuyệt đối.

### Kiến trúc & Năng lực đã kiểm chứng (Verified Capabilities)
- **Mathematical Double-Entry General Ledger:** Bắt buộc nguyên tắc cân bằng kế toán ($\sum \text{Debit} \equiv \sum \text{Credit}$), kiểm tra tính hợp lệ bằng PostgreSQL `CheckConstraint`. Lịch sử ghi sổ là bất biến (immutable); mọi điều chỉnh/hủy bỏ phải tạo bút toán đảo (reversal journal) kế thừa liên kết `reversal_of`.
- **Maker-Checker Dual Control:** Invariant A1 (`maker_id != approver_id`) được cưỡng chế ở cả tầng domain và API route; tuyệt đối không cho phép tự phê duyệt ngay cả với quyền root admin.
- **Approval Security Gate (PIN & Lockout):** Các nghiệp vụ nhạy cảm ($\ge 50$ units hoặc $\ge 500\text{k VND}$) kích hoạt cổng mã PIN kiểm soát viên; cơ chế brute-force protection khóa tài khoản sau 5 lần nhập sai trong 15 phút. Fail-closed khi ở ngoài demo mode nếu chưa cấu hình secret PIN.
- **Pessimistic Locking & Concurrency Control:** Ngăn chặn race condition bằng ordered `SELECT ... FOR UPDATE` row locks kết hợp optimistic concurrency (`version_id_col`) và First-Expired, First-Out (FEFO) batch allocation.
- **Audit Hash Chain (Tamper-Evident Trail):** Chuỗi hash mật mã (SHA-256) liên kết từng bản ghi audit với genesis hash và previous hash; ORM listeners và PostgreSQL triggers ngăn chặn vật lý mọi hành vi `UPDATE` hoặc `DELETE` trên bảng audit.
- **Temporal RBAC & Branch Scoping:** Sử dụng `ScopeGrant` có thời hạn; tự động inject phạm vi chi nhánh vào query SQL để triệt tiêu lỗ hổng IDOR giữa các chi nhánh.

### Số liệu đo lường & Đối chiếu CI / Test Suites
- **Tổng số test thu thập (Collected Tests):** **1,456 tests** trong `backend/tests/`.
- **Kết quả CI công bố (Baseline CI tại commit `2e753d1` / PR #31):** **1,285 passed, 8 skipped, 1 xfailed**, 25 warnings trên Python 3.11 + PostgreSQL 16 (thời gian chạy 541.68s); Frontend Flutter đạt **355 passed, 0 failed**.
- **Kiểm chứng trực tiếp các test suites trọng yếu:**
  - `tests/test_approval_controls.py`: **10/10 pass** (D4 demo PIN gate, production fail-closed, rate limiting 5 attempts/15min lockout, supervisor PIN).
  - `tests/test_banking_concurrency.py`: **1/1 pass** (kiểm soát đồng thời: nhiều workers tăng balance song song không làm lost update).
  - `tests/test_banking_double_entry.py`: **2/2 pass** (chuyển quỹ tạo bút toán cân bằng $\sum \text{Debit} \equiv \sum \text{Credit}$; từ chối bút toán lệch).
  - `tests/test_audit_chain.py`: **22/22 pass** (canonical JSON hashing, avalanche effect, hash chaining, tamper detection, ORM & DB trigger block UPDATE/DELETE).
  - `tests/test_banking_audit_chain.py`: **1/1 pass** (xác minh tính hợp lệ và phát hiện sửa đổi chuỗi audit).
  - `tests/test_banking_saga.py`: **1/1 pass** (saga rollback / compensation khi step sau gặp lỗi).
  - `tests/test_banking_idempotency.py`: **1/1 pass** (saga chạy cùng idempotency key chỉ thực thi duy nhất 1 lần).

### CV Bullet Points (Honest & Verified)
- Phát triển nền tảng backend ngân quỹ và kho vận trên Python 3.11, FastAPI, SQLAlchemy 2 (AsyncSession) và PostgreSQL 16; thiết kế sổ cái kép kế toán ($\sum \text{Debit} \equiv \sum \text{Credit}$) bảo đảm tính toàn vẹn toán học.
- Xây dựng cơ chế Maker-Checker với bất biến chống tự phê duyệt (`maker_id != approver_id`) và cổng bảo vệ PIN kiểm soát viên tích hợp brute-force rate limiter (5 lần/15 phút).
- Ngăn chặn race condition và overselling khi giao dịch đồng thời nhờ ordered pessimistic row locks (`SELECT ... FOR UPDATE`) và kiểm tra version lạc quan.
- Hiện thực hóa chuỗi nhật ký kiểm toán bất biến (Audit Hash Chain) với SHA-256 chaining và DB-level triggers chặn can thiệp sửa/xóa dữ liệu.

### Giới hạn & Điểm không tuyên bố (Not Claimed)
- Không tuyên bố live SaaS production SLA 99.99% — hệ thống được chứng minh trên môi trường test/staging tự động hóa.

---

## 3. AKS-SRE-Platform (Kubernetes Platform Blueprint & SRE Operations)

### Bài toán & Bối cảnh kỹ thuật (Problem)
Xây dựng nền tảng hạ tầng Kubernetes (AKS platform blueprint) an toàn, tuân thủ nguyên tắc GitOps (ArgoCD), thực thi chính sách tự động (Policy Gates), bảo đảm tính quan sát (SLO/burn-rate alerts) và xây dựng SRE Incident Response Agent có khả năng hỗ trợ xử lý sự cố ở chế độ read-only an toàn.

### Số liệu đo lường & Bằng chứng kiểm chứng (Verified Metrics & Evidence)
| Chiều đo lường (Dimension) | Số đo thực tế | Lệnh kiểm chứng & Tệp nguồn |
|---|---|---|
| **Gate Regression Matrix** | **62 passed, 0 failed** (62 assertions total, G1–G16) | `python3 tests/test_gates.py` |
| **Gate G10 (CI Narrowing Protection)** | **7/7 assertions passed** (tflint recursive config, test roots, non-zero exit) | `tests/test_gates.py:330-410` |
| **Policy Enforcement** | **121 objects passed** trên 6 Kustomize roots | `python3 scripts/policy-gate.py` |
| **GitOps Integrity** | **38 passed checks**, zero wildcard permissions | `python3 gitops/validate-gitops.py` |
| **SRE Agent Unit Suite** | **14/14 passed, 86.74% statement coverage** | `PYTHONPATH=. pytest tests/test_sre_incident_agent.py` |
| **Total Test Suite** | **64 passed, 12 subtests passed** (0 failures) | `PYTHONPATH=. pytest tests/` |
| **Rollback Execution Time** | **0.105 seconds** (khôi phục 2/2 ready pods với image digest) | `scripts/kind_rollback_drill.py`, CI run 44916032886 |

### Xác thực chi tiết Gate G10 ("the CI gates are not narrowed back")
Toàn bộ 7 điều kiện của Gate G10 đã được kiểm chứng và đạt tuyệt đối:
1. `tflint` thực hiện lint từ thư mục gốc `terraform/`.
2. `tflint --init` bắt buộc chạy trước `tflint --recursive`.
3. Quá trình lint đệ quy bắt buộc truyền tham số `--config` với **đường dẫn tuyệt đối** (ngăn chặn fallback ruleset gây sót lỗ hổng AzureRM).
4. CI thực thi tất cả các terraform root sở hữu file kiểm thử `*.tftest.hcl`.
5. Bước kiểm thử duyệt qua toàn bộ các declared roots.
6. Bước test làm fail pipeline nếu tổng số test root tìm thấy bằng 0.
7. Nhãn gate-matrix của GitHub Actions workflow khớp chính xác với định nghĩa gate matrix trong code.

### Quy trình xác thực Image Digest & Rollback Drill
- Khác biệt giữa Cloud Promotion và Local Drill: Đợt drill trên Kind chứng minh chu trình: Build demo workload → Đẩy vào local registry → Lấy immutable image digest (`sha256:ee364b0871...`) → Triển khai qua K8s Deployment → Cố tình kích hoạt deployment ảnh hỏng (`ErrImagePull`) → Thực thi rollback phục hồi đúng image digest ban đầu trong 0.105 giây với 2/2 pods đạt trạng thái Ready (HTTP probe 200).
- Gate G15/G16 bảo đảm GitOps workflow là read-only, không tự ý deploy đè và bắt buộc render đúng targetRevision cam kết.

### CV Bullet Points (Honest & Verified)
- Thiết kế Kubernetes Platform Blueprint với kiến trúc Zero-Trust: Kustomize policy gates kiểm tra 121 Kubernetes objects, ArgoCD AppProject kiểm soát 38 điều kiện phân quyền không dùng wildcard.
- Xây dựng bộ regression gate matrix gồm 62 assertions (G1–G16) ngăn chặn suy hao chính sách CI/CD, bảo đảm tính bất biến của cấu hình Terraform và GitOps manifests.
- Thực nghiệm kịch bản phục hồi sự cố tự động (zero-downtime rollback drill) dựa trên immutable image digest trên cluster Kind, hoàn tất lệnh rollback trong 0.105 giây.
- Phát triển SRE Incident Agent bằng Python với độ bao phủ kiểm thử 86.74%, tuân thủ nguyên tắc read-only adapters để thu thập telemetry phục vụ chẩn đoán sự cố an toàn.

### Giới hạn & Điểm không tuyên bố (Not Claimed)
- Azure cluster production đã được decommission có chủ đích bằng Terraform destroy; không duy trì cluster đám mây trực tiếp chạy liên tục để tránh phát sinh chi phí. Toàn bộ kịch bản phục hồi và chính sách được chứng minh qua local Kind cluster và GitOps static gates.

---

## 4. KỊCH BẢN DEMO 2-3 PHÚT MỖI REPO

### Demo 1: FlashSale-Backend (2 phút)
1. **Khởi động dịch vụ phụ trợ (30s):**
   ```bash
   docker compose up -d postgres redis rabbitmq
   ```
2. **Thực thi kịch bản kiểm chứng độ tin cậy Idempotency & Outbox (45s):**
   ```bash
   mvn -B test -Dtest=IdempotencyReliabilityIntegrationTest#retrySameKeyAfterTransientTimeout_returnsRecordedResultWithoutDuplicate
   ```
   *Quan sát:* Lần gọi đầu nhận timeout (TimedOut), lần retry cùng key trả về ngay kết quả đã lưu mà không tạo bản ghi payment hay outbox trùng lặp.
   ```bash
   mvn -B test -Dtest=IdempotencyReliabilityIntegrationTest#tenConcurrentRequestsSameIdempotencyKey_onePaymentAndOneOutboxEvent
   ```
   *Quan sát:* 10 luồng gửi đồng thời cùng một `idempotencyKey`, database deduplication (`ON CONFLICT DO NOTHING`) chỉ tạo duy nhất 1 giao dịch và 1 outbox event.
3. **Đo kiểm hiệu năng tức thì với k6 (45s):**
   ```bash
   STAFF_PASSWORD=interview-demo-pass BASE_URL=http://localhost:8081 k6 run load-tests/k6/flash-sale.js
   ```
   *Quan sát:* 100% checks thành công, p95 latency ~92ms, failure rate 0.00%.

---

### Demo 2: QuanLyKho (2 phút)
1. **Khởi tạo môi trường kiểm thử (30s):**
   Đảm bảo test database PostgreSQL đang hoạt động (ví dụ container `quanlykho-test-postgres-test-1` tại cổng 5433).
2. **Thực thi chu trình Maker-Checker, Phê duyệt PIN và Sổ cái kép (45s):**
   ```bash
   cd QuanLyKho/backend
   .venv/bin/pytest tests/test_approval_controls.py -k "test_demo_pin_is_rejected_outside_demo_mode or test_supervisor_pin_rate_limiting" -v
   ```
   *Quan sát:* Cổng kiểm soát viên từ chối PIN mặc định khi ngoài demo mode (fail-closed); kích hoạt khóa sau 5 lần nhập sai.
   ```bash
   .venv/bin/pytest tests/test_banking_concurrency.py tests/test_banking_double_entry.py tests/test_banking_idempotency.py -v
   ```
   *Quan sát:* 12 workers cộng dồn đồng thời không gây lost-update; chuyển tiền sinh đúng cặp bút toán Nợ/Có cân bằng; saga giữ đúng trạng thái COMPLETED khi nhận trùng key.
3. **Kiểm tra tính bất biến của Audit Trail (15s):**
   ```bash
   .venv/bin/pytest tests/test_audit_chain.py -k "test_tamper_detection or test_orm_listener_rejects_update" -v
   ```
   *Quan sát:* Bất kỳ can thiệp sửa đổi payload hay hash đều bị engine phát hiện gãy chuỗi; lệnh UPDATE/DELETE bị chặn đứng ở mức DB driver.

---

### Demo 3: AKS-SRE-Platform (2 phút)
1. **Xác thực toàn bộ 62 Gate Assertions (30s):**
   ```bash
   cd AKS-SRE-Platform
   python3 tests/test_gates.py
   ```
   *Quan sát:* Toàn bộ G1 đến G16 hiển thị `[ok]`, đặc biệt G10 khẳng định các gate CI không bị thu hẹp hoặc nới lỏng. Kết quả: `62 passed, 0 failed`.
2. **Kiểm tra Policy Gate và GitOps Allowlist (30s):**
   ```bash
   python3 scripts/policy-gate.py
   python3 gitops/validate-gitops.py
   ```
   *Quan sát:* 121 objects Kubernetes duy trì đạt chuẩn; 38 quy tắc ArgoCD không chứa wildcard permissions.
3. **Xác minh SRE Incident Agent & Rollback Invariant (45s):**
   ```bash
   PYTHONPATH=. pytest tests/test_sre_incident_agent.py tests/test_kind_rollback_drill.py -v
   ```
   *Quan sát:* 14 unit test của SRE Agent đạt 86.74% code coverage; kịch bản rollback khôi phục image digest trong 0.105 giây mà không làm gián đoạn hệ thống.

---

## 5. TỔNG KẾT SO SÁNH GIỮA TUYÊN BỐ VÀ BẰNG CHỨNG (Recruiter Evidence Summary)

| Tiêu chí | FlashSale-Backend | QuanLyKho | AKS-SRE-Platform |
|---|---|---|---|
| **Tech Stack Chính** | Java 17, Spring Boot 4, PostgreSQL, Redis, RabbitMQ | Python 3.11, FastAPI, SQLAlchemy 2, PostgreSQL 16 | Kubernetes, ArgoCD, Terraform, Python (SRE Agent) |
| **Trạng thái Kiểm thử** | 187 tests passed (0 failures) | 1,456 tests collected (CI: 1,285 pass, 8 skip, 1 xfail) | 62/62 gate assertions pass, 64/64 pytest pass |
| **Bảo mật & Phân quyền** | OIDC/JWT Fail-Closed, Role RBAC (Customer/Staff/Admin) | Maker-Checker (Invariant A1), Supervisor PIN Rate Limiter | Zero-Trust GitOps, không wildcard RBAC, Read-only SRE Agent |
| **Xử lý Đồng thời** | `ON CONFLICT DO NOTHING`, Idempotent Key Replay | Pessimistic Locks (`FOR UPDATE`), Atomic Saga Compensation | Resource Quotas, HPA / Cluster Autoscaler policies |
| **Toàn vẹn Dữ liệu** | Transactional Outbox + Publisher Confirms + DLQ | Double-Entry General Ledger ($\sum \text{Dr} \equiv \sum \text{Cr}$), Audit Hash Chain | Immutable Image Digest Tagging, Rollback trong 0.105s |
| **Khả năng Tái hiện** | `mvn -B test`, k6 script cục bộ | `poetry run pytest` (harness tmpfs PostgreSQL 16) | `python3 tests/test_gates.py`, local Kind cluster drill |
