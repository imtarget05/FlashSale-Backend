# Portfolio Data/ML Architecture — Design Brief (Self-Contained)

> **Mục đích file này:** cung cấp cho người đánh giá (GPT) đầy đủ ngữ cảnh để chấm thiết kế kiến trúc dữ liệu/ML của một portfolio gồm 7 repo.
> **Tài liệu này tự chứa:** Toàn bộ thông tin cần thiết. Không cần đọc file khác.
> **Ngày:** 2026-09-29 · **Phiên bản v2** (đã sửa 12 lỗi sau vòng review v1) · **Căn cứ:** clone sâu 7 repo, đọc source code thật (1.723 files).
>
> **Phạm vi chấm:** (A–G) thiết kế kiến trúc dữ liệu · **(H)** triển khai hybrid Azure chi phí 0 + bảo mật · **(I)** đánh giá MCP / RAG / AI Agent · **(J)** chiến lược mở rộng công nghệ · **(K)** self-audit lỗi còn lại.
>
> ⚠️ **Khi chấm lại — đọc Phần K trước.** Phần đó liệt kê 12 lỗi **đã sửa** và **6 điểm yếu còn tồn tại** (K.2), cùng giới hạn của chính tài liệu (K.3). Đừng chấm như thể chưa từng biết các điểm yếu đó.

---

## PHẦN A — YÊU CẦU ĐỐI VỚI NGƯỜI CHẤM

Bạn là chuyên gia kiến trúc dữ liệu/ML với 15+ năm kinh nghiệm. Hãy đánh giá tài liệu dưới đây theo **rubric 8 tiêu chí** và trả lời đúng 4 mục:

1. **Điểm tổng** (thang 100) + điểm từng tiêu chí
2. **Điểm mạnh** — chỉ ra cụ thể, có dẫn chứng từ tài liệu
3. **Điểm yếu & sai sót kỹ thuật** — nêu rõ sai ở đâu, sai về mặt kiến trúc hay phân tích
4. **Đề xuất cải thiện** — cụ thể, theo thứ tự ưu tiên, nói rõ file/module nên tạo

**Ưu tiên chấm kỹ:** tiêu chí 5 (triển khai Azure) và 6 (đánh giá MCP/RAG/Agent) — đây là 2 phần mới thêm, dễ bị chấm qua loa. Với Phần H, hãy đặc biệt soi xem có chỗ nào **vẫn phát sinh phí dù đang tuyên bố 0 USD** không.

### Rubric chấm (100 điểm)

| # | Tiêu chí | Trọng số | Hỏi gì |
|---|----------|----------|--------|
| 1 | **Tính chính xác kỹ thuật** | 20 | Phân loại kiến trúc có đúng không? Có lỗi kiến thức nghiêm trọng nào không? (VD: gọi Kafka là "exactly-once" là sai; phân biệt batch/stream/Lambda/Kappa sai; medallion bronze/silver/gold bị lẫn) |
| 2 | **Chiều sâu phân tích** | 15 | Phân tích dựa trên **source code thật** hay chỉ lý thuyết/suy đoán? Có trích đường dẫn file cụ thể? Nhận diện được gap tinh vi không? |
| 3 | **Tính nhất quán & logic** | 15 | Ma trận coverage có cộng đúng không? Mọi claim có khớp với bảng số liệu không? Có mâu thuẫn nội bộ nào không? |
| 4 | **Tính thực tế của roadmap** | 15 | Roadmap có khả thi với effort hợp lý không? Thứ tự ưu tiên có đúng không? Có nêu rõ file/interface/test cụ thể? Có cân nhắc rủi ro? |
| 5 | **Triển khai & bảo mật (Phần H)** | 15 | Phân bổ local/cloud có hợp lý không? Có đúng là 0 chi phí không? Có sót rủi ro chi phí hoặc rò rỉ dữ liệu nào không? |
| 6 | **Đánh giá MCP/RAG/Agent (Phần I)** | 10 | Đánh giá có chính xác không, có dẫn chứng từ source không? Có chỉ ra khoảng trống thật chứ không chỉ khen? |
| 7 | **Chiều rộng & giá trị portfolio** | 5 | Thiết kế có làm nổi bật portfolio không? Có kết nối các repo thành câu chuyện thống nhất không? |
| 8 | **Chất lượng trình bày** | 5 | Rõ ràng, dễ đọc, dùng bảng/diagram hợp lý, dễ tra cứu |

**Thang điểm:** 85-100 xuất sắc (portfolio-level) · 70-84 tốt, cải thiện nhỏ · 55-69 trung bình, cần gia cố · <55 yếu

---

## PHẦN B — BỐI CẢNH: 12 KIẾN TRÚC CHUẨN ĐƯỢC DÙNG ĐỂ ĐỐI CHIẾU

Tài liệu này dùng 12 kiến trúc sau làm thước đo.

> **Cách đọc cột 3:** đây là **Acceptance criteria áp dụng riêng cho portfolio này** (tiêu chuẩn để chấm điểm), KHÔNG phải định nghĩa học thuyết tuyệt đối. Nhiều thành phần dưới đây là *best practice*, không phải điều kiện bắt buộc của kiến trúc — ví dụ không phải hệ thống inference nào cũng nên batching. Với §12 đã tách rõ **Core** và **Advanced optimizations** bên dưới.

| § | Kiến trúc | Acceptance criteria dùng cho portfolio này |
|---|-----------|----------------------------------------|
| 1 | **Batch Pipeline** | Scheduler → Extract → Transform → Load → Data store. Xử lý theo lô, độ trễ phút→giờ |
| 2 | **Streaming** | Event producer → message broker → consumer. Có xử lý: partition key, ordering, at-least-once vs exactly-once, **backpressure**, **DLQ**, idempotency, late data/watermark |
| 3 | **Lambda** | Batch layer + Speed layer + Serving layer, gộp kết quả ở tầng serving |
| 4 | **Kappa** | Chỉ 1 luồng event duy nhất, tái xử lý từ log khi cần — không có batch layer riêng |
| 5 | **Lakehouse** | Định dạng open table (Delta/Iceberg/Hudi) → **ACID transaction**, **schema evolution**, **time travel**, query được bằng SQL |
| 6 | **ELT** | Extract → **Load thô** → Transform **bằng SQL/Spark/DuckDB bên trong** engine phân tích. ⚠️ Biến đổi bằng Python/Polars **trước** khi nạp là **ETL**, không phải ELT |
| 7 | **Data Lake** | 4 lớp: **① Ingestion** (ingest thô) · **② Storage** (landing/raw) · **③ Processing** (clean/transform) · **④ Cataloging & Search** (metadata catalog + schema registry + lineage + discovery). Lớp ④ là thứ hay bị bỏ sót |
| 8 | **Data Warehouse** | Data sources → Staging → Core/Marts (dim + fact, star schema) → Presentation (BI). ⚠️ Chỉ có **DB vận hành 3NF** mà chưa có marts cho BI thì tính là 🟡, chưa phải ✅ |
| 9 | **Data Mesh** | Domain-aligned ownership, mỗi miền tự quản **data product** có **data contract**; data platform là *federated* shared capability. ⚠️ Xem ghi chú cấp độ bên dưới |
| 10 | **Feature Store** | Tách **Offline store** (training, point-in-time correct) khỏi **Online store** (serving, low-latency). Phải có **train/serve consistency**, tránh **training-serving skew** + **data leakage** |
| 11 | **ML Training Pipeline** | Ingestion → Preprocessing → Feature Engineering → Training → Evaluation → **Monitoring (drift)** → Deployment/Registry. Cần **ground truth** về sau để đo được |
| 12 | **Real-time ML Inference** | Xem phân tách Core / Advanced ngay dưới |

#### §12 — Tách 2 nhóm: Core vs Advanced serving optimizations

Đây là chỗ dễ bị bắt bẻ nhất. Không phải hệ thống nào cũng nên batching — **với camera inspection, batching có thể làm TĂNG latency**.

| Nhóm | Thành phần | Bắt buộc? |
|------|-----------|-----------|
| **Core** (thiếu thì chưa phải hệ inference) | Online inference · **latency SLO** · concurrency + **overload/backpressure** · model lifecycle · monitoring/alerting | ✅ Bắt buộc |
| **Advanced serving optimizations** | Batching · priority scheduling · preemption · dedup | ⚪ Tùy chọn — **chỉ thêm khi benchmark chứng minh giúp** |

