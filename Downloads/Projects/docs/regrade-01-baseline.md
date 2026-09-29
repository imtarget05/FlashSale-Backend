# REGRADE #1 — DESIGN-ONLY BASELINE

> **File chấm:** `DESIGN-BRIEF-v2-frozen.md` (647 dòng)
> **SHA-256:** `f2a7c054b85f34961091401da8881b1dfb29aecdd004766df5c47811830cf137` ✅ khớp baseline
> **Ngày chấm:** 2026-09-29 · **Bối cảnh:** baseline cố ý **chưa có runtime evidence**
> **Lịch sử:** v1 chấm 82/100 → sửa 12 lỗi → v2 (bản này)

---

## 1. BẢNG ĐIỂM 8 TIÊU CHÍ

| # | Tiêu chí | Trọng số | Điểm | Cơ sở |
|---|----------|----------|------|-------|
| 1 | Tính chính xác kỹ thuật | 20 | **16** | 12 lỗi kỹ thuật đã sửa đúng (MemorySaver, ACR, MCP framing, ELT-vs-ETL, DW criteria). Nhưng còn `ELT ✅` ở MiniERP không được phân tích, và giờ mới phát hiện thêm 1 lỗi tổng số liệu |
| 2 | Chiều sâu phân tích | 15 | **11** | Có đường dẫn file + số dòng cụ thể. Nhưng tài liệu tự thừa nhận chưa chạy gì; ma trận 12×7 là kết luận đọc-code, không phải quan sát runtime |
| 3 | Tính nhất quán & logic | 15 | **11** | 6 mâu thuẫn nội bộ đã sửa. **Nhưng còn 1 lỗi số học mới** (mục 3) — ngay trong ô mà rubric này dùng để chấm |
| 4 | Tính thực tế roadmap | 15 | **12** | Có file/interface/test cụ thể, có phân tích rủi ro. Đã sửa E.4 theo hướng "đo trước tối ưu sau". Nhưng E.5 thiếu acceptance criteria đo được cho L2 |
| 5 | Triển khai & bảo mật (H) | 15 | **10** | 2 profile trust boundary, kill switch, 9 quy tắc bảo mật. Trừ vì R1 quota chưa verify và H.2 còn ô "cần xác minh lại" — tự thừa nhận chưa đủ |
| 6 | Đánh giá MCP/RAG/Agent (I) | 10 | **8** | Có dẫn chứng source thật (protocol.py:49-53, 2.479 dòng MCP, 608 test). Nhưng "MCP đạt chuẩn" dựa trên tools-only, chưa pin version nên chưa chứng minh conformance |
| 7 | Chiều rộng & giá trị portfolio | 5 | **4** | 7 repo có vai trò riêng biệt, cross-repo pattern nối thành câu chuyện. Trừ vì chưa repo nào được code chạy thật cho thiết kế mới |
| 8 | Chất lượng trình bày | 5 | **4** | 11 phần A–K, bảng + diagram rõ. Trừ vì ma trận D.3 thiếu dòng trống trước dòng Tổng, và tổng số liệu sai |
| | **TỔNG** | **100** | **76** | |

### Vì sao 76 chứ không phải 82

v1 được 82 nhưng **v2 thừa nhận nhiều giới hạn hơn**. Ba tiêu chí giảm (2, 5, 6) vì tài liệu nói rõ điều chưa làm, thay vì để reviewer phát hiện. Đó là chi phí của sự trung thực, không phải suy giảm chất lượng.

**Không đạt 90.** Khoảng cách lớn nhất không phải văn phong — mà là **chưa có gì được thực thi**.

---

## 2. PHẦN K — TRẢ LỜI 3 CÂU

### a) Trong 12 lỗi ở K.1, lỗi nào sửa NỬA KHÔNG ĐÚT?

| Lỗi | Đánh giá |
|---|---|
| 1 — `MemorySaver` | ✅ Đúng hoàn toàn. `AsyncPostgresSaver` đúng tài liệu LangGraph |
| 2 — ACR | ✅ Đúng. Bỏ anonymous pull là chuẩn bảo mật |
| 3 — Batch "cả 7 repo" | ✅ Đúng |
| 4 — MiniERP DW | ✅ Đúng |
| 5 — Factory ELT | ✅ Đúng |
| 6 — MAIA Lambda/Kappa | ⚠️ **Sửa nửa.** Giải thích đúng, nhưng giữ cả hai ô 🟡 vẫn là điền số. Bảng phân tách nói document path là Batch/ELT → ô §3 Lambda về logic phải là ❌ |
| 7 — §12 Core/Advanced | ✅ Đúng, là sửa tốt nhất của v2 |
| 8 — Budget alert | ✅ Đúng |
| 9 — 2 profile | ✅ Đúng |
| 10 — MCP capabilities | ✅ Đúng |
| 11 — Data Mesh L0/L1/L2 | ✅ Đúng |
| 12 — Tổng số liệu | ❌ **KHÔNG ĐÚT.** Tài liệu ghi `14 ✅ · 20 🟡`, đếm máy được **12 ✅ · 23 🟡** |

