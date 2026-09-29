# Portfolio Architecture Map — 7 Repos × 12 Data Architectures

> **Nguồn spec:** [DATA-ARCHITECTURES.md](./DATA-ARCHITECTURES.md) — 12 kiến trúc Data Pipeline chuẩn.
> **Mục đích:** Bản đồ ánh xạ từng portfolio project vào kiến trúc Data/ML chuẩn, chỉ ra **cái gì đã có**, **cái gì thiếu**, và **cơ hội mở rộng tiếp theo**.
> **Ngày lập:** 2026-09-29 · **Nguồn:** clone sâu 7 repo, main branch, quy mô 1.723 files.

---

## Phần 1 — Portfolio Overview

### 1.1 Bảng tổng quan

| # | Project | Stack chính | Files | Test fns | Lĩnh vực nghiệp vụ |
|---|---------|-------------|-------|----------|---------------------|
| 1 | **CreditFlow** | Python 3.12, FastAPI, React 18, MLflow, LangGraph, XGBoost | 212 | 213 | Rủi ro tín dụng / ngân hàng |
| 2 | **Factory-Data-Automation** | Python, FastAPI, Streamlit, Polars, Ollama Qwen2.5 | 126 | 126 | Data platform sản xuất |
| 3 | **ApexInspect-AI** | Python 3.11, YOLOv8, ONNX Runtime, OpenCV, LangGraph | 187 | 188 | Kiểm tra thị giác / MES |
| 4 | **FlashSale-Backend** | .NET 10, PostgreSQL 16, Redis, RabbitMQ | 347 | — | Concurrency / e-commerce |
| 5 | **MAIA** | Python 3.12, FastAPI, Qdrant, LangGraph, Kafka | 378 | 608 | RAG doanh nghiệp / ITSM |
| 6 | **MiniERP-Manufacturing-Warehouse** | Oracle PL/SQL 23c, ASP.NET Core 8 | 245 | 302 | ERP sản xuất + kho |
| 7 | **Enterprise-IT-Helpdesk-Lab** | Windows Server 2022, AD/DNS/DHCP/GPO, Node.js, PowerShell | 228 | 329 | IT Operations / ITIL |