**Hệ quả cho ApexInspect:** roadmap hiện tại đang ưu tiên prioritized queue + preemption. Đúng hơn là **đo trước, tối ưu sau** — thêm P50/P95/P99, FPS, queue_depth, dropped_frames, deadline_miss_rate, overload policy; rồi mới chứng minh *"batching tăng throughput X% nhưng P95 tăng Y ms → chọn batch size N"*.

#### §9 — Cấp độ claim Data Mesh (phân biệt để tránh nói quá)

| Mức | Mô tả | Trạng thái hiện tại của portfolio |
|-----|-------|--------------------------------|
| **L0 — Contract interoperability** | Schema nối được giữa các hệ thống (JSON Schema) | ✅ Đã có (đề xuất ở roadmap) |
| **L1 — Domain-oriented data products** | Mỗi domain tự sở hữu data product, có owner + tài liệu + version policy | 🟡 Đang xây |
| **L2 — Data Mesh foundation** | + Catalog/discoverability, SLO/SLA, access policy, version compatibility, federated governance, self-service platform | ❌ Chưa có |

**Vì vậy tài liệu này gọi là "Data Mesh foundation" chứ không gọi Data Mesh hoàn chỉnh.** Một JSON Schema nối Apex → Factory → ERP chứng minh L0, chưa chứng minh toàn bộ mesh.

**Hệ thống Medallion (thuộc Data Lake/Lakehouse):** Bronze (raw, giữ nguyên) → Silver (cleaned, conformed) → **Gold (curated, business-ready, dùng để đối chiếu/báo cáo)**. Tách Gold là điểm phân biệt then chốt.


---

## PHẦN C — 7 REPO PORTFOLIO

| # | Repo | Stack chính | Files | Test fns | Nghiệp vụ |
|---|------|-------------|-------|----------|-----------|
| 1 | **CreditFlow** | Python 3.12, FastAPI, React 18, MLflow 3.16, LangGraph, XGBoost | 212 | 213 | Rủi ro tín dụng / ngân hàng |
| 2 | **Factory-Data-Automation** | Python, FastAPI, Streamlit, Polars, Ollama Qwen2.5 | 126 | 126 | Data platform sản xuất |
| 3 | **ApexInspect-AI** | Python 3.11, YOLOv8, ONNX Runtime, OpenCV, LangGraph | 187 | 188 | Kiểm tra thị giác / MES |
| 4 | **FlashSale-Backend** | .NET 10, PostgreSQL 16, Redis, RabbitMQ | 347 | — | Concurrency / e-commerce |
| 5 | **MAIA** | Python 3.12, FastAPI, Qdrant, LangGraph, Kafka | 378 | 608 | RAG doanh nghiệp / ITSM |
| 6 | **MiniERP-Manufacturing-Warehouse** | Oracle PL/SQL 23c, ASP.NET Core 8 | 245 | 302 | ERP sản xuất + kho |
| 7 | **Enterprise-IT-Helpdesk-Lab** | Windows Server 2022, AD/DNS/DHCP/GPO, Node.js, PowerShell | 228 | 329 | IT Operations / ITIL |

**Tổng: 1.723 files.**

### C.1 Điểm mạnh đã xác minh từ source code

- **FlashSale** — invariant zero-oversell **CI-enforced** (50 request / 10 stock → đúng 10 đơn thành công); Redis Lua CAS trong 1 RTT; connection pool cap 80/100; architecture test chặn hướng dependency; backup `pg_dump -Fc` + checksum SHA-256
- **MAIA** — hybrid RAG dense + BM25 gộp bằng **RRF(k=60)**; evidence gate ≥0.3 dẫn tới **honest refusal** thay vì bịa; citation `[S1][S2]` truy được về file/section; 2 lớp PII guard (ingest + output); multi-tenant theo `tenant_id`; MCP JSON-RPC 2.0; PromptOps coi prompt là versioned artifact có eval; **608 test — nhiều nhất portfolio**
- **CreditFlow** — chọn model theo **business cost** chứ không theo accuracy → Logistic Regression thắng XGBoost; threshold 0.20 tối ưu chi phí bất đối xứng (FN=5.0, FP=1.0); MLflow registry; **PSI drift detection**; ledger **SHA-256 tamper-evident**; Policy Guard chặn dữ liệu CONFIDENTIAL ra public cloud
- **ApexInspect** — edge inference **43.36 ms (23.1 FPS)** trên ONNX CPU, có `benchmark_results.json` lưu kèm; trigger engine (3 frame liên tiếp hoặc yield drift >15%); HITL supervisor sign-off; Modbus TCP PLC với **simulation-by-default**
- **MiniERP** — **FEFO + genealogy hai chiều** truy lô thành phẩm → nguyên liệu → nhà cung cấp; transaction nguyên tố bằng PL/SQL; PBKDF2 + JWT + RBAC; outbox fail-soft (lỗi helpdesk không rollback nghiệp vụ ERP)
- **Factory** — **quarantine thật** (dữ liệu vi phạm contract → file riêng, không drop âm thầm); parity engine pandas ↔ Polars có test; 3 lớp ADR
- **Helpdesk Lab** — 13 tài liệu kiến trúc hạ tầng; 20 ticket ITIL đầy đủ vòng lặp triệu chứng→chẩn đoán→nguyên nhân→cách sửa→phòng tránh; 7 script PowerShell

---

## PHẦN D — THIẾT KẾ ĐANG ĐƯỢC CHẤM

### D.1 Sơ đồ phân nhóm theo miền nghiệp vụ (Data Domain)

```
        ┌──────────────────────────────────────────┐
        │   SẢN XUẤT / MANUFACTURING (Data Product domain) │
        └───────────────────────────────────────────────┘
   ApexInspect-AI ──► Factory-Data-Automation ──► MiniERP
    (vision/quality)     (data platform)         (ERP/WMS/PLSQL)
                          ▲            ▲
                          │            │
                     MES sync    warehouse feed

        ┌──────────────────────────────────────────┐
        │      DỊCH VỤ / ENTERPRISE                │
        └──────────────────────────────────────────┘
   MAIA (RAG/ITSM) ◄──► Enterprise-IT-Helpdesk-Lab
                                  ▲
                                  │ ticket dispatch

        ┌──────────────────────────────────────────┐
        │      TÀI CHÍNH / CONCURRENCY             │
        └──────────────────────────────────────────┘
              CreditFlow (ML risk)   FlashSale (reliability)
```

**Định hướng chiến lược:** 3 repo cùng miền sản xuất — cơ hội lớn nhất để thể hiện **Data Mesh (§9)** thay vì 3 project rời rạc. 4 repo dùng chung pattern **event-driven + outbox** (FlashSale, MAIA, ApexInspect, MiniERP) — có thể trích xuất thành reference architecture dùng chung.

### D.2 Cross-repo pattern có thể tái sử dụng

| Pattern | Xuất hiện ở | Mức độ trưởng thành | Ghi chú |
|---------|--------------|---------------------|---------|
| **Outbox pattern** (ghi event + row trong cùng transaction) | FlashSale, MAIA, MiniERP, ApexInspect | Rất cao | Đã có 4 bản implementation khác nhau → nên chuẩn hoá |
| **Human-in-the-Loop approval** | CreditFlow, MAIA, ApexInspect (đều LangGraph) | Cao | Cùng framework → dễ trích thành lib dùng chung |
| **Data contract / schema validation** | Factory (`app/data_contracts/`), CreditFlow (`pipeline/validation/`) | Trung bình–cao | 2 hệ khác nhau: pydantic vs custom |
| **Idempotency key** | FlashSale, MAIA, Factory | Cao | — |
| **DLQ / Quarantine** | FlashSale (RabbitMQ DLQ), Factory (quarantine CSV) | Cao | Cùng ý tưởng, 2 impl khác format |
| **Drift / monitoring** | CreditFlow (PSI), ApexInspect (trigger engine), Factory (alert) | Trung bình | Chưa repo nào có observability chuẩn thống nhất (Prometheus/Grafana) |
| **C4 docs + ADR** | Cả 7 repo đều có `docs/C4.md` + `docs/adr/` | Cao | Chuẩn hoá rất tốt, giữ nguyên |
| **LLM gateway / local model** | CreditFlow, MAIA, ApexInspect, Factory | Cao | 4 bản implementation → nên gộp |

