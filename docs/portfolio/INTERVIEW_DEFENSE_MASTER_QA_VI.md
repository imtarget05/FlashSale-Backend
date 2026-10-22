# Bộ Câu Hỏi & Kịch Bản Bảo Vệ Phỏng Vấn Kỹ Thuật (Master Interview Defense Q&A)

> **Mục tiêu**: Bộ tài liệu chuẩn bị phỏng vấn cấp độ **Senior / Staff Backend / Cloud & SRE Engineer**.  
> **Cơ sở thực chứng**: Đúc kết từ 5 repositories thực tế (`OpsBridge`, `retail-finance-core-platform`, `QuanLyKho`, `AKS-SRE-Platform`, `SmartDocumentChatbot`).  
> **Yêu cầu Gap Register**: Đóng hoàn toàn mục **G17** & **G19**.

---

## PHẦN 1: HỆ PHÂN TÁN, ĐỒNG THỜI CAO & CONCURRENCY CONTROL

### Câu 1: Làm sao hệ thống Flash-Sale của bạn giải quyết bài toán 10.000 users tranh mua 50 sản phẩm cùng một giây mà không bị Overselling và không làm sập Database?
**Trả lời**:
Tôi áp dụng mô hình **Tiered Architecture (Kiến trúc phân tầng)** kết hợp giữa **Admission Control** và **Authoritative Persistence**:
1. **Tầng 1 — Lọc lưu lượng tức thời (Redis Lua CAS)**: Hàng chục nghìn request đến cùng lúc được chặn lại tại tầng Redis. Tôi sử dụng một đoạn **Lua Script nguyên tử (Check-And-Set)** kiểm tra `if stock >= requested then redis.call('decrby', ...)`. Tác vụ này chạy trong memory với độ trễ $O(1)$ (~29ms p95). 99.5% request đến sau khi hết hàng sẽ bị từ chối ngay lập tức với `409 Conflict (Out of Stock)`, hoàn toàn không chạm đến cơ sở dữ liệu quan hệ.
2. **Tầng 2 — Bất đồng bộ hóa (Async Decoupling)**: 50 request may mắn giữ được suất trên Redis sẽ được trả về ngay `202 Accepted { status: "processing", idempotencyKey: "..." }` và đưa vào Message Queue (`orders` topic). Nhờ đó, thread pool của API không bị nghẽn (sub-50ms admission).
3. **Tầng 3 — Bảo vệ bất biến tại Database (Atomic Conditional SQL)**: Worker background rút message từ Queue và thực thi câu lệnh SQL có điều kiện:
   ```sql
   UPDATE Products 
   SET Stock = Stock - @qty 
   WHERE Id = @id AND Stock >= @qty;
   ```
   Nếu Redis và PostgreSQL đồng bộ, câu lệnh trả về `1 row affected` $\rightarrow$ ghi nhận đơn hàng. Nếu có độ lệch dữ liệu (Stock Drift) khiến số hàng update được là 0, hệ thống ném `StockDriftException` và đẩy ngay vào DLQ để SRE rà soát chứ tuyệt đối không bán lạm.

---