**Tổng: 1.723 files.** Test function thực đếm được: 1.766 (chưa tính test C#/.NET vì khác syntax).

### 1.2 Nhóm theo miền nghiệp vụ (Data Domain)

```
                    ┌─────────────────────────────────────┐
                    │   SẢN XUẤT / MANUFACTURING (Mesh)   │
                    └─────────────────────────────────────┘
       ApexInspect-AI ──► Factory-Data-Automation ──► MiniERP
        (vision/quality)     (data platform)         (ERP/WMS/PLSQL)
                              ▲            ▲
                              │            │
                         MES sync    warehouse feed
                    ┌─────────────────────────────────────┐
                    │      DỊCH VỤ / ENTERPRISE           │
                    └─────────────────────────────────────┘
        MAIA (RAG/ITSM) ◄──► Enterprise-IT-Helpdesk-Lab (ITSM/ITIL)
                                  ▲
                                  │ ticket dispatch
                                  │
                    ┌─────────────────────────────────────┐
                    │      TÀI CHÍNH / CONCURRENCY        │
                    └─────────────────────────────────────┘
                        CreditFlow (ML risk)   FlashSale (reliability)
```

**Nhận xét chiến lược:** 3 repo cùng miền *sản xuất* — đây là cơ hội lớn nhất để portfolio thể hiện **Data Mesh** (§9) thay vì 3 project rời rạc. **4 repo** dùng chung pattern **event-driven + outbox** (FlashSale, MAIA, ApexInspect, MiniERP) — có thể trích xuất thành một *reference architecture* dùng chung.

### 1.3 Cross-repo pattern tái sử dụng được

| Pattern | Xuất hiện ở | Mức độ trưởng thành | Ghi chú |
|---------|--------------|---------------------|---------|
| **Outbox pattern** (write event + row cùng transaction) | FlashSale, MAIA, MiniERP, ApexInspect | Rất cao | Đã có 3 bản implementation khác nhau — nên chuẩn hoá |
| **Human-in-the-Loop approval** | CreditFlow (LangGraph), MAIA (LangGraph), ApexInspect (LangGraph) | Cao | Cùng framework → dễ trích thành lib dùng chung |
| **Data contract / schema validation** | Factory (app/data_contracts), CreditFlow (pipeline/validation) | Trung bình–cao | 2 hệ khác nhau: pydantic vs custom |
| **Idempotency key** | FlashSale, MAIA, Factory (etl idempotency test) | Cao | |
| **DLQ / Quarantine** | FlashSale (RabbitMQ DLQ), Factory (quarantine CSV) | Cao | Cùng ý tưởng, 2 impl khác format |
| **Drift / monitoring** | CreditFlow (PSI), ApexInspect (trigger engine), Factory (alert system) | Trung bình | Chưa có repo nào có observability chuẩn (Prometheus/Grafana đồng nhất) |
| **C4 docs + ADR** | Cả 7 repo đều có `docs/C4.md` | Cao | Chuẩn hoá rất tốt, giữ nguyên |
| **LLM gateway / local model** | CreditFlow, MAIA, ApexInspect, Factory (`llm-gateway/`) | Cao | 4 bản implementation — nên gộp |


---

## Phần 2 — Bản đồ kiến trúc: Repo nào hiện thực kiến trúc nào

### 2.1 Ma trận coverage (✅ đã có · 🟡 một phần · ❌ chưa có)

| Kiến trúc | CreditFlow | Helpdesk | Factory | ApexInspect | FlashSale | MAIA | MiniERP |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| §1 Batch Pipeline | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| §2 Streaming Pipeline | ❌ | ❌ | ❌ | ❌ | 🟡 | ✅ | 🟡 |
| §3 Lambda | ❌ | ❌ | ❌ | ❌ | ❌ | 🟡 | ❌ |
| §4 Kappa | ❌ | ❌ | ❌ | ❌ | ❌ | 🟡 | ❌ |
| §5 Lakehouse | ❌ | ❌ | 🟡 | ❌ | ❌ | 🟡 | ❌ |
| §6 ELT | 🟡 | ❌ | ✅ | ❌ | ❌ | 🟡 | ✅ |
| §7 Data Lake | ❌ | ❌ | ✅ | ❌ | ❌ | 🟡 | 🟡 |
| §8 Data Warehouse | ❌ | ❌ | 🟡 | 🟡 | 🟡 | 🟡 | ✅ |
| §9 Data Mesh | ❌ | 🟡 | 🟡 | 🟡 | 🟡 | 🟡 | 🟡 |
| §10 Feature Store | 🟡 | ❌ | 🟡 | ❌ | ❌ | ❌ | ❌ |
| §11 ML Training | ✅ | ❌ | 🟡 | 🟡 | ❌ | 🟡 | ❌ |
| §12 Real-time Inference | 🟡 | ❌ | ❌ | ✅ | ✅ | 🟡 | ❌ |


**Tổng điểm:** 13 ✅ · 21 🟡 · 50 ❌ / 84 ô.

### 2.2 Phân tích theo repo

#### 🏦 1. CreditFlow — ML Credit Risk Decision Engine

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | §11 ML Training Pipeline + §1 Batch |
| **Pipeline thật** | `pipeline/`: `data/` → `validation/` → `feature_engineering/features.py` → `modeling/{train,models,evaluate,threshold}` → `monitoring/{drift,drift_offline}` → `agent/` (LangGraph 12-node) → `disbursement/` → `storage/` → `gateways/` → `financial/` |
| **Điểm mạnh** | Chọn model theo **business cost** chứ không accuracy → LogReg thắng XGBoost; threshold 0.20 tối ưu chi phí bất đối xứng (FN=5.0, FP=1.0); MLflow 3.16 registry; **PSI drift detection**; ledger SHA-256 tamper-evident; **Policy Guard** chặn CONFIDENTIAL data ra public cloud |
| **Đã theo §11** | Client → Preprocessing → ML model → Prediction; Ground-truth storage; Evaluation & Monitoring; Feature store (`pipeline/feature_engineering`) |
| **Thiếu** | ❌ **§10 Feature Store chưa tách riêng** — feature nằm trong `feature_engineering/features.py`, chưa có online/offline store, chưa có train/serve consistency check · ❌ §12 real-time serving chưa có priority queue/preemption · ❌ §2 streaming (loan application chỉ batch) |
| **Cơ hội #1** | Trích `pipeline/feature_engineering` thành **Feature Store thật** (offline parquet + online dict, có point-in-time correctness test) → khớp §10 hoàn toàn |

#### 🏭 2. Factory-Data-Automation — Smart Manufacturing Data Platform

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | §7 Data Lake + §6 ELT + §8 Warehouse |
| **Pipeline thật** | `app/etl/`: `pipeline.py` (discover → load → clean → contract-gate → save) → `medallion.py` (silver parquet snappy) → `polars_etl.py` (engine parity) → `quarantine.py` (DLQ) → `kpi_engine.py` (84+ KPI, OEE) |
| **Storage hiện có** | `data/raw/` → `data/processed/` · `data/silver/production.parquet` · `data/quarantine/corrupt_records.csv` · `data/exports/` |
| **Đã theo §7** | ✅ Ingestion (discover CSV/XLSX) · ✅ Storage (landing=raw, clean=quarantine+processed) · ✅ Processing (validate/clean/standardize → transform/KPI) · ✅ Consumption (dashboard + AI report + export) |
| **Thiếu** | ❌ **GOLD/CURATED layer chưa tồn tại** — mới dừng ở silver · ❌ **§7 Cataloging & Search Layer** — chưa có metadata catalog / lineage (`docs/DATA_DICTIONARY.md` là tài liệu tĩnh) · ❌ **§8 staging→marts** — chưa tách marts theo chủ đề (OEE/Quality/Inventory) · ❌ **§2 streaming** — OEE/alert chỉ batch |
| **Điểm mạnh** | Quarantine có thật (không drop âm thầm); data contract ở `app/data_contracts/`; engine parity pandas↔polars có test; 3 lớp ADR |
| **Cơ hội #1 (cao nhất)** | **Thêm gold/curated layer + data catalog + lineage** → hoàn thiện §7 + §8. Đây là repo khớp chủ đề nhất |


#### 🏭 3. ApexInspect-AI — Autonomous Vision & Quality Agent

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | §12 Real-time ML Inference + §1 Batch |
| **Pipeline thật** | `src/vision/` (ONNX + SAHI patching) → `src/backend/` (FastAPI + InspectionService) → `src/agent/` (LangGraph + subagents) → `src/industrial/` (Modbus TCP PLC) |
| **Đã theo §12** | ✅ Edge inference 43.36ms (23.1 FPS) trên ONNX CPU · ✅ Trigger engine (3-consecutive hoặc yield drift >15%) · ✅ HITL supervisor sign-off · ✅ MES sync · ✅ PLC dispatch |
| **Khác §12** | Dùng **synchronous ONNX call**, chưa có pipeline ưu tiên mới (data slicing → dedup → prioritized queues → batching → preemptive async) |
| **Thiếu** | ❌ Priority queue / preemption · ❌ Streaming inference từ camera (hiện pull từng frame) · ❌ Feature store · ❌ **AuditLog KHÔNG tamper-evident** — README tự thừa nhận "append-only by convention, no hash chain". CreditFlow đã có SHA-256 chain → nên port sang. |
| **Điểm mạnh** | Trung thực benchmark (43.36ms có `benchmark_results.json`); PLC simulation-by-default an toàn; Neon PG + SQLite fallback |

#### ⚡ 4. FlashSale-Backend — Reliability Platform

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | §2 Streaming (RabbitMQ) + transactional outbox |
| **Cấu trúc** | Clean Architecture 4 tầng: `FlashSale.Domain` ← `Application` ← `Infrastructure` ← `Order.Api`/`Order.Worker`/`Payment.Service` |
| **Đã theo §2** | ✅ Producer → RabbitMQ (at-least-once) → Worker → PostgreSQL (ack-after-persist) · ✅ DLQ · ✅ Outbox + Saga trong Domain · ✅ Idempotency key |
| **Điểm mạnh** | Zero-oversell invariant **CI-enforced** (50 req/10 stock → đúng 10 sales); Redis Lua CAS 1 RTT; pool cap 80/100; architecture test chặn hướng dependency; backup pg_dump -Fc + SHA-256 |
| **Thiếu** | ❌ Chưa có **event sourcing** đầy đủ · ❌ Chưa có Kafka (RabbitMQ chưa partition mạnh) · ❌ Chưa có data lake/warehouse cho analytics lịch sử flash sale |
| **Cơ hội** | Bổ sung **projection/materialized view** cho analytics (đơn theo giây, conversion funnel) → §8 Warehouse |

#### 🧠 5. MAIA — Enterprise RAG Knowledge Platform

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | §7 Data Lake (marketing corpus) + §2 Streaming (Kafka) + §6 ELT (NL→SQL mini-warehouse) |
| **Cấu trúc** | `src/maia/`: `ingestion.py` + `ingestion_pipeline.py` → `chunking.py` → `vector_store.py` (Qdrant) + `retriever.py` (hybrid RRF) → `reranker.py` → `workflow.py` (LangGraph) · `outbox_scheduler.py` + `outbox_worker.py` · `mcp/` · `promptops/` · `market_api.py` (mini-warehouse) |
| **Điểm mạnh (mạnh nhất portfolio)** | Hybrid RAG dense+BM25 fuse bằng RRF(k=60) · Evidence gate ≥0.3 → **honest refusal** thay vì hallucinate · Citation [S1][S2] truy được về file/section · 2-layer PII guard (ingest + output) · Multi-tenant isolation theo `tenant_id` · **MCP JSON-RPC 2.0 thật** · PromptOps = prompt như versioned artefact có eval · **608 test — nhiều nhất portfolio** |
| **Đã theo §7** | ✅ Ingestion (file/JSONL/CSV + HTTP cursor pagination) · ✅ Storage (Qdrant + SQLite mini-warehouse + run ledger) · ✅ Processing (chunking, dedup) · ✅ Consumption (Streamlit UI, API) |
| **Thiếu** | ❌ Cataloging & Search Layer (§7) — chưa có metadata catalog · ❌ Lineage end-to-end · ❌ Marketing data plane mới có, chưa mở rộng domain khác |

#### 🏢 6. MiniERP — Manufacturing ERP + WMS

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | §8 Data Warehouse (Oracle 3NF) + ELT (12 file SQL) + Outbox |
| **Cấu trúc** | `sql/00..11` (schema → PL/SQL → seed → traceability → RBAC) · `src/` ASP.NET Core 8 API presenter · `dashboard/` vanilla JS + nginx · 31 bảng 3NF, 3 package: `ERP_OPERATIONS`, `ERP_AUTOMATION`, `ERP_TRACEABILITY` |
| **Đã theo §8** | ✅ Data sources (vật tư, BOM, PO, WO) · ✅ Staging/automation package · ✅ Storage 3NF + metadata · ✅ Presentation (dashboard) |
| **Điểm mạnh** | **FEFO + genealogy hai chiều** truy lô thành phẩm → nguyên liệu → nhà cung cấp (compliance thực, hiếm portfolio khác có) · Transaction nguyên tử PL/SQL · PBKDF2 + JWT + RBAC · Helpdesk outbox fail-soft (không rollback nghiệp vụ ERP) |
| **Thiếu** | ❌ Chưa có warehouse/marts cho BI phân tích xu hướng · ❌ Chưa có streaming cho sự kiện sản xuất |

#### 🖥️ 7. Enterprise-IT-Helpdesk-Lab — IT Operations Platform

| Mục | Nội dung |
|-----|----------|
| **Kiến trúc chính** | **Không thuộc data pipeline** — IT infra lab + ITSM app. Ánh xạ gần nhất: §9 Data Mesh (domain = IT Operations) |
| **Nội dung** | 13 docs kiến trúc (VMware topology, AD/DNS/DHCP, GPO matrix, troubleshooting, VLAN/firewall, backup/DR, MiniERP integration) · 20 ticket ITIL đầy đủ triệu chứng→chẩn đoán→nguyên nhân→cách sửa→phòng tránh · 7 PowerShell script · `internal-portal/` Node.js + Python |
| **Điểm mạnh** | Thể hiện **ITSM domain knowledge thực** + networking + security policy (USB block, screen lock, password policy) + backup/DR. Repo chứng minh breadth khác các repo còn lại. |
| **Thiếu** | ❌ Không có data pipeline — nhưng **IT asset data** là một domain data product tiềm năng |
| **Tích hợp** | Đã có `docs/11-minierp-integration.md` — manh mối với MiniERP · `tickets/` là **knowledge base RAG-ready** (nạp được vào MAIA) |

---

## Phần 3 — Bản đồ 12 kiến trúc → nơi hiện thực & nơi nên mở rộng

| § | Kiến trúc | Hiện thực ở | Hướng mở rộng đề xuất | Ưu tiên |
|---|-----------|------------|-------------------------|--------|
| 1 | Batch Pipeline | Cả 7 repo | Đã phủ, không cần | — |
| 2 | Streaming | MAIA (Kafka), FlashSale (RabbitMQ) | **ApexInspect**: camera stream → Kafka → infer; **MiniERP**: sự kiện sản xuất → stream | 🟡 TB |
| 3 | Lambda | MAIA (batch + realtime doc index) | Không cần cố ý build — Kappa đủ cho portfolio | ⚪ Thấp |
| 4 | Kappa | MAIA (Kafka + ingest → serve) | Mở rộng thành **reference architecture** dùng chung | 🟢 TB |
| 5 | Lakehouse | Factory (silver parquet) | **Factory**: parquet → Delta/Iceberg có ACID + time travel | 🟡 TB |
| 6 | ELT | Factory, MiniERP, MAIA (NL→SQL) | **MAIA**: NL→SQL planner mở rộng multi-domain | 🟢 Cao |
| 7 | **Data Lake** | Factory (mạnh nhất), MAIA | **Factory**: thêm **Cataloging & Search Layer** + GOLD zone | 🟢 **Cao nhất** |
| 8 | Data Warehouse | MiniERP (Oracle 3NF), Factory (SQLite) | **Factory**: staging → marts OEE/Quality/Inventory | 🟢 Cao |
| 9 | **Data Mesh** | Mọi repo là "domain" riêng | **3 repo sản xuất** (ApexInspect → Factory → MiniERP) + data contract chung | 🟢 **Cao** |
| 10 | Feature Store | CreditFlow (partial) | **CreditFlow**: tách feature store online/offline + point-in-time test | 🟢 Cao |
| 11 | ML Training | CreditFlow (đầy đủ nhất) | **Factory**: 84 KPI → feature cho OEE prediction | 🟡 TB |
| 12 | **Real-time Inference** | ApexInspect (ONNX sync), FlashSale | **ApexInspect**: prioritized queue + preemptive async + dedup; port hash chain audit từ CreditFlow | 🟢 Cao |

---

## Phần 4 — Roadmap phát triển đề xuất (thứ tự ưu tiên)

### Ưu tiên 1 — Factory-Data-Automation: hoàn thiện Data Lake + Warehouse (§7, §8)

**Bước 1 — GOLD/CURATED layer**
```
data/raw/        (landing)   → giữ nguyên
data/silver/     (clean)     → giữ nguyên, đã có parquet snappy
data/gold/       (curated)   → MỚI: oee_daily.parquet, quality_daily.parquet,
                                inventory_snapshot.parquet, machine_health.parquet
data/catalog/    (metadata)  → MỚI: catalog.json + lineage.json
```
- Module mới: `app/etl/gold.py` (aggregate từ KPI engine → 4 bảng chủ đề)
- Contract riêng cho từng gold table trong `app/data_contracts/`
- Test: `tests/test_gold_layer.py` — idempotent, đúng số dòng, không mất dữ liệu từ silver

**Bước 2 — Data Catalog & Lineage**
- `app/catalog/registry.py`: đăng ký dataset (name, owner, schema, zone landing/silver/gold, freshness SLA)
- `app/catalog/lineage.py`: ghi lại quan hệ raw→silver→gold mỗi lần ETL chạy
- CLI: `python -m app.catalog.registry --list` / `--lineage production`
- `docs/DATA_DICTIONARY.md` sinh tự động từ catalog thay vì viết tay

**Bước 3 — Staging → Marts (§8)**
- `app/database/marts.py`: SQLite materialized views `mart_oee`, `mart_quality`, `mart_inventory`
- Dashboard `app/dashboard/` đọc từ mart thay vì tính lại từ raw

### Ưu tiên 2 — CreditFlow: Feature Store thật (§10)

- `pipeline/feature_store/offline_store.py`: ghi feature theo timestamp (parquet) — phục vụ training
- `pipeline/feature_store/online_store.py`: dict/Redis phục vụ inference
- `pipeline/feature_store/definitions.py`: khai báo feature, nguồn, transform, TTL
- **Điểm then chốt:** `tests/test_feature_store.py` chứng minh **point-in-time correctness** — không rò rỉ feature tương lai vào tập train (lỗi kinh điển gây data leakage)
- `tests/test_train_serve_parity.py`: cùng input → cùng feature vector ở cả 2 store

### Ưu tiên 3 — ApexInspect-AI: nâng real-time inference (§12)

- `src/vision/scheduler.py`: **prioritized queue** (critical defect > lỗi lặp > bình thường)
- `src/vision/dedup.py`: loại frame trùng trong cửa sổ thời gian
- `src/vision/batcher.py`: gom frame đủ batch size → giảm overhead ONNX call
- `src/vision/async_engine.py`: ONNX async + **preemption** khi có frame critical
- Đo lại benchmark → so sánh trước/sau với 43.36ms baseline
- **Port hash chain** từ CreditFlow `src/creditflow/ledger` → `AuditLog` để đạt tamper-evident thật

### Ưu tiên 4 — Data Mesh giữa 3 repo sản xuất (§9)

Thay vì refactor code, tạo **data contract chung** mô tả luồng:

```
ApexInspect-AI                Factory-Data-Automation              MiniERP
  (Vision Domain)      →       (Plant Data Domain)        →      (ERP Domain)
   defect events               OEE/Quality/Inventory           WO/PO/BOM
        │                             │                             │
        └── data contract v1 (JSON Schema) ─────────────────────────┘
                          + data product docs (owner, SLA, schema)
```

- `contracts/inspection-events.v1.json` — contract do ApexInspect sở hữu
- `contracts/factory-kpis.v1.json` — contract do Factory sở hữu
- Mỗi repo có `DATA_PRODUCT.md`: owner, consumers, SLA, schema, versioning policy


---

## Phần 5 — Câu hỏi phỏng vấn dự kiến & câu trả lời dựa trên tài liệu này

| Câu hỏi | Repo trả lời tốt nhất | Điểm bám |
|---------|---------------------|----------|
| "So sánh batch và streaming khi nào chọn gì?" | FlashSale, MAIA | §1 vs §2; FlashSale chọn RabbitMQ vì inventory cần thứ tự |
| "Làm sao không oversell?" | FlashSale | Redis Lua CAS + PG atomic UPDATE + invariant CI-enforced |
| "Xử lý data hỏng thì sao?" | Factory | Quarantine (DLQ) + data contract, không drop âm thầm |
| "Chống hallucination thế nào?" | MAIA | Evidence gate ≥0.3 + citation + honest refusal |
| "Chọn model thế nào?" | CreditFlow | Business cost matrix, không dùng accuracy; LogReg thắng XGBoost |
| "Ai quyết định khi model không chắc?" | CreditFlow, ApexInspect, MAIA | Cả 3 đều LangGraph HITL interrupt |
| "Cân bằng latency vs throughput?" | ApexInspect | 43.36ms; §12 prioritized queue + batching |
| "Có thể thêm domain mới không?" | 3 repo sản xuất | §9 Data Mesh: chuẩn hoá data contract trước, code sau |

---

## Phần 6 — Nguyên tắc đã xác lập (đừng phá vỡ khi phát triển tiếp)

1. **Trung thực về benchmark** — mọi số liệu hiệu năng phải kèm điều kiện đo; nếu chỉ đo local thì ghi rõ "local single run, not a CI gate". FlashSale và ApexInspect đã làm tốt, giữ nguyên.
2. **Không drop dữ liệu âm thầm** — vi phạm contract → quarantine, không `drop()` không ghi log.
3. **Fail-open có kiểm soát** — nếu validator crash thì log `failing_open` (đã làm trong `pipeline.py:304`).
4. **Simulation by default** — ApexInspect `APEX_PLC_MODE=simulation`; không bao giờ biến lỗi ghi hardware thành thành công giả.
5. **Degrade honestly** — thiếu credential thì `dry_run: true` / `delivered: false`, không fake success.
6. **LLM giải thích, không quyết định** — CreditFlow đã có Policy Guard; giữ nguyên nguyên tắc này.
7. **ADR cho mỗi quyết định kiến trúc lớn** — cả 7 repo đều có `docs/adr/`, giữ và bổ sung khi thêm kiến trúc mới.
8. **Test là bằng chứng, không phải để coverage** — ưu tiên test chứng minh invariant (zero-oversell, point-in-time, train/serve parity) hơn test chạy cho có.

---

## Phần 7 — Quy trình làm việc với tài liệu này

| Khi bạn muốn… | Tôi sẽ dùng |
|---------------|-----------|
| Thêm kiến trúc mới vào 1 repo | §3 để xem repo đó đang ở đâu, chọn § còn ❌/🟡, rồi bám roadmap Phần 4 |
| Chuẩn hoá pattern giữa các repo | §1.3 (cross-repo pattern) + Phần 4 Ưu tiên 4 (Data Mesh) |
| Chuẩn bị phỏng vấn | §2.2 (điểm mạnh/thiếu từng repo) + §3 (map kiến trúc) + Phần 5 (câu hỏi) |
| Review code mới | Phần 6 (8 nguyên tắc) làm checklist |
| Onboard người mới vào portfolio | Phần 1 (overview) + §2.1 (ma trận) + DATA-ARCHITECTURES.md |