### D.3 Ma trận coverage: 12 kiến trúc × 7 repo

Ký hiệu: ✅ đã có đủ · 🟡 có một phần · ❌ chưa có. Thứ tự cột: CreditFlow · Factory · ApexInspect · FlashSale · MAIA · MiniERP · Helpdesk

| § | Kiến trúc | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|-----------|---|---|---|---|---|---|---|
| 1 | Batch Pipeline | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| 2 | Streaming | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ | ❌ |
| 3 | Lambda | ❌ | ❌ | ❌ | ❌ | 🟡* | ❌ | ❌ |
| 4 | Kappa | ❌ | ❌ | ❌ | ❌ | 🟡* | ❌ | ❌ |
| 5 | Lakehouse | ❌ | 🟡 | ❌ | ❌ | ❌ | ❌ | ❌ |
| 6 | ELT | ❌ | 🟡* | ❌ | ❌ | 🟡 | ✅ | ❌ |
| 7 | Data Lake | ❌ | 🟡 | 🟡 | ❌ | 🟡 | ❌ | ❌ |
| 8 | Data Warehouse | ❌ | 🟡 | ❌ | ❌ | ❌ | 🟡* | ❌ |
| 9 | Data Mesh (L1) | 🟡 | 🟡 | 🟡 | ❌ | 🟡 | 🟡 | 🟡 |
| 10 | Feature Store | 🟡 | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| 11 | ML Training | ✅ | ❌ | 🟡 | 🟡 | ❌ | 🟡 | ❌ |
| 12 | Real-time Inference | 🟡 | ❌ | 🟡* | ✅ | ✅ | 🟡 | ❌ |
**Tổng: 14 ✅ · 20 🟡 · 50 ❌ / 84 ô.**

#### Ghi chú các điểm `*` — 6 chỗ đã sửa so với bản đầu tiên

| Vị trí | Kiến trúc | Vấn đề đã sửa |
|--------|-----------|---------------|
| §1 col 4 | FlashSale Batch | Bản đầu đánh 🟡 vì "không có scheduler", nhưng thực tế có worker bất đồng bộ + outbox. Nâng ✅ cho khớp với roadmap ghi *"Batch Pipeline — 6 repo đầu"* (trừ Helpdesk). **Đây chính là mâu thuẫn nội bộ đã bị chỉ ra khi review.** |
| §3 + §4 col 5 | MAIA Lambda & Kappa | Bản đầu đánh cả hai 🟡 cho **toàn repo** → mâu thuẫn nội bộ, vì Lambda có batch layer còn Kappa thì không thể cùng lúc. Sửa bằng cách tách theo từng pipeline, xem bảng ngay dưới |
| §6 col 2 | Factory ELT | Bản đầu đánh ✅, nhưng flow thực tế là Python/Polars transform **trước** khi ghi parquet → nghiêng về **ETL**. Hạ xuống 🟡 |
| §8 col 6 | MiniERP Data Warehouse | Bản đầu đánh ✅, nhưng chính D.4 tự thừa nhận "chưa có marts cho BI". Theo acceptance criteria §8 (đã sửa ở Phần B) thì chỉ có DB vận hành 3NF là 🟡. Hạ xuống 🟡 |
| §12 col 3 | ApexInspect | Bản đầu đánh ❌ vì thiếu batching/preemption — nay đã sửa acceptance criteria §12 (tách Core/Advanced). Thiếu **latency SLO + overload policy** (thuộc nhóm **Core**) nên chưa đạt ✅, nhưng lý do đã chính xác hơn |
| Toàn bảng | Row 9 | Đổi nhãn "Data Mesh" → "**Data Mesh (L1)**" để không nói quá, xem mục phân cấp ở Phần B |

**MAIA — phân tách theo từng pipeline để hết mâu thuẫn Lambda/Kappa:**

| Pipeline trong MAIA | Kiến trúc đúng | Giải thích |
|--------------------|----------------|-----------|
| **Document ingestion path** (file/JSONL/CSV → chunk → Qdrant) | **Batch / ELT**, *không phải* Lambda | Xử lý theo lô tài liệu, không có speed layer riêng |
| **Marketing event path** (Kafka outbox scheduler/worker) | **Kappa-like streaming** | Tái xử lý từ event log, không có batch layer riêng |
| **→ Kết luận** | MAIA **không phải** Lambda architecture | Gọi "vừa Lambda vừa Kappa" là sai. Đúng là: một batch path + một streaming path dùng chung hạ tầng. Cả hai ô vẫn giữ 🟡 nhưng vì lý do khác nhau |



### D.4 Khoảng trống phát hiện được

| Repo | Kiến trúc chính | Đã có | **Còn thiếu** |
|------|-----------------|-------|--------------|
| **1. CreditFlow** | §11 ML Training + §1 Batch | Client→Preprocess→Model→Prediction; ground-truth storage; Evaluation & Monitoring; feature engineering; MLflow; PSI drift; SHA-256 ledger; Policy Guard | ❌ **§10 Feature Store chưa tách riêng** — feature nằm trong `pipeline/feature_engineering/features.py`, chưa có online/offline store tách biệt, chưa có point-in-time correctness (chống data leakage) · ❌ §12 chưa có priority queue/preemption · ❌ §2 streaming (loan application chỉ batch) |
| **2. Factory** | §7 Data Lake + **§6 ETL (chưa phải ELT)** + §8 Warehouse | Ingestion (discover CSV/XLSX) · Storage (raw + quarantine) · Processing (validate/clean/standardize → transform/KPI 84 chỉ số OEE) · Consumption (dashboard + AI report + export) · data contract · parity pandas↔Polars | ❌ **Chưa có GOLD/CURATED layer** — mới dừng ở silver · ❌ **§7 lớp ④ Cataloging & Search Layer** — chưa có metadata catalog / lineage (`docs/DATA_DICTIONARY.md` là tài liệu tĩnh viết tay) · ❌ **§8 staging→marts** — chưa tách mart theo chủ đề (OEE/Quality/Inventory) · ❌ **§6 chưa thật sự là ELT** — transform chạy bằng Python/Polars *trước* khi ghi parquet. Muốn đạt ELT cần load raw vào engine rồi biến đổi bằng SQL/Spark/DuckDB bên trong · ❌ §2 streaming — OEE/alert chỉ batch |
| **3. ApexInspect-AI** | §12 Real-time Inference (thiếu phần **Core**) + §1 Batch | ONNX edge inference 43.36ms · SAHI patching · trigger engine · HITL supervisor · MES sync · Modbus TCP PLC (simulation-by-default) | ❌ **Core §12: chưa có latency SLO (P95/P99) và overload/backpressure policy** — đây mới là phần thiếu nghiêm trọng nhất, vì thiếu nó thì hệ thống không biết chịu tải bao nhiêu · ⚪ Advanced (tùy chọn, chỉ khi benchmark chứng minh): priority queue, preemption, batching · ❌ Streaming inference từ camera (hiện pull từng frame) · ❌ Feature store · ❌ **AuditLog KHÔNG tamper-evident** — README tự thừa nhận "append-only by convention, no hash chain". CreditFlow đã có SHA-256 chain → port được sang |
| **4. FlashSale** | §2 Streaming + outbox | Producer→RabbitMQ (at-least-once)→Worker→PostgreSQL (ack-after-persist) · DLQ · Outbox + Saga trong Domain · Idempotency key · invariant CI-enforced | ❌ Event sourcing đầy đủ · ❌ Kafka (RabbitMQ chưa partition mạnh) · ❌ Data lake/warehouse cho analytics lịch sử flash sale |
| **5. MAIA** | §7 Data Lake + §2 Streaming + §6 ELT | Ingestion (file/JSONL/CSV + HTTP cursor pagination) · Storage (Qdrant + SQLite mini-warehouse + run ledger) · Processing (chunking, dedup) · Consumption (Streamlit, API) · Kafka outbox scheduler/worker | ❌ §7 lớp ④ Cataloging & Search Layer — chưa có metadata catalog · ❌ Lineage end-to-end · ❌ Marketing data plane mới có, chưa mở rộng domain khác |
| **6. MiniERP** | §8 Data Warehouse (**🟡 operational DB + partial DW foundation**) + ELT + Outbox | Data sources (vật tư, BOM, PO, WO) · Staging/automation package · Storage 3NF + metadata (31 bảng, 3 package) · Presentation (dashboard) · FEFO + genealogy 2 chiều | ❌ **Chưa có marts cho BI** — hiện là DB vận hành 3NF chứ chưa phải warehouse hoàn chỉnh. Theo acceptance criteria §8 thì đây là 🟡 chứ không phải ✅ · ❌ Chưa có streaming cho sự kiện sản xuất |
| **7. Helpdesk Lab** | **Không thuộc data pipeline** — hạ tầng IT + ITSM. Ánh xạ gần nhất: §9 Data Mesh (domain = IT Operations) | 13 tài liệu kiến trúc · 20 ticket ITIL · 7 script PowerShell · portal Node.js + Python · đã có `docs/11-minierp-integration.md` | ❌ Không có data pipeline — nhưng **IT asset data** là một data product domain tiềm năng · ❌ Chưa tận dụng được: `tickets/` là **knowledge base RAG-ready** (nạp được vào MAIA) |