### b) Trong 6 điểm yếu K.2, điểm nào bị đánh giá NHẸ?

| Mức | Đánh giá thật |
|---|---|
| R1 🟡 | **Nặng hơn.** H.2 có 2 ô ghi "cần xác minh lại" — không phải chi tiết, mà ảnh hưởng trực tiếp tới tuyên bố "0 USD". Chưa xác minh thì không được nói 0 USD |
| R2 🟡 | **Nặng hơn.** Không chỉ "chưa có test" — ma trận là **cơ sở của cả tài liệu**, 35 ô chưa ô nào có bằng chứng |
| R3 🔴 | Đúng mức. Lỗ hổng lớn nhất, đã ghi đúng |
| R4 🟡 | Đúng mức |
| R5 🟡 | Đúng mức |
| R6 🟢 | Đúng mức |

### c) K.3 nói "chưa có gì kiểm chứng bằng thực thi" — đồng ý không?

**Đồng ý hoàn toàn.** Đây là hạn chế nghiêm trọng nhất:

- Benchmark 43.36ms: lấy từ `benchmark_results.json` có sẵn, **chưa tự đo lại**
- 608 test MAIA: đếm `def test_` trong source, **chưa chạy**
- Mọi ô ✅/🟡 trong D.3: kết luận từ đọc code
- Toàn bộ Phần H: **chưa deploy Azure lần nào**
- Phần J: chưa chạy Kafka/Spark/Airflow/K8s/Databricks

**Hệ quả:** đây là **thiết kế có chất lượng**, chưa phải **hệ thống đã kiểm chứng**.

---

## 3. LỖI MỚI PHÁT HIỆN KHI CHẤM

| Lỗi | Chi tiết |
|-----|----------|
| **Tổng số liệu ma trận sai** | Ghi `14 ✅ · 20 🟡 · 50 ❌ / 84 ô`. Đếm bằng script trên 12 dòng × 7 cột: **12 ✅ · 23 🟡 · 49 ❌**. Tổng 84 đúng, phân bố sai |
| **§3 Lambda của MAIA về logic** | Bảng phân tách nói document path là Batch/ELT → ô Lambda MAIA phải ❌, không phải 🟡 |
| **Thiếu dòng trống trước dòng Tổng** | Dòng 174 dính ngay sau bảng, Markdown có thể render sai |

⚠️ **Frozen file KHÔNG được sửa.** Ba lỗi này ghi vào đây; tài liệu đã đóng băng — sửa lúc này sẽ phá baseline.


---

## 4. ĐIỂM MẠNH

| Điểm | Dẫn chứng |
|---|---|
| Self-audit trung thực | K.2 tự ghi R3 🔴 "chưa file nào trong 7 repo được thay đổi" — hiếm khi tài liệu tự thừa nhận điều bất lợi cho chính mình |
| Sửa đúng lỗi kỹ thuật thật | `MemorySaver` → `AsyncPostgresSaver`; bỏ ACR anonymous pull; tách §12 Core/Advanced |
| Tách Core vs Advanced (§12) | Nhận ra batching với camera inspection có thể **tăng** latency. Suy nghĩ đúng, không phải thêm công nghệ |
| Phân cấp Data Mesh L0/L1/L2 | Tránh nói quá; chỉ ra 1 JSON Schema chỉ chứng minh contract interoperability |
| 2 profile trust boundary | `local-full` vs `cloud-demo`, thừa nhận cloud demo **mất hybrid search** thay vì giấu |
| Kill switch cho Azure | Biết budget alert không chặn chi phí — điểm ít người nghĩ tới |
| Bảng 3 tầng memory | Checkpointer ≠ Store ≠ RAG knowledge — đúng câu hỏi phỏng vấn Agent Engineer |
| Phân biệt ETL vs ELT | Python/Polars transform trước khi nạp là ETL. Chỗ Data Engineer hay hỏi |

---

## 5. ĐIỂM YẾU CÒN LẠI (không tính lại 12 lỗi đã sửa)