### Câu 2: Tại sao bạn không dùng Distributed Two-Phase Commit (2PC) để đồng bộ giữa Database và Message Broker? Bạn giải quyết bài toán Dual-Write như thế nào?
**Trả lời**:
1. **Nhược điểm chí mạng của 2PC**: Two-Phase Commit (chuẩn XA) đòi hỏi Distributed Coordinator lock các tài nguyên qua nhiều network hops. Theo định lý CAP, 2PC làm sụt giảm nghiêm trọng Availability và Throughput (giảm tới 10 lần), đồng thời dễ dẫn đến blocking deadlock nếu coordinator bị crash giữa Phase 1 (Prepare) và Phase 2 (Commit).
2. **Giải pháp Transactional Outbox**: Thay vì cố gắng ghi vào 2 hệ thống phân tán trong cùng một giao dịch, tôi áp dụng **Transactional Outbox Pattern** ([ADR-019](file:///Users/mainguyenbinhtan/Downloads/OpsBridge/docs/adr/019-transactional-outbox.md)):
   - Bảng `OutboxMessages` nằm ngay trong cơ sở dữ liệu PostgreSQL của dịch vụ.
   - Khi tạo đơn hàng hoặc nhận webhook, việc ghi dữ liệu nghiệp vụ (`Orders` / `GoodsReceipts`) và ghi bản tin sự kiện (`OutboxMessages` trạng thái `Pending`) diễn ra trong **duy nhất một Local ACID Transaction**.
   - Nếu commit thành công, cả hai cùng tồn tại bền vững; nếu lỗi, cả hai cùng rollback $\rightarrow$ triệt tiêu hoàn toàn rủi ro Dual-Write.
3. **Dispatcher độc lập**: Một background worker (`OutboxPublisherHostedService`) định kỳ quét bảng outbox bằng câu lệnh `SELECT ... FOR UPDATE SKIP LOCKED` để tránh lock contention, publish lên RabbitMQ và chỉ đánh dấu `Published` khi nhận được **Publisher Confirms (ACK)** từ broker. Hệ thống chấp nhận ngữ nghĩa **At-Least-Once Delivery** và giao trách nhiệm deduplication cho phía Consumer.

---

### Câu 3: Làm thế nào đảm bảo tính Exactly-Once ở phía Consumer khi Broker chỉ cam kết At-Least-Once Delivery?
**Trả lời**:
Trong hệ phân tán thực tế, không có cái gọi là "Exactly-Once Message Delivery" trên đường truyền mạng. Để đạt được kết quả kinh doanh Exactly-Once (**Effectively Once Execution**), tôi triển khai **Durable Inbox Pattern** tại consumer:
1. Mỗi integration event mang một `event_id` (UUID v4) duy nhất được sinh ra từ nguồn.
2. Khi Consumer nhận message, trước khi thực thi business logic, nó mở transaction và kiểm tra/chèn vào bảng `InboxMessages (EventId PRIMARY KEY)`.
3. Nếu DB báo lỗi vi phạm Unique Constraint (`23505` trên PostgreSQL), Consumer nhận biết đây là message gửi lặp (duplicate delivery), lập tức bỏ qua và gửi `BasicAck` về broker để giải phóng hàng đợi.
4. Đối với các thao tác chuyển trạng thái tài chính (như Saga Checkout hay đối soát Kho), hệ thống sử dụng thêm khóa **Idempotency-Key** do client cung cấp, lưu giữ kết quả của lần chạy đầu tiên để trả về ngay cho các lần gọi lặp lại.

---

## PHẦN 2: THIẾT KẾ DỮ LIỆU, TRANSACTION & TÍNH TOÀN VẸN

### Câu 4: Trong hệ thống tài chính & kho (retail-finance / QuanLyKho), bạn dùng kỹ thuật Concurrency Control nào: Optimistic hay Pessimistic Locking? Khi nào nên dùng cái nào?
**Trả lời**:
Tôi phối hợp cả hai tùy thuộc vào đặc thù tranh chấp (Contention Profile) của từng nghiệp vụ:
1. **Optimistic Locking (Khóa lạc quan qua `@Version` / Concurrency Token)**:
   - *Áp dụng*: Trong sổ cái tài khoản (`Account` trong `retail-finance-core` và `CheckoutSagaState` trong `OpsBridge`).
   - *Cơ chế*: Entity mang thuộc tính số nguyên `Version`. Khi update, câu lệnh SQL sẽ là `UPDATE Account SET Balance = @new, Version = Version + 1 WHERE Id = @id AND Version = @oldVersion`.
   - *Ưu điểm*: Không giữ khóa trên DB, tối ưu cho trường hợp đọc nhiều, ghi ít hoặc tranh chấp thấp. Nếu có xung đột, hệ thống ném `DbUpdateConcurrencyException` để retry hoặc báo lỗi.
2. **Pessimistic Locking (Khóa bi quan `SELECT ... FOR UPDATE`)**:
   - *Áp dụng*: Trong quy trình xuất kho theo lô FEFO (`QuanLyKho/backend/app/api/routes/inventory.py`) và outbox poller.
   - *Cơ chế*: Khi xuất kho nguyên liệu, server thực hiện `SELECT ... FOR UPDATE` trên dòng tồn kho của chi nhánh để khóa cứng bản ghi trong suốt transaction.
   - *Ưu điểm*: Ngăn chặn tuyệt đối tình trạng 2 nhân viên quầy cùng quét xuất một bao trà/hộp sữa tại cùng một thời điểm, loại bỏ rủi ro số dư kho bị âm.

---

### Câu 5: Tại sao bạn chuyển đổi từ Offset Pagination (`LIMIT ... OFFSET`) sang Keyset Pagination trong QuanLyKho và OpsBridge?
**Trả lời**:
1. **Vấn đề hiệu năng của Offset Pagination**: Khi bảng `audit_events` hoặc `inv_balances` lên tới hàng triệu dòng, câu lệnh `OFFSET 100000 LIMIT 50` buộc PostgreSQL phải scan qua 100.050 dòng rồi mới vứt bỏ 100.000 dòng đầu tiên. Độ phức tạp là $O(N)$, gây tốn I/O và nghẽn CPU trầm trọng.
2. **Vấn đề trôi dữ liệu (Data Drift / Inconsistent Window)**: Nếu trong lúc người dùng xem trang 2 mà có dòng mới được chèn vào trang 1, người dùng sẽ bị xem trùng lặp bản ghi ở trang 2.
3. **Giải pháp Keyset Pagination**:
   - Tôi sử dụng cặp khóa `(occurred_at, id)` làm con trỏ (`cursor`).
   - Câu lệnh truy vấn: `WHERE (occurred_at, id) < (@cursor_time, @cursor_id) ORDER BY occurred_at DESC, id DESC LIMIT 50`.
   - Nhờ có B-Tree index trên `(occurred_at, id)`, PostgreSQL nhảy trực tiếp đến vị trí con trỏ trong thời gian $O(\log N)$, tốc độ luôn ổn định dưới 5ms bất kể người dùng duyệt đến trang thứ mấy.

---

## PHẦN 3: ĐỘ TIN CẬY CLOUD NATIVE, KUBERNETES & SRE

### Câu 6: Làm thế nào bạn bảo đảm một đợt nâng cấp Kubernetes Node Pool (Node Drain) không làm rớt bất kỳ đơn hàng Flash-Sale nào đang xử lý?
**Trả lời**:
Quy trình được tôi chuẩn hóa trong Runbook **RB-01** ([runbook-pdb-node-drain-maintenance.md](file:///Users/mainguyenbinhtan/Downloads/AKS-SRE-Platform/docs/runbooks/runbook-pdb-node-drain-maintenance.md)):
1. **PodDisruptionBudget (PDB)**: Workload `order-api` khai báo `minAvailable: 1` (trên tổng số 2 replicas). Kubelet từ chối evict pod nếu hành động đó vi phạm PDB.
2. **Graceful Shutdown Lifecycle**:
   - Khi nhận tín hiệu `SIGTERM`, pod chuyển sang trạng thái unready để Kubernetes Service gỡ IP khỏi Endpoints (ngừng nhận traffic mới).
   - Tiến trình .NET / Python sử dụng `IHostApplicationLifetime` đợi 15 giây để xử lý nốt các in-flight HTTP requests và commit transaction trước khi process thoát.
3. **Thực chứng Drain Drill**: Trong bài test thực tế trên cluster, lệnh `kubectl drain` chuyển giao traffic 100% sang pod replica ở node còn lại với **0 lỗi HTTP 5xx** và **0 rớt kết nối**.

---

### Câu 7: Bạn áp dụng mô hình Zero-Trust mạng (Network Security) trên Kubernetes như thế nào?
**Trả lời**:
Tôi sử dụng **Calico CNI** và triển khai **Default-Deny Ingress & Egress NetworkPolicy**:
1. Mặc định mọi Pod trong namespace bị cô lập hoàn toàn.
2. Chỉ cho phép các luồng giao tiếp được khai báo tường minh:
   - `order-api` chỉ được phép egress ra port 5432 (PostgreSQL) và port 6379 (Redis). Mọi nỗ lực kết nối ra Internet công cộng hoặc sang namespace khác đều bị chặn đứng ở tầng nhân Linux (iptables/eBPF).
   - Ingress vào Database chỉ chấp nhận traffic từ Pod mang label `app: order-api` hoặc `app: worker`.
3. Khi có hành vi vi phạm (ví dụ một container bị mã độc chiếm quyền cố gắng quét cổng nội bộ), Calico lập tức drop packet và kích hoạt cảnh báo Prometheus theo Runbook **RB-03**.

---

## PHẦN 4: QUẢN TRỊ ỨNG DỤNG AI & LLM (AI GOVERNANCE)

### Câu 8: Trong dự án QuanLyKho và SmartDocumentChatbot, bạn tích hợp AI/LLM như thế nào để vừa tận dụng được tính năng thông minh vừa không bị rủi ro ảo giác (Hallucination) hoặc vi phạm nghiệp vụ?
**Trả lời**:
Tôi tuân thủ triệt để nguyên tắc **Advisory-Only & Deterministic Policy Floor** ([ADR 0002](file:///Users/mainguyenbinhtan/Downloads/QuanLyKho/docs/adr/0002-ai-governance-and-advisory-boundaries.md)):
1. **Không trao quyền ghi cho AI**: Mô hình AI tuyệt đối không có quyền tự động phê duyệt xuất kho, chuyển tiền, hay ghi đè cơ sở dữ liệu. Mọi quyết định tài chính đều thuộc về con người (Maker-Checker two-man rule).
2. **Deterministic Fallback**: Nếu mô hình AI (Gemma/OpenAI) bị timeout hoặc trả về kết quả không parse được JSON, hệ thống tự động fallback về luật phân tích thống kê tĩnh (`heuristic_fallback`), không bao giờ để AI block luồng vận hành chính.
3. **Niêm phong băm SHA-256 (Provenance Stamping)**: Để tránh client giả mạo dữ liệu gửi lên model, server tự động load chứng từ từ PostgreSQL, tính hash SHA-256 rồi mới gửi vào pipeline đánh giá rủi ro, bảo đảm tính toàn vẹn chứng từ kế toán.
4. **Circuit Breaker trong RAG (ADR 0003)**: Trong `SmartDocumentChatbot`, luồng Agent gọi mô hình ngoài được bảo vệ bởi Resilience4j `@CircuitBreaker`. Nếu lỗi vượt quá 50%, hệ thống tự động chuyển sang luồng Corrective RAG (CRAG) truyền thống qua PostgreSQL vector search.

---

## PHẦN 5: BẢNG TỔNG HỢP CÁC CON SỐ THỰC CHỨNG (CHEAT-SHEET)

| Chỉ số / Invariant | Con số thực chứng (Evidence Grounded) | Lệnh tái hiện / Chứng minh |
|---|---|---|
| **Flash-Sale Concurrency** | 50 buyers / 10 stock $\rightarrow$ 10 fills, 40 × 409, **0 oversell** | `python3 load-tests/concurrency/test_oversell_demo.py` |
| **Admission SLA** | p95 Latency **< 29 ms** trên Redis Lua reservation | `k6 run load-tests/k6/flash-sale.js` |
| **Disaster Recovery RTO** | **0.1581 giây** phục hồi hoàn toàn trạng thái sổ cái | `docs/evidence/disaster-recovery-drill-PASS.md` |
| **Disaster Recovery RPO** | **0 giây** (Delta balance = `0.0000`, 100% data parity) | `docs/evidence/disaster-recovery-drill-PASS.md` |
| **PDB Node Drain** | **0% packet loss**, **0 dropped requests** khi evict node | `docs/evidence/pdb-drain-drill-PASS.md` |
| **Cloud Billing Guard** | **\$0/tháng** Azure compute sau khi thực thi teardown runbook | `az group exists --name rg-aks-platform-dev` $\rightarrow$ `false` |