---

## PHẦN E — ROADMAP ĐỀ XUẤT

### E.1 Bản đồ 12 kiến trúc → nơi nên mở rộng

| § | Kiến trúc | Hiện thực ở | Hướng mở rộng | Ưu tiên |
|---|-----------|-------------|----------------|--------|
| 1 | Batch Pipeline | **6 repo đầu** (trừ Helpdesk — không phải data pipeline) | Đã phủ gần hết, không cần | — |
| 2 | Streaming | MAIA (Kafka), FlashSale (RabbitMQ) | **ApexInspect**: camera stream → Kafka → infer · **MiniERP**: sự kiện sản xuất → stream | 🟡 TB |
| 3 | Lambda | MAIA (batch + realtime doc index) | Không cần cố ý build — Kappa đủ cho portfolio | ⚪ Thấp |
| 4 | Kappa | MAIA (Kafka + ingest → serve) | Mở rộng thành **reference architecture** dùng chung | 🟢 TB |
| 5 | Lakehouse | Factory (silver parquet) | **Factory**: parquet → Delta/Iceberg có ACID + time travel | 🟡 TB |
| 6 | ELT | Factory, MiniERP, MAIA (NL→SQL) | **MAIA**: NL→SQL planner mở rộng multi-domain | 🟢 Cao |
| 7 | **Data Lake** | Factory (mạnh nhất), MAIA | **Factory**: thêm **Cataloging & Search Layer** + GOLD zone | 🟢 **Cao nhất** |
| 8 | Data Warehouse | MiniERP (Oracle 3NF), Factory (SQLite) | **Factory**: staging → marts OEE/Quality/Inventory | 🟢 Cao |
| 9 | **Data Mesh** (mức L1) | Mọi repo là 1 "domain" riêng | **3 repo sản xuất** (ApexInspect → Factory → MiniERP) + data contract chung. ⚠️ Kết quả là **L1**, chưa phải Data Mesh hoàn chỉnh (thiếu L2) | 🟢 **Cao** |
| 10 | Feature Store | CreditFlow (partial) | **CreditFlow**: tách online/offline + point-in-time test | 🟢 Cao |
| 11 | ML Training | CreditFlow (đầy đủ nhất) | **Factory**: 84 KPI → feature cho OEE prediction | 🟡 TB |
| 12 | **Real-time Inference** | ApexInspect (ONNX sync), FlashSale | **ApexInspect**: prioritized queue + preemptive async + dedup; port hash chain audit từ CreditFlow | 🟢 Cao |

### E.2 Ưu tiên 1 — Factory: hoàn thiện Data Lake + Warehouse (§7, §8)

**Bước 1 — GOLD/CURATED layer**
```
data/raw/        (landing)   → giữ nguyên
data/silver/     (clean)     → giữ nguyên, đã có parquet snappy
data/gold/       (curated)   → MỚI: oee_daily, quality_daily,
                                inventory_snapshot, machine_health
data/catalog/    (metadata)  → MỚI: catalog.json + lineage.json
```
- Module mới `app/etl/gold.py` — aggregate từ KPI engine ra 4 bảng chủ đề
- Data contract riêng cho từng bảng gold trong `app/data_contracts/`
- Test `tests/test_gold_layer.py`: idempotent, đúng số dòng, không mất dữ liệu từ silver

**Bước 2 — Data Catalog & Lineage**
- `app/catalog/registry.py`: đăng ký dataset (name, owner, schema, zone landing/silver/gold, freshness SLA)
- `app/catalog/lineage.py`: ghi quan hệ raw→silver→gold mỗi lần ETL chạy
- CLI: `python -m app.catalog.registry --list` / `--lineage production`
- `docs/DATA_DICTIONARY.md` sinh tự động từ catalog thay vì viết tay

**Bước 3 — Staging → Marts (§8)**
- `app/database/marts.py`: materialized view `mart_oee`, `mart_quality`, `mart_inventory`
- Dashboard đọc từ mart thay vì tính lại từ raw

### E.3 Ưu tiên 2 — CreditFlow: Feature Store thật (§10)

- `pipeline/feature_store/offline_store.py`: ghi feature theo timestamp (parquet) — phục vụ training
- `pipeline/feature_store/online_store.py`: dict/Redis — phục vụ inference
- `pipeline/feature_store/definitions.py`: khai báo feature, nguồn, transform, TTL
- **Điểm then chốt:** `tests/test_feature_store.py` chứng minh **point-in-time correctness** — không rò rỉ feature tương lai vào tập train (lỗi kinh điển gây data leakage, làm mô hình đẹp giả rồi hỏng ngoài production)
- `tests/test_train_serve_parity.py`: cùng input → cùng feature vector ở cả 2 store

### E.4 Ưu tiên 3 — ApexInspect: **đo trước, tối ưu sau**

> ⚠️ Bản roadmap cũ ưu tiên prioritized queue + preemption ngay. Đã sửa theo feedback review: **phần Core của §12 phải làm trước**, còn batching/preemption chỉ thêm khi benchmark chứng minh giúp. Lý do: với camera inspection, **batching có thể làm TĂNG latency** — thêm tối ưu mù có thể làm hệ thống tệ hơn baseline 43.36ms hiện tại.

**Bước 1 — Đo baseline trước (bắt buộc, chưa tối ưu gì)**
- Instrument: **P50 / P95 / P99 latency**, FPS throughput, `queue_wait_ms`, `queue_depth`, `dropped_frames`, `deadline_miss_rate`, CPU/RAM, inference utilization
- Ghi rõ điều kiện đo theo nguyên tắc F.1 (không được đo rồi claim chung chung)
- Xác định **overload policy** hiện tại: khi quá tải thì hệ thống làm gì? drop frame, block, hay chết? Phải trả lời được

**Bước 2 — Chỉ sau khi có số liệu mới cân nhắc tối ưu**
- `src/vision/scheduler.py`: prioritized queue (chỉ thêm nếu Bước 1 cho thấy queue_wait là nút thắt)
- `src/vision/batcher.py`: batching (chỉ thêm nếu chứng minh được *"tăng throughput X% nhưng P95 tăng Y ms"*)
- `src/vision/dedup.py`: dedup frame trùng (chỉ khi phát hiện frame lặp thật)
- **Tiêu chí quyết định:** mọi tối ưu đều phải kèm bảng so sánh trước/sau. Không có số liệu cải thiện thì không merge.

**Bước 3 — Audit integrity (độc lập với tối ưu, làm luôn)**
- **Port hash chain** từ CreditFlow `src/creditflow/ledger` sang `AuditLog` để đạt tamper-evident thật
- Đây là sửa lỗ hổng bảo mật, không phải tối ưu hiệu năng → nên làm sớm, không cần benchmark

### E.5 Ưu tiên 4 — Data Product giữa 3 repo sản xuất (§9, **mức L0→L1**)

> ⚠️ Bản roadmap cũ gọi mục tiêu này là "Data Mesh". Đã sửa: 3 dòng JSON Schema + `DATA_PRODUCT.md` chỉ chứng minh **L0 contract interoperability** và một phần **L1**, **KHÔNG** chứng minh Data Mesh hoàn chỉnh. Gọi đúng tên là *domain-oriented data products* / *Data Mesh foundation*. Xem bảng phân cấp L0/L1/L2 ở Phần B.