| # | Vấn đề | Mức |
|---|--------|-----|
| W1 | **0 runtime evidence.** 35 ô ✅/🟡 đều chưa có bằng chứng thực thi | 🔴 |
| W2 | Tổng số liệu ma trận sai (12/23/49 chứ không phải 14/20/50) | 🔴 |
| W3 | **Tuyên bố "0 USD" chưa chứng minh** — H.2 còn ô "cần xác minh lại" | 🟡 |
| W4 | Phần H chưa từng deploy thật; `cloud-demo` chỉ là thiết kế | 🟡 |
| W5 | MCP "đạt chuẩn" chưa pin protocol version → chưa chứng minh conformance | 🟡 |
| W6 | Không có ADR cho 3 quyết định loại bỏ phương án | 🟡 |
| W7 | E.5 chưa có acceptance criteria đo được cho mức L2 | 🟢 |
| W8 | E.2 (Factory) và E.3 (Feature Store) chưa ước lượng effort | 🟢 |

---

---

## 7. BẢNG CLAIM CẦN EVIDENCE

> **Spec cho `verify_matrix.py`.** 35 claim = 12 ✅ + 23 🟡. 49 ô ❌ có trạng thái cố định NOT_RUN, không cần chạy gì.
> **Không tự thêm claim ngoài bảng này.** Claim mới → P2 backlog.

### 7.1 Claim ✅ — cần evidence để giữ VERIFIED (12)

| Claim ID | Repo | § | Claim cụ thể | Runtime evidence tối thiểu | Command verify |
|---|---|---|---|---|---|
| `D3-CREDITFLOW-BATCH` | CreditFlow | 1 | Batch pipeline end-to-end | Job chạy, sinh output, exit 0 | `pytest tests/ -k batch --junitxml=...` |
| `D3-FACTORY-BATCH` | Factory | 1 | discover→load→clean→contract-gate→save | Chạy trên fixture, ra parquet ở `data/silver/`, exit 0 | `pytest tests/test_pipeline.py --junitxml=...` |
| `D3-APEX-BATCH` | ApexInspect | 1 | Batch xử lý ảnh tĩnh | Nạp 1 ảnh, ra kết quả detect | `pytest tests/ -k batch --junitxml=...` |

### 7.2 Claim 🟡 — nhóm Data Lake / Warehouse / Lakehouse (7)

| Claim ID | Repo | § | Đã có (cần chứng minh) | **Còn thiếu → không được gọi VERIFIED** | Command verify |
|---|---|---|---|---|---|
| `D3-FACTORY-LAKEHOUSE` | Factory | 5 | Silver parquet (snappy) | **Không phải lakehouse** — thiếu ACID, time travel, schema evolution. Parquet trần không đạt | `pytest tests/test_medallion.py` |
| `D3-FACTORY-DATALAKE` | Factory | 7 | ①Ingestion ②Storage ③Processing ④Consumption | **Thiếu lớp ④ Catalog & Search** — chưa có metadata catalog, chưa có lineage. `DATA_DICTIONARY.md` là tài liệu tĩnh | `pytest tests/ -k "contract or quarantine"` + check `app/catalog/` |
| `D3-MAIA-DATALAKE` | MAIA | 7 | 4 lớp cơ bản, HTTP cursor pagination | **Thiếu lớp ④** metadata catalog; chưa có lineage end-to-end | `pytest tests/test_ingestion_pipeline.py` |
| `D3-APEX-DATALAKE` | ApexInspect | 7 | Ảnh + kết quả detect lưu local | **Không có data lake thật** — chỉ lưu file, không có ingestion/processing layer | Check `src/vision/` ghi ở đâu |
| `D3-FACTORY-DW` | Factory | 8 | Silver parquet + KPI export | **Chưa có staging→marts.** Thiếu `mart_oee`/`mart_quality`/`mart_inventory` (dim+fact) | Check `app/database/marts.py` — **dự kiến KHÔNG tồn tại** |
| `D3-MINIERP-DW` | MiniERP | 8 | Oracle 3NF, staging package, dashboard | **Chưa có marts cho BI.** Đây là operational DB, chưa phải warehouse | `sqlplus @sql/08_*` + đếm object `ERP_AUTOMATION` |
| `D3-MAIA-ELT-PARTIAL` | MAIA | 6 | Mini-warehouse SQLite NL→SQL (MAIA) | Chỉ 1 domain marketing; chưa có transform layer có hệ thống | `pytest tests/test_market_api.py` |

### 7.3 Claim 🟡 — nhóm Data Mesh L1 (6)

### 7.4 Claim 🟡 — nhóm Feature Store / ML Training / Lambda-Kappa (4)

| Claim ID | Repo | § | Đã có (cần chứng minh) | **Còn thiếu → không được gọi VERIFIED** | Command verify |
|---|---|---|---|---|---|
| `D3-MAIA-LAMBDA` | MAIA | 3 | Batch path (doc ingestion) | **Lambda speed layer không tồn tại.** Về logic phải là ❌ — xem lỗi mục 3 | `pytest tests/test_ingestion.py` |
| `D3-MAIA-KAPPA` | MAIA | 4 | Kafka outbox, tái xử lý từ log | Chưa có speed layer song song; chưa chứng minh replay từ offset đầu | `pytest tests/test_outbox_worker.py` |
| `D3-CREDITFLOW-FEATURESTORE` | CreditFlow | 10 | `pipeline/feature_engineering/features.py` | **Chưa tách offline/online store. Chưa có point-in-time correctness → nguy cơ data leakage** | Check `pipeline/feature_store/` — **dự kiến KHÔNG tồn tại** |
| `D3-APEX-MLTRAINING` | ApexInspect | 11 | Trigger engine, MES sync | **Không có ML training** — chỉ inference. Không có eval/drift/registry | Check có script training nào không |

### 7.5 Claim 🟡 — nhóm Real-time Inference Core (3)

| Claim ID | Repo | § | Đã có (cần chứng minh) | **Còn thiếu → không được gọi VERIFIED** | Command verify |
|---|---|---|---|---|---|
| `D3-APEX-REALTIME` | ApexInspect | 12 | ONNX 43.36ms, SAHI, trigger | **Thiếu P50/P95/P99, queue_depth, deadline_miss_rate, overload policy.** Chưa có benchmark chạy lại | Chạy benchmark script → `latency.json` + `throughput.json` |
| `D3-CREDITFLOW-REALTIME` | CreditFlow | 12 | FastAPI serving, HITL LangGraph | **Thiếu latency SLO, overload policy, monitoring** (phần Core của §12) | `pytest tests/test_api.py` + đo latency thật |
| `D3-MINIERP-REALTIME` | MiniERP | 12 | Outbox fail-soft, PL/SQL atomic | **Thiếu latency SLO + overload policy.** Đây là hệ transaction, không phải ML inference | SQL timing script |

### 7.6 Claim bị nghi ngờ phân loại sai — cần xác minh khi chạy (3)

| Claim ID | Repo | § | Vấn đề | Hành động khi verify |
|---|---|---|---|---|
| `D3-FLASHSALE-MLTRAINING` | FlashSale | 11 | **Sai về phân loại** — FlashSale không có ML. Nên là ❌ | Chạy xác nhận không có model training; nếu đúng thì đổi ô ❌ |
| `D3-MINIERP-MLTRAINING` | MiniERP | 11 | **Sai về phân loại** — MiniERP không có ML. Nên là ❌ | SQL đếm bảng, xác nhận không có model registry |
| `D3-FLASHSALE-REALTIME-ALT` | FlashSale | 12 | Có thể trùng ý với `D3-FLASHSALE-REALTIME` | Gộp nếu trùng |

### 7.7 Tổng kết claim

| Nhóm | Số claim | Trạng thái kỳ vọng ban đầu |
|---|---|---|
| 7.1 — ✅ cần giữ VERIFIED | 12 | NOT_RUN (chưa có evidence) |
| 7.2 — Data Lake/Warehouse | 7 | PARTIAL hoặc NOT_RUN |
| 7.3 — Data Mesh L1 | 6 | PARTIAL hoặc NOT_RUN |
| 7.4 — Feature Store / ML / Lambda-Kappa | 4 | PARTIAL hoặc NOT_RUN |
| 7.5 — Real-time Core | 3 | PARTIAL hoặc NOT_RUN |
| 7.6 — Nghi phân loại sai | 3 | Cần xác minh |
| **Tổng** | **35** | |

**Điểm kiểm tra:** 12 + 7 + 6 + 4 + 3 + 3 = **35** ✓ khớp 12✅ + 23🟡

---

## 8. GHI CHÚ CHO REGRADE #2

Dùng **cùng rubric 100 điểm**, không đổi tiêu chuẩn. Cần hỏi thêm 3 câu:

1. Claim nào **được chứng minh** (có artifact + digest + commit khớp)?
2. Claim nào **chỉ được viết ra** (evidence thiếu hoặc STALE)?
3. Điểm tăng có tương ứng **năng lực thật**, hay chỉ thêm giấy tờ?

**Không mặc định REGRADE #2 = điểm cao hơn.** Nếu evidence cho thấy 43.36ms không reproduce được, hoặc 608 test fail, thì trừ ở tiêu chí 1 và 2 là đúng.