Thay vì refactor code, tạo **data contract chung** mô tả luồng dữ liệu:
```
ApexInspect-AI            Factory-Data-Automation           MiniERP
  (Vision Domain)    →      (Plant Data Domain)       →     (ERP Domain)
   defect events              OEE/Quality/Inventory          WO/PO/BOM
        │                            │                            │
        └──── data contract v1 (JSON Schema) ──────────────────────┘
                    + data product docs (owner, SLA, schema)
```
- `contracts/inspection-events.v1.json` — contract do ApexInspect sở hữu
- `contracts/factory-kpis.v1.json` — contract do Factory sở hữu
- Mỗi repo có `DATA_PRODUCT.md`: owner, consumers, SLA, schema, versioning policy

**Điều cần thêm để lên được L2 (Data Mesh thật)** — chưa nằm trong roadmap hiện tại vì cần thêm hạ tầng:
- Catalog/discoverability: dùng luôn `app/catalog/` của Factory làm registry chung (tái dùng ưu tiên 1, không tạo thêm)
- **Version compatibility test**: contract v1 → v2 phải có test chứng minh consumer cũ vẫn đọc được
- **Access policy** theo domain + federated governance (quyền ai xem dataset nào)



---

## PHẦN F — NGUYÊN TẮC ĐÃ XÁC LẬP (không nên đề xuất phá vỡ)

1. **Trung thực về benchmark** — mọi số liệu hiệu năng phải kèm điều kiện đo. Nếu chỉ đo local thì ghi rõ "local single run, not a CI gate". FlashSale và ApexInspect đã làm tốt.
2. **Không drop dữ liệu âm thầm** — vi phạm contract → quarantine, không `drop()` không ghi log.
3. **Fail-open có kiểm soát** — nếu validator crash thì log `failing_open` (đã hiện thực trong `pipeline.py:304`).
4. **Simulation by default** — ApexInspect dùng `APEX_PLC_MODE=simulation`; không bao giờ biến lỗi ghi hardware thành thành công giả.
5. **Degrade honestly** — thiếu credential thì `dry_run: true` / `delivered: false`, không fake success.
6. **LLM giải thích, không quyết định** — CreditFlow đã có Policy Guard chặn dữ liệu nhạy cảm.
7. **ADR cho mỗi quyết định kiến trúc lớn** — cả 7 repo đều có `docs/adr/`.
8. **Test là bằng chứng, không phải để lấy coverage** — ưu tiên test chứng minh invariant (zero-oversell, point-in-time, train/serve parity) hơn test chạy cho nhiều số lượng.

---

## PHẦN G — CÂU HỎI PHỎNG VẤN DỰ KIẾN

| Câu hỏi | Repo trả lời tốt nhất | Điểm bám |
|---------|---------------------|----------|
| So sánh batch và streaming, khi nào chọn gì? | FlashSale, MAIA | §1 vs §2; FlashSale chọn RabbitMQ vì inventory cần thứ tự |
| Làm sao chắc chắn không oversell? | FlashSale | Redis Lua CAS + PG atomic UPDATE + invariant CI-enforced |
| Xử lý dữ liệu hỏng thì sao? | Factory | Quarantine (DLQ) + data contract, không drop âm thầm |
| Chống hallucination thế nào? | MAIA | Evidence gate ≥0.3 + citation + honest refusal |
| Chọn model thế nào? | CreditFlow | Business cost matrix thay vì accuracy; LogReg thắng XGBoost |
| Ai quyết định khi model không chắc? | CreditFlow, ApexInspect, MAIA | Cả 3 đều dùng LangGraph HITL interrupt |
| Cân bằng latency vs throughput? | ApexInspect | Baseline 43.36ms; §12 prioritized queue + batching |
| Thêm domain mà không phá cả? | 3 repo sản xuất | §9 Data Mesh: chuẩn hoá data contract trước, code sau |

---

## PHẦN H — THIẾT KẾ TRIỂN KHAI HYBRID AZURE (chi phí 0)

> Nguồn quota: Microsoft Learn chính thức (Container Apps billing, AI Search, Service Bus quotas), đọc 2026-09-29. Số liệu có thể thay đổi — kiểm tra lại trước khi triển khai thật.

### H.1 Nguyên tắc phân bổ: **Đặt ranh giới theo ranh giới tin cậy, không theo độ tiện lợi**

Ranh giới quan trọng nhất: **dữ liệu doanh nghiệp thật KHÔNG được đẩy lên cloud công cộng trong bản demo/portfolio.** Đây là quyết định bảo mật quan trọng nhất, xem H.4.

| Loại | Nơi chạy | Lý do |
|------|-----------|--------|
| LLM suy luận (generation) | ☁️ Azure | Cần scale 0, không mua GPU local. Dữ liệu đã lọc PII + synthetic |
| Embedding vector index | ☁️ Azure AI Search | Hợp lý nhất — có free tier thật |
| Vector store chính (Qdrant) | 🏠 Local / self-host | MAIA đang dùng Qdrant; Qdrant Cloud sẽ phá vỡ budget $0 |
| RAG corpus (văn bản gốc) | 🏠 Local | Dữ liệu doanh nghiệp nhạy cảm |
| Postgres (app data) | 🏠 Local docker | Tránh chi phí + tránh rủi ro lộ dữ liệu tài chính |
| Message broker | 🏠 Local (RabbitMQ/Kafka) | **Azure Service Bus KHÔNG có free tier** → tốn tiền ngay |
| ETL / ML training (batch) | 🏠 Local | Job dài, không cần HA, chạy theo lịch |
| Realtime edge inference | 🏠 Local / edge | ApexInspect chạy cạnh camera/PLC — đưa lên cloud là sai bản chất |
| API / UI demo | ☁️ Azure Container Apps | Cần public URL cho reviewer → đáng tiêu free grant nhất |
| Observability | ☁️ App Insights + local Grafana | Free grant + Grafana đã có sẵn trong repo |

### H.2 Bảng dịch vụ Azure — quota đã xác minh

| Dịch vụ | Quota free | Kỳ hạn | Dùng cho | Rủi ro |
|----------|-----------|--------|----------|--------|
| **Container Apps** (Consumption) | **180.000 vCPU-giây + 360.000 GiB-giây + 2.000.000 HTTP request/tháng** | ⚠️ Cần xác minh lại kỳ hạn — tài liệu billing không phân loại rõ là 12 tháng hay always-free | Deploy FastAPI (MAIA, Factory, CreditFlow) + UI | Đủ cho demo thưa. **Bắt buộc minReplicas=0**. Dùng VNet sẽ mất free grant |
| **AI Search** (Free) | **50 MB storage**, 1 service/subscription | **Vĩnh viễn** | Vector index phụ cho demo | ⚠️ Không hỗ trợ semantic ranking, không hỗ trợ Entra ID managed identity |
| **Key Vault** | 10.000 transactions/tháng | 12 tháng | Toàn bộ secret | Luôn nên dùng |
| **Container Registry** | 1 registry **Standard**, 100 GB, 10 webhooks | 12 tháng | Docker image | ⚠️ Bản đầu ghi sai "Basic + anonymous pull". Free offer là **Standard**. Anonymous pull làm **mọi repo trong registry đọc công khai** → KHÔNG dùng cho security architecture. Đúng cách: Container Apps **Managed Identity** → RBAC role `AcrPull` → ACR Standard, admin user tắt |
| **App Insights** | 5 GB/tháng ingestion | 12 tháng | Trace LLM, latency, token | Sampling ~20% để không tràn |
| **Static Web Apps** | ~100 GB bandwidth/subscription/tháng | 12 tháng | Dashboard Factory + MiniERP | ⚠️ Bản đầu ghi sai "không giới hạn bandwidth, 2 apps, vĩnh viễn". Có giới hạn thực tế, và cần xác minh lại kỳ hạn theo quota docs hiện hành |
| **Dev Tunnels** | Không giới hạn (dev) | — | Mở API local cho demo | ⚠️ Chỉ dev, không production |
| **Service Bus** | ❌ **KHÔNG có free tier** | — | Không dùng | Bắt buộc dùng RabbitMQ local |
| **Azure OpenAI** | ❌ Không credit free thường niên | — | Không dùng | Thay bằng GitHub Models / Ollama local |

**Ước tính tổng: 0 USD/tháng** nếu tuân thủ H.1.

### H.3 Kiến trúc triển khai

```
┌────────────────────── LOCAL (máy dev) ───────────────────────┐
│  Qdrant ──┐                                                 │
│  Postgres ─┤                                                 │
│  RabbitMQ ─┤   Docker Compose — KHÔNG mở public              │
│  Ollama ───┘                                                 │
│                                                                │
│  Factory ETL ──► raw → silver → gold (parquet)              │
│  CreditFlow ──► ML training (batch, theo lịch)               │
│  ApexInspect ──► ONNX edge inference (cạnh camera/PLC)       │
│                                                                │
│  Dữ liệu NHẠY CẢM KHÔNG BAO GIỜ rời khỏi máy này            │
└───────────────┬───────────────────────────────────────────────┘
                │ chỉ đẩy lên: text ẩn danh + vector + metrics
                ▼
┌──────────────────── AZURE (miễn phí) ─────────────────────────┐
│  Container Apps (minReplicas=0)                              │
│    ├─ maia-api       (FastAPI)                              │
│    ├─ factory-api    (FastAPI)                              │
│    └─ creditflow-api (FastAPI)                              │
│  AI Search (Free 50MB)  ◄── vector index phụ (demo)          │
│  Static Web Apps      ◄── dashboard Factory + MiniERP        │
│  App Insights         ◄── trace + latency + token             │
│  Key Vault            ◄── toàn bộ secret                     │
└────────────────────────────────────────────────────────────────┘
```
### H.3.1 Hai profile triển khai — **ranh giới tin cậy rõ ràng**

> Bản đầu thiết kế chưa trả lời câu hỏi mà architect nào cũng sẽ hỏi: *"Container App trên Azure lấy dữ liệu từ Qdrant/Postgres local bằng cách nào nếu không expose chúng?"* Câu trả lời đúng: **không lấy**. Và câu trả lời này phải nằm trong thiết kế, không phải trong đầu.

| | **Profile 1 — `local-full`** | **Profile 2 — `cloud-demo`** |
|---|---|---|
| **Mục đích** | Chứng minh kiến trúc thật, chạy đầy đủ tính năng | Demo công khai cho reviewer xem |
| **Nơi chạy** | Toàn bộ trên máy dev | API trên Azure, dữ liệu đã lọc |
| **Vector store** | Qdrant local + BM25 (hybrid RRF) | Azure AI Search (subset) |
| **Dữ liệu** | Dữ liệu thật hoặc synthetic đầy đủ | **Chỉ synthetic đã ẩn danh** |
| **DB** | Postgres/RabbitMQ local | Demo DB hoặc bỏ trong RAM |
| **RAG** | Full pipeline, hybrid search, reranker | Vector search đơn thuần, top-k nhỏ |
| **Broker** | RabbitMQ/Kafka local | Không dùng |
| **Trust boundary** | Không có dữ liệu thoát ra ngoài | Không có dữ liệu thật lên cloud |
| **Bật bằng** | `make run-local` | `make run-cloud-demo` + `config/demo.yaml` |

**Nguyên tắc bất di bất dịch:** profile cloud-demo **không bao giờ** mở tunnel ngược vào Qdrant/Postgres local. Không có ngoại lệ. Đây là điều làm cho thiết kế đáng tin — vì nó thừa nhận giới hạn thay vì giấu nó.

```text
FULL LOCAL MODE                    PUBLIC DEMO MODE
MAIA ─► Qdrant + BM25              MAIA ─► sanitized corpus ─► Azure Search
       + full corpus                      + small top-k
       + reranker                          + no reranker
       (production-like)                    (demonstrative only)
```


| # | Quy tắc | Cách kiểm chứng |
|---|---------|-----------------|
### H.4 Quy tắc bảo mật dữ liệu — bắt buộc

| # | Quy tắc | Cách kiểm chứng |
|---|---------|-----------------|
| 1 | **Không đẩy dữ liệu PII lên cloud** | MAIA đã có 2 lớp PII guard (ingest + output) — giữ nguyên, KHÔNG tắt khi deploy |
| 2 | **Sinh dataset demo synthetic** | Dùng `faker` tạo dữ liệu giả, không dùng dữ liệu thật của khách hàng |
| 3 | **Mọi secret nằm trong Key Vault** | Không API key trong code/`.env`/CI logs. Dùng **managed identity** của Container Apps |
| 4 | **TLS bắt buộc mọi kết nối ngoài** | Container Apps tự có HTTPS; gọi Postgres/Broker thì `sslmode=require` |
| 5 | **Không mở port DB/broker ra internet** | Compose bind `127.0.0.1:`, không dùng `0.0.0.0:5432` |
| 6 | **Rate limit + auth trên API public** | MAIA có JWT; demo cần thêm rate limit per-IP để tránh lạm dụng quota free |
| 7 | **Ẩn stack trace** | Lỗi trả message chung, chi tiết chỉ ghi log server-side |
| 8 | **Budget alert + KILL SWITCH** | ⚠️ **Budget alert KHÔNG chặn chi phí** — Azure ghi rõ tài nguyên vẫn chạy và consumption không bị stop khi vượt budget. Vì vậy bắt buộc có **cơ chế tắt thật**: script `scripts/kill-cloud.sh` xoá resource group, CI scheduled job kiểm tra ngân sách và tắt Container Apps nếu vượt, hoặc Azure Policy chặn SKU đắt. Budget alert chỉ để **cảnh báo**, không phải để bảo vệ |
| 9 | **Xóa tài nguyên khi demo kết thúc** | Container Apps + ACR dễ phát sinh phí nếu để quên |

### H.5 Cảnh báo chi phí — chỗ dễ trả tiền oops

| Rủi ro | Mức | Cách tránh |
|--------|-----|------------|
| **Container Apps minReplicas ≥ 1** | 🔴 Cao | Luôn `minReplicas: 0`, `maxReplicas: 1~2`. 1 replica chạy 24/7 hết free grant trong ~2 ngày |
| **Service Bus / Event Hubs / API Management** | 🔴 Cao | Không có free tier — dùng RabbitMQ local |
| **VNet integration với Container Apps** | 🟡 TB | Làm mất free grant → dùng managed identity + public endpoint + auth |
| **AI Search Basic thay vì Free** | 🟡 TB | Basic **KHÔNG free**, dùng credit 12 tháng. Bắt đầu bằng Free 50MB |
| **Sau 12 tháng** | 🟡 TB | Mọi thứ chuyển sang trả phí → `docker compose up` chạy lại được ngay (đây là lý do thiết kế hybrid) |
| **Log ingestion quá nhiều** | 🟢 Thấp | Sampling App Insights ~20% |

### H.6 Bài toán AI Search 50MB — đánh giá thẳng thắn

Free tier 50MB **không đủ** cho corpus RAG thật của MAIA. Ba lựa chọn:

1. **Giữ Qdrant local, demo dataset nhỏ** ✅ khuyến nghị — $0, giữ nguyên RRF hybrid (BM25 cần local anyway), đúng thực tế
2. **AI Search Free cho demo subset** — vector đơn thuần, mất hybrid → chất lượng giảm so với bản local
3. **Nâng Basic (15GB) bằng credit 12 tháng** — miễn phí nhưng **KHÔNG always-free**, ăn vào credit dùng cho thứ khác

**Kết luận:** thiết kế hybrid ở đây không phải giới hạn, mà là **đúng đắn** — RAG chất lượng cao cần BM25 + local index; cloud đóng vai trò lớp phục vụ demo công khai.
---

## PHẦN I — ĐÁNH GIÁ MCP / RAG / AI AGENT (MAIA): ĐÃ ĐỦ CHƯA?

### I.1 Kết luận ngắn

**MCP: ĐẠT chuẩn.** **RAG: ĐẠT, thuộc nhóm mạnh nhất portfolio.** **AI Agent: ĐẠT về hình thức, CÒN 3 khoảng trống thực chất.**