| Claim ID | Repo | § | Đã có (cần chứng minh) | **Còn thiếu → không được gọi VERIFIED** | Command verify |
|---|---|---|---|---|---|
| `D3-CREDITFLOW-MESH` | CreditFlow | 9 | Domain riêng, có data contract | **Thiếu L2**: catalog/discoverability, SLO, access policy, federated governance | Check `docs/adr/` + `DATA_PRODUCT.md` — dự kiến chưa có |
| `D3-FACTORY-MESH` | Factory | 9 | Data contract trong `app/data_contracts/` | **Thiếu L2** + chưa có consumer ngoài repo | `pytest tests/ -k contract` |
| `D3-APEX-MESH` | ApexInspect | 9 | Domain vision riêng, MES sync | **Thiếu L2** hoàn toàn; chưa có data contract publish | Check `contracts/` — dự kiến chưa có |
| `D3-MAIA-MESH` | MAIA | 9 | 1 data plane (marketing) | **Chưa mở rộng domain**; thiếu L2 | `pytest tests/ -k "mcp or market"` |
| `D3-MINIERP-MESH` | MiniERP | 9 | ERP_OPERATIONS/TRACEABILITY packages, outbox | **Thiếu L2**; chưa consume data product từ domain khác | SQL: đếm object trong 3 package |
| `D3-HELPDESK-MESH` | Helpdesk | 9 | ITSM domain, `docs/11-minierp-integration.md` | **Thiếu L2**; IT asset chưa thành data product | Đếm file trong `tickets/` + `docs/` |

| `D3-FLASHSALE-BATCH` | FlashSale | 1 | Worker bất đồng bộ + outbox | Ghi order → outbox row cùng transaction → worker consume | `dotnet test --filter Category=Integration` |
| `D3-MAIA-BATCH` | MAIA | 1 | doc ingestion path (file→chunk→Qdrant) | Nạp doc, chunk, index, search trả kết quả | `pytest tests/test_ingestion.py --junitxml=...` |
| `D3-MINIERP-BATCH` | MiniERP | 1 | ELT qua 12 file SQL | Chạy `sql/00..11`, seed → query trả data | `sqlplus @sql/00_*.sql ...` hoặc Docker Oracle |
| `D3-FLASHSALE-STREAMING` | FlashSale | 2 | Producer→RabbitMQ→Worker→PG, DLQ, idempotency, **CI-enforced zero-oversell** | 50 req/10 stock → đúng 10 sales; consumer fail → DLQ; replay không trùng | `dotnet test --filter Category=Integration` |
| `D3-MAIA-STREAMING` | MAIA | 2 | Kafka outbox scheduler/worker, Kappa-like | Producer→topic→consumer; restart từ offset; không mất/săn | `pytest tests/test_outbox*.py --junitxml=...` |
| `D3-MINIERP-ELT` | MiniERP | 6 | ELT 12 file SQL, staging→3NF | Load thô → transform **bằng SQL trong engine** → 3NF | SQL script + assert số bảng/dòng |
| `D3-CREDITFLOW-MLTRAINING` | CreditFlow | 11 | Client→Preprocess→Feature→Train→Eval→MLflow→PSI drift | Train thật, metric sinh, model vào MLflow, PSI chạy | `pytest tests/ -k "model or drift" --junitxml=...` |
| `D3-FLASHSALE-REALTIME` | FlashSale | 12 | Redis Lua CAS 1 RTT, pool cap 80/100 | Benchmark CAS latency; test pool cap không vượt | `dotnet test --filter Category=Perf` + benchmark JSON |
| `D3-MAIA-REALTIME` | MAIA | 12 | Hybrid RRF dense+BM25, evidence gate, citation | Query → top-k có `fused_score`; không có bằng chứng → refusal; citation resolve được file | `pytest tests/test_retriever.py tests/test_corrective_rag.py` |


## 6. TOP 5 VIỆC TIẾP THEO

| # | Việc | Vì sao |
|---|------|--------|
| 1 | **Dựng verification system** rồi chạy Factory → MAIA → ApexInspect | Đóng W1. Mọi việc khác đều phụ thuộc evidence thật |
| 2 | Sửa tổng số liệu ma trận trong bản làm việc (giữ frozen nguyên vẹn) | Đóng W2 — sai số học trong tài liệu dùng làm chuẩn là rủi ro |
| 3 | Chạy Azure sandbox đo quota thật, thay mọi ô "cần xác minh" | Đóng W3 — không đo thì không được nói 0 USD |
| 4 | Pin MCP protocol version + conformance test | Đóng W5 |
| 5 | 3 ADR cho quyết định loại phương án | Đóng W6 |