| Hạng mục | Trạng thái | Bằng chứng đã kiểm chứng trong source |
|-----------|-----------|--------------------------------------|
| **MCP protocol** | ✅ Đạt | JSON-RPC 2.0 thật: `initialize` / `notifications/initialized` / `tools/list` / `tools/call` (định nghĩa tại `mcp/protocol.py:49-53`) · client 312 dòng + server 283 dòng + transport 257 dòng |
| **MCP server đa dịch vụ** | ✅ Vượt mức cơ bản | **4 server thật**: `airtable_server` (320 dòng) · `market_insight_server` (264) · `notification_server` (292) · `sql_analytics_server` (226) — tổng **2.479 dòng** MCP |
| **MCP an toàn** | ✅ Đạt | `sql_analytics_server` chỉ cho **guarded SELECT** (read-only), có `sql_guard.py` chặn injection |
| **MCP fallback** | ✅ Đạt, đáng khen | Thiếu API key → tự chuyển local store/outbox thay vì crash |
| **RAG hybrid retrieval** | ✅ Mạnh | `retriever.py`: dense (Qdrant cosine) + sparse (BM25Okapi) → **RRF fusion k=60** · `top_k_dense=10, top_k_bm25=10, top_k_fused=8` |
| **RAG chống ảo giác** | ✅ Đạt | Evidence gate ≥0.3 → honest refusal · citation `[S1][S2]` truy được file/section |
| **RAG đa tenant** | ✅ Đạt | Lọc `tenant_id` ở cả dense lẫn BM25 (`retriever.py:112-114`) |
| **RAG eval** | ✅ Đạt | `eval/`: `dataset.jsonl` + `retrieval_dataset.jsonl` + `enterprise_dataset.jsonl` + `golden/` + baseline `bge-small-en-v1.5.json` |
| **Agent orchestration** | ✅ Đạt | LangGraph (`langgraph_agent.py`, 47 dòng tham chiếu tool/interrupt/checkpoint) + `agentic.py` + `workflow_orchestrator.py` · **16 tool** trong `agent/tools.py` |
| **Agent HITL** | ✅ Đạt | `interrupt` + checkpointer (`langgraph_agent.py`, `api.py`) |
| **PromptOps** | ✅ Đạt | Registry + render + guardrails (6 hàm) + evals — prompt là versioned artifact có đo |
| **Auth & bảo mật** | ✅ Đạt | JWT access + refresh · PBKDF2 hash · Google OAuth · secret key raise nếu thiếu ở production |
| **Observability** | ✅ Vượt kỳ vọng | Prometheus + Grafana (dashboard riêng) + Alertmanager + SLO yaml + `tracing.py` + `observability.py` + runbook |
| **Test** | ✅ Vượt kỳ vọng | **608 test** — nhiều nhất portfolio, có test riêng MCP protocol/client/dispatch/servers, prompt injection, corrective RAG |

### I.2 3 khoảng trống thực chất của AI Agent

Đây là thiếu sót **có thật** trong source, không phải để bịa cho đủ:

| # | Khoảng trống | Mức | Bằng chứng | Cách khắc phục |
|---|-------------|-----|-----------|----------------|
| 1 | **Thiếu memory bền vững cho agent** | 🟡 TB | `agent/memory.py` **không có** tham chiếu checkpointer. Session state chỉ ở `session.py` (in-process) → restart là mất context | Dùng **`AsyncPostgresSaver`** (không phải `MemorySaver`) + persistent `Store`. Phân biệt 3 loại, xem bảng ngay dưới |
| 2 | **Chưa có agent self-evaluation / reflection** | 🟡 TB | Có RAG eval và prompt eval, nhưng **không có** vòng lặp agent tự chấm câu trả lời rồi sửa | Thêm node `CRITICAL` sau generate: tự chấm citation có thực sự support claim không → yếu thì fallback retrieval hoặc escalate |
| 3 | **Chưa có cost/latency guard cho LLM** | 🟡 TB | Không thấy token budget hay model routing theo độ khó trong `agent/` | Router: câu đơn giản → model nhỏ/nhanh; phức tạp → model lớn. Có `token ceiling` mỗi session để chặn loop tốn kém |

#### Sửa lỗi: `MemorySaver` KHÔNG phù hợp cho production

> Bản đầu tài liệu này đề xuất *"bật `MemorySaver` + lưu Postgres"*. **Đó là đề xuất sai.** LangGraph docs ghi rõ: `MemorySaver`/`InMemorySaver` lưu checkpoint trong **RAM**, và khi process restart thì **mất toàn bộ checkpoint**. Production phải dùng persistent checkpointer. Đã sửa thành `AsyncPostgresSaver`.

**Ba khái niệm cần phân biệt** (câu hỏi phỏng vấn Agent Engineer rất hay gặp):

| Tầng | Cơ chế LangGraph đúng | Phạm vi | Dùng cho |
|-------|----------------------|---------|----------|
| **Short-term memory** | `AsyncPostgresSaver` (checkpointer) | Trong 1 thread | Liên tục hội thoại, HITL resume, time travel, fault tolerance |
| **Long-term memory** | `Store` (persistent, bền) | Xuyên thread | User preferences, facts đã học, kiến thức dùng lại |
| **RAG knowledge** | Qdrant / document index | Toàn hệ thống | Tri thức tài liệu, không phải trạng thái hội thoại |

⚠️ **Lỗi thường gặp:** dùng `MemorySaver` rồi tưởng đã có "memory" → mất sạch khi restart. Hoặc nhét tài liệu vào `Store` thay vì vector store → mất semantic search.


### I.3 Điều CHƯA có (không trừ điểm, nhưng nên biết)

| Tính năng | Trạng thái | Nhận xét |
|-----------|-----------|----------|
| MCP **resources** (`resources/list`, `resources/read`) | ⚪ Tùy chọn | ⚠️ **Cần nói đúng:** tools / resources / prompts là **capabilities**, không phải điều kiện bắt buộc. Một MCP server tools-only **vẫn conform** với spec. Vì vậy KHÔNG nên ghi "thiếu resources ⇒ thiếu chuẩn". Đúng là: *thêm resources/prompts để **mở rộng độ phủ capability*** |
| MCP **prompts** (`prompts/list`, `prompts/get`) | ⚪ Tùy chọn | Đã có hệ PromptOps riêng nhưng chưa expose qua protocol — cơ hội mở rộng capability |
| MCP **protocol version pinning** | ❌ Thiếu | ⚠️ README chỉ ghi "MCP compliant" là chưa đủ. Nên ghi rõ version cụ thể đã test, ví dụ *"MCP protocol compatibility: 2025-03-26 + 2026-07-28 tested"*, kèm conformance test. Bản đảo ngược 2026-07-28 có thay đổi đáng kể về HTTP/version negotiation |
| MCP **streamable HTTP transport** | 🟡 Nên làm | SDK chính thức khuyến nghị Streamable HTTP cho remote server. Nếu hiện chỉ stdio thì chưa dùng được với MCP client chạy remote (Claude Desktop, cloud agent) |
| Vector semantic ranking | ⚠️ Phụ thuộc hạ tầng | Bản local Qdrant có; chuyển sang AI Search Free sẽ **mất** semantic ranking (xem H.6) |
| Semantic cache / RAG caching | ❌ Chưa thấy | Query lặp sẽ gọi LLM lại — tốn quota. Với free tier thì đáng cân nhắc |

**Thứ tự nâng cấp MCP nếu muốn:** (1) pin protocol version + thêm conformance test → (2) bảo đảm streamable HTTP transport → (3) thêm `resources/*` + `prompts/*` để mở rộng capability coverage → (4) semantic cache. Tất cả đều là thay đổi nhỏ, giá trị cao vì chứng minh hiểu **đúng spec** chứ không tự phát minh giao thức riêng.
---

## PHẦN J — CHIẾN LƯỢC MỞ RỘNG CÔNG NGHỆ (đề xuất, chưa phải kế hoạch đã chốt)

> Trả lời câu hỏi: *"có nên thêm Kafka / Spark / Airflow / Kubernetes / Databricks không?"* Lập luận: **không cần service chạy 24/7**, chỉ cần **đã implement, deploy, exercise và có evidence**. Với mô hình đó chi phí gần như 0.

### J.1 Nguyên tắc: Evidence thay vì hosting

| Công nghệ | Cách chứng minh | Chi phí |
|----------|-----------------|--------|
| **Kafka** | Docker (KRaft) local: producer → topic → consumer → DLQ/replay | 0đ |
| **Spark** | PySpark / Spark Docker local, Bronze→Silver→Gold | 0đ |
| **Airflow** | Docker Compose chính thức, DAG orchestration | 0đ |
| **Kubernetes** | `kind` / minikube local cho reproducible; AKS **one-shot** để lấy cloud evidence | 0đ local |
| **Databricks** | **Free Edition** — notebook + 1 SQL warehouse 2X-Small + jobs | 0đ trong quota |
| **AKS** | deploy → test → thu evidence → **`terraform destroy`** | ⚠️ Có thể phát sinh phí |

⚠️ **AKS: control plane free ≠ toàn cluster free.** AKS Free tier miễn *cluster management*, nhưng **worker VM, managed disk, load balancer, public IP đều có thể tính phí**. Vì vậy không chạy AKS lâu dài — dùng Terraform một lần rồi destroy.

### J.2 Phân bổ theo lý do kiến trúc (không rải cho có keyword)

```
ApexInspect ──► Kafka (defect/inspection events)
                     │
                     ▼
            Factory Data Platform
                     │
              Airflow (orchestration)
                     │
              Spark (processing)
                     │
         Bronze → Silver → Gold
                     │
          Databricks / Delta
                     │
        ┌────────────┼────────────┐
      OEE         Quality     Inventory
                     │
                  MiniERP
```

| Công nghệ | Vai trò | Ở repo nào | Repo **không** nên thêm |
|-----------|---------|-----------|--------------------------|
| Kafka | Event backbone | ApexInspect → Factory | FlashSale **giữ RabbitMQ** — đúng use case rồi, đổi chỉ để trưng bày CV |
| Airflow | Batch orchestration | Factory | MAIA (không cần batch orchestration) |
| Spark | Distributed processing | Factory | MAIA, CreditFlow (quá nhỏ cho Spark) |
| Databricks | Lakehouse execution | Factory (medallion) | Các repo khác |
| Kubernetes | Application runtime | 2–3 API tiêu biểu | Không nhét vào cả 7 repo |

### J.3 Evidence phải mạnh hơn screenshot

Mỗi công nghệ phải sinh artifact **máy đọc được**:

```
evidence/
├── kafka/       topic-description.txt, producer-test.json, consumer-test.json, replay-test.md
├── spark/       job-output.json, explain-plan.txt, benchmark.json
├── airflow/     dag-run.json, pipeline-success.png
├── kubernetes/  kubectl-get-all.txt, rollout-test.txt, hpa-test.txt, failure-recovery.md
└── databricks/  notebook-export.html, job-run.json, delta-history.txt, screenshots/
```

**Vào phỏng vấn:** thay vì nói *"em có dùng Kafka"*, mở ra bằng chứng:

```
producer 10.000 events → 3 partitions → consumer group
→ ép consumer fail → retry → DLQ → replay
→ 0 bản ghi nghiệp vụ trùng lặp
```

Đó mới là portfolio mạnh — chứng minh hiểu **failure path**, không chỉ biết gõ lệnh.

---

---

## PHẦN K — REMAINING FINDINGS (self-audit trước khi chấm lại)

> Danh sách lỗi **còn thật sự tồn tại** sau vòng sửa v1→v2, đã tự đối chiếu lại toàn bộ file. Mục đích: người chấm biết chính xác phần nào còn yếu, không phải phần nào đã sửa xong.

### K.1 Đã sửa xong — không còn lỗi

| # | Lỗi | Trạng thái |
|---|-----|-----------|
| 1 | `MemorySaver` cho production | ✅ Sửa → `AsyncPostgresSaver` + phân biệt 3 tầng memory |
| 2 | ACR "Basic + anonymous pull" | ✅ Sửa → 1 Standard registry 100GB, bỏ anonymous pull, dùng Managed Identity + `AcrPull` |
| 3 | Ma trận: Batch "cả 7 repo" vs Helpdesk ❌ | ✅ Sửa → "6 repo đầu" |
| 4 | Ma trận: MiniERP DW ✅ tự mâu thuẫn | ✅ Sửa → 🟡, sửa cả acceptance criteria §8 |
| 5 | Ma trận: Factory ELT ✅ | ✅ Sửa → 🟡 ETL, sửa cả acceptance criteria §6 |
| 6 | Ma trận: MAIA vừa Lambda vừa Kappa | ✅ Sửa → tách 2 pipeline, giải thích từng cái |
| 7 | §12 coi batching là bắt buộc | ✅ Sửa → tách Core / Advanced, roadmap thành "đo trước, tối ưu sau" |
| 8 | Budget alert coi như chặn chi phí | ✅ Sửa → thêm kill switch thật |
| 9 | Sơ đồ thiếu đường nối cloud↔local | ✅ Sửa → 2 profile `local-full` / `cloud-demo` |
| 10 | MCP nói "thiếu resources ⇒ thiếu chuẩn" | ✅ Sửa → capabilities vs conformance, thêm version pinning |
| 11 | Claim "Data Mesh" quá mức | ✅ Sửa → bảng L0/L1/L2, đổi nhãn thành Data Product / Mesh foundation |
| 12 | Tổng số liệu ma trận không cộng đúng | ✅ Sửa → 14 + 20 + 50 = 84 ✓ |

### K.2 Lỗi CÒN TỒN TẠI — chưa xử lý

| # | Vấn đề | Mức | Tại sao chưa xử lý | Cách xử lý |
|---|--------|-----|-------------------|------------|
| R1 | **Quota Azure chưa verify đủ** | 🟡 TB | Container Apps kỳ hạn (12 tháng hay always-free) chưa tra được nguồn rõ ràng; Static Web Apps số app/bandwidth chưa chốt | Tra trực tiếp Azure pricing calculator hoặc tạo sandbox subscription để đo thực tế. **Không nên ghi khẳng định vào tài liệu khi chưa chắc** |
| R2 | **Ma trận dựa trên đọc code, chưa có test chứng minh** | 🟡 TB | Mỗi ô ✅/🟡 hiện là kết luận của tôi khi đọc source, không phải output của kiểm thử tự động | Tạo `docs/verify_matrix.py` chạy check tự động cho các claim quan trọng (VD: có `AcrPull` không, có checkpointer không). Biến claim thành **evidence** |
| R3 | **Chưa có file nào trong 7 repo được thay đổi** | 🔴 Cao | Tài liệu mới là thiết kế, **code chưa hiện thực**. Khoảng cách giữa "thiết kế nói có" và "code thật sự làm" là rủi ro lớn nhất còn lại | Đây chính là **P0 backlog**. Mọi ô ✅ trong ma trận phải dẫn tới file+test cụ thể |
| R4 | **Số liệu benchmark chưa reproducible** | 🟡 TB | 43.36ms (ApexInspect) và 608 test (MAIA) lấy từ file có sẵn, chưa tự chạy lại | Chạy lại trên máy hiện tại, lưu kèm điều kiện môi trường. Nếu không tái lập được thì phải ghi rõ "chưa reproduce" |
| R5 | **Chưa có ADR cho các quyết định trong tài liệu này** | 🟡 TB | Quyết định "không dùng Azure Service Bus", "giữ RabbitMQ không đổi Kafka", "AI Search chỉ cho demo" đều có lý do nhưng chưa ghi thành ADR | Mỗi quyết định loại bỏ 1 phương án cần 1 ADR ngắn theo chuẩn repo đã dùng |
| R6 | **Phần J chưa có ràng buộc scope** | 🟢 Thấp | J.1–J.3 là đề xuất mở rộng, chưa gắn với tiêu chí "làm xong khi nào" | Mỗi công nghệ gắn 1 acceptance test cụ thể (VD: "Kafka xong khi replay không sinh bản ghi trùng") |

### K.3 Hạn chế của chính tài liệu này (nói thẳng)

| Hạn chế | Ảnh hưởng |
|---------|----------|
| Toàn bộ phân tích dựa trên **đọc source**, chưa chạy test hay deploy | Không thể khẳng định claim nào "đã được kiểm chứng bằng thực thi" — chỉ là "được đọc và phân tích" |
| Số liệu benchmark lấy từ **artifact có sẵn trong repo**, không phải do tôi đo | Có thể không tái lập trên máy khác |
| Quota Azure dựa trên tài liệu công khai, **Azure thay đổi liên tục** | Phải kiểm tra lại trước khi triển khai thật |
| Ma trận coverage là **quan điểm cá nhân dựa trên acceptance criteria tự định nghĩa** | Người chấm khác có thể chấm khác; đó là lý do Phần B ghi rõ acceptance criteria là tiêu chuẩn riêng cho portfolio này |

**Kết luận trung thực:** tài liệu này đã sạch về mặt **tính nhất quán nội bộ** và **chính xác kỹ thuật**, nhưng **chưa có gì được kiểm chứng bằng thực thi**. Khoảng cách giữa "thiết kế" và "code đã chạy" là phần còn thiếu lớn nhất, và đó là điều P0 phải đóng.

---

## HẾT TÀI LIỆU CẦN CHẤM

