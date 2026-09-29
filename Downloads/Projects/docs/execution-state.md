# Execution State

Cập nhật sau mỗi capability hoàn tất. File này là nguồn sự thật khi context
dài hoặc phiên làm việc bị ngắt — đọc file này để biết chính xác cần làm gì tiếp.

## Trạng thái hiện tại

| Mục | Giá trị |
|---|---|
| Phase hiện tại | 15 — Databricks (`NOT_RUN` không cần credential) |
| Ngày | 2026-09-29 |
| Phase hoàn tất | 6, 7, 8, 11, 12, 13, 14 |

## Phase 6 — Factory Data Platform: COMPLETE

Commits (`Factory-Data-Automation-v-AI-Reporting-Platform`):

- `521ad5b` — Gold layer, 4 dataset, 20/20 test
- `958c6a0` — Catalog registry + Lineage + Marts, 78/78 targeted test

Targeted **78 passed**. Full **213 passed, 2 failed, 5 skipped**.
2 failure **pre-existing**, xác nhận bằng `git stash` trên `521ad5b`:
`test_fda_013_scale_10x_pipeline_survives`, `test_silver_parquet_smaller_than_csv`.

Evidence: `docs/evidence/factory-v3/`. Generator: `docs/_gen_factory_v3_evidence.py`.

## Phase 7 — CreditFlow Feature Store: COMPLETE

Baseline: `847f8f45738663ce323074e7cf02e156f6206e7f`. Commit: `c2d87d3`.

Feature store tests **36 passed**. Full suite **410 passed, 5 skipped, 0 failed**.

Evidence chứng minh bằng runtime:

- `leakage_invariant_holds: true` — thêm 3 sự kiện tương lai, feature tại T không đổi
- `train_serve_parity_holds: true` — 8 feature × 3 mốc thời gian, 0 mismatch
- `ttl_behaviour_correct: true` — hết hạn trả `null`

Evidence: `docs/evidence/creditflow-p7/`. Generator: `docs/_gen_creditflow_p7_evidence.py`.

**Quyết định thiết kế:** Online store KHÔNG tính lại feature từ raw event; nó phục vụ
giá trị đã materialize tại `as_of`. Test riêng chứng minh khi có đủ lịch sử thì
`compute_feature` khớp offline chính xác.

## Phase 8 — MAIA Durable Persistence: COMPLETE

Baseline: `54b87d89ef350fc24a31427fa265f5942f7684fc` (823 passed / 1 skipped).
Commit: `a0c9724` (trong worktree `MAIA-clean`).

Persistence tests **8 passed**. Full suite **831 passed, 1 skipped, 0 failed**.

**Bằng chứng restart thật, hai process khác PID:**

- `write_pid 74713` → process chết → `read_pid 74746`
- `processes_differ: true`, `state_survived_restart: true`, `resumed_correctly: true`
- Xác nhận từ phía database: **40 checkpoint rows**, 39 có parent link (chain thật)

Ba tầng persistence tách bạch (không sửa 2 tầng cũ):

| Tầng | Backend | Trạng thái |
|---|---|---|
| thread state | Postgres (AsyncPostgresSaver) | **MỚI** |
| long-term memory | SQLite (`agent/memory.py`) | có sẵn |
| RAG index | Qdrant (`vector_store.py`) | có sẵn |

Evidence: `docs/evidence/maia-p8/`. Generator: `docs/_gen_maia_p8_evidence.py`.
Probe: `docs/_p8_process_restart_probe.py`.

Hạ tầng local đang chạy (docker):

- `maia-pg` — postgres:16-alpine, port **55432**
- `maia-qdrant` — qdrant, port **6333**
- DSN: `postgresql://maia:maia_test_pw@localhost:55432/maia`
  (chỉ dùng cho test local, KHÔNG commit vào repo)

## Phase 11 — Manufacturing Contracts: COMPLETE

Repo mới: `manufacturing-contracts` (git init riêng — **không** commit vào
git repo của home dir). Commit `ca91aa1`.

3 contract + registry, tests **38 passed**.

| Contract | Producer | Consumers | Security |
|---|---|---|---|
| `inspection-events.v1` | ApexInspect | Factory, MiniERP | internal |
| `factory-kpis.v1` | Factory | dashboards, MiniERP | internal |
| `manufacturing-events.v1` | cả 3 | cả 3 | confidential |

Evidence: `docs/evidence/p11-contracts/`. Generator: `docs/_gen_p11_contracts_evidence.py`.

### Phạm vi — ghi rõ KHÔNG phải Data Mesh

`registry.json` có `scope_boundaries.is_not`, và **test khẳng định** nó:
không có governance plane, không self-serve, không query federation.
Chỉ là 3 file trong repo. Không gọi là mesh.

### Quy tắc tương thích — được kiểm chứng, không phải hứa suông

`classify_change` / `classify_change_deep` hiện thực `compatibility_policy`.
Test bắt chính classifier với 7 thay đổi breaking + 4 thay đổi an toàn.

**Quy tắc ngược đời đáng nhớ: BỎ field khỏi `required` là breaking.** Nghe như
nới lỏng, nhưng consumer đã viết logic "trường này luôn có" sẽ nổ lúc
runtime thay vì bị chặn ở validation. Phát hiện ra khi viết test `tenant_id`.

### Giới hạn đã ghi nhận, không che

`classify_change` chỉ quét top-level; `classify_change_deep` quét đệ quy.
Test `test_classifier_is_top_level_only_which_is_a_known_limit` khẳng định
**cả hai** phía, nên lỗ hổng vẫn nhìn thấy được.

## Phase 12 — Kafka Event Backbone: COMPLETE

Cùng repo `manufacturing-contracts`. Commit `b28a37f`. Tests **50/50 pass**
(38 contract + 12 eventbus, gồm end-to-end qua broker thật).

Broker: Redpanda (Kafka-compatible) container `maia-kafka`, external port
**59092**, admin 58081. Advertise addr phải là `external://localhost:59092`
thì client từ host mới nối được (mặc định `127.0.0.1:9092` là port trong
container → connection refused).

**Semantics: at-least-once + idempotent consumer.** Không hứa exactly-once —
có test pin chuỗi `"at-least-once"` để không ai vô tình nâng lời hứa.

### Bằng chứng runtime (không mock)

- produce 6 → broker high watermark **6** → consume **6**
- **Cưỡng ép giao lại** (cùng group + seek offset 0 + deduper đã có):
  `handled 0, duplicates_suppressed 6`
- Handler fail lần 1: **2 attempts, retry thành công**, lần fail không commit

### Vấn đề dedupe state — phát hiện khi chạy thật

`Deduplicator` là set trong RAM → chết cùng process → restart là xử lý lại
mọi message trùng. Đây đúng là thứ at-least-once bắt buộc phải chịu.

Cách xử lý (không giả vờ đã giải quyết):
- deduper **inject được** → có thể nối store bền vững
- `Deduplicator.restore(ids)` — nạp lại state trước khi consume
- Giới hạn "mặc định là RAM" được ghi rõ trong README và docstring

### Quyết định thiết kế

**Commit offset SAU khi handler chạy xong, không phải trước.** Commit trước
nghĩa là mất event vĩnh viễn nếu chết giữa commit và xử lý. Không commit thì
phải chịu redelivery — đó là lý do dedupe tồn tại.

Cách test redelivery: dùng **cùng consumer group** + `seek(TopicPartition(t,0,0))`.
Group mới chỉ chứng minh "group mới đọc từ đầu", không chứng minh gì về dedupe.

## Phase 13 — Spark Benchmark: COMPLETE

Repo mới: `spark-benchmark` (git init riêng). Commit `c3a23a1`.
Tests **12/12 pass**. Evidence: `docs/evidence/p13-spark/`.
Generator: `docs/_gen_p13_spark_evidence.py`.

Máy 8 CPU, Python 3.14.7, Spark 4.2.0 `local[4]`, Polars 1.44.2.

| size | rows | polars | spark | ratio | parity |
|---|---:|---:|---:|---:|:--:|
| small | 50,000 | 0.0039s | 0.1779s | 45.5× | OK |
| medium | 500,000 | 0.0062s | 0.6175s | 99.0× | OK |
| large | 5,000,000 | 0.0726s | 1.1972s | 16.5× | OK |

**Polars thắng cả 3 kích thước.** Ghi đúng như đo — và **không phải lý do bỏ
Spark**. 5M dòng ≈ 100 MB, đúng vùng mà engine in-process một node là lựa
chọn đúng. Spark thuộc bài toán khác.

### 3 khác biệt thật giữa 2 engine (phát hiện khi chạy)

| Trường hợp | Polars | Spark | Xử lý |
|---|---|---|---|
| `x / 0` | NaN, im lặng | ném exception | cả hai → `null` |
| `sum` tập rỗng | `0` | `NULL` | cả hai → `null` |
| trung bình float32 | lệch ~1e-06 | lệch ~1e-06 | tolerance `1e-5` |

**Quyết định đáng tranh luận:** `sum` tập rỗng ra `null` chứ không phải `0`.
"Không có bản ghi nào đo được downtime" ≠ "downtime bằng 0 phút".
`coalesce(_, 0)` nói dối theo hướng nguy hiểm hơn. Vì vậy kiểm **count bản
ghi đo được**, không kiểm tổng — dây chuyền chạy liên tục thì tổng = 0 là
thông tin thật.

Cột số nguyên vẫn so `==`. Có test khẳng định tolerance không bao giờ vượt
`1e-3`, để nó không thành cách "cho xanh" dễ dàng.

### 2 bug chỉ lộ ra khi chạy thật

1. `createDataFrame(to_pylist())` khiến Spark đoán kiểu từ vài dòng đầu thay
   vì schema → `date` bị đọc thành float → groupBy ra 50,000 groups thay vì
   4,380. Parity FAIL là kết quả **đúng**; con số thời gian thì vô nghĩa.
2. numpy scalar **không** pass `isinstance(x, float)` → hàm làm tròn bị bỏ qua
   cho mọi cột float.

### Giới hạn phép đo — ghi rõ, không giấu

`extra_peak_mb` từ `tracemalloc` **chỉ đo Python heap** → KHÔNG phản ánh bộ
nhớ Spark (JVM). `process_peak_rss_mb` (173/248/845 MB) gần đúng hơn nhưng
là cả process. Thời gian là best-of-3 sau warm-up. Một máy `local[4]`; không
suy ra gì về hành vi scale ngang.

## Phase 14 — Airflow DAG: COMPLETE

Commit `8fe2dc4` (repo `Factory-Data-Automation-v-AI-Reporting-Platform`).
Evidence: `docs/evidence/p14-airflow/`. Probe: `docs/_p14_airflow_probe.py`.
Generator: `docs/_gen_p14_evidence.py`.

Airflow 2.10.5, Python 3.11.15, venv riêng `Factory-Data-Automation-v-AI-Reporting-Platform/.venv-airflow`
(dời từ root vào repo Factory ngày 2026-09-29; `.airflow/` cũng nằm trong repo Factory).
**Airflow 2.x yêu cầu Python <3.13** → phải tạo venv riêng bằng
`uv venv --python 3.11`, không dùng được venv Python 3.14.

DAG `factory_medallion_daily`:
`ingest → validate_quarantine → gold → marts → quality_checks`

### Bằng chứng runtime (không mock)

- `import errors: NONE`
- dependency chain đúng thứ tự
- **cả 5 task SUCCESS**, `dag state=success`
- **retry thật** (lần chạy riêng): thiếu pydantic → `UP_FOR_RETRY` lúc
  16:17:48 và 16:18:26 (cách 38s rồi 117s — exponential backoff hoạt động)
  → `FAILED` 16:20:23
- **failure path**: `quality_checks` raise `ValueError` khi mart rỗng
- **idempotency**: build 2 lần → counts và DataFrame giống hệt

Lưu ý evidence ghi rõ: `tasks_retried=0` ở lần chạy thành công là **bình
thường**, không phải retry chưa chứng minh. Config là khai báo; hành vi thì
chứng minh bằng log lần chạy thật bị fail.

### Quyết định thiết kế

- **Quarantine KHÔNG làm DAG fail.** Hàng vi phạm contract bị lo có chủ
  đích. Fail nghĩa là mất cả lô dữ liệu tốt. Chỉ fail khi tỷ lệ lo >25% —
  lúc đó vấn đề ở nguồn.
- **Task cuối kiểm chứng, không báo cáo.** Mọi task trước có thể "thành công"
  với dữ liệu sai. `quality_checks` assert 3 điều: mart không rỗng, tỷ lệ
  lỗi hợp lý, `good + reject <= actual`.

## Bug thật đã phát hiện và sửa

### Phase 6

1. Catalog lệch tên gold (`gold_oee_daily` vs `oee_daily` thật) → import `GOLD_DATASETS`
2. `e["target"]` KeyError — typo so với `target_dataset`
3. `__contains__` không đủ cho `set(cat)` → thêm `__iter__`

### Phase 7

4. Store từ chối raw event → thêm `RAW_EVENTS` + `WRITABLE_NAMES`
5. `_visible_history` bỏ qua raw event → mọi feature cửa sổ ra 0
6. `WRITABLE_NAMES` đặt trước `FEATURE_BY_NAME` → `NameError` lúc import
7. `assert_no_future_data` sai ngữ nghĩa → kiểm tra lịch sử trả về, không kiểm tra data gốc

### Phase 8

8. Test secret scan quá rộng — flag ví dụ trong error message → chỉ flag DSN có credential thật
9. Vị trí test file sai (`REPO` resolve 3 cấp) → dùng `MAIA_ROOT` từ `__file__`

## Điểm cần nhớ về quy trình

- `editor` giới hạn **6000 ký tự** mỗi lần ghi.
- **`insert_line` có thể ăn mất dòng tại đúng vị trí chèn.** Đã xảy ra **9 lần**:
  mất `return`, mất `}`, mất `summary = ...`, sinh orphan, mất `if __name__`.
  **Luôn làm sau mỗi `insert_line`:** `py_compile` → `tail` → xem vùng chèn.
- **Heredoc với ký tự Unicode làm hỏng terminal.** Dùng `editor`.
- **Commit message nhiều dòng dùng `-m` làm hỏng terminal.** Ghi file rồi `-F`.
- Venv (vị trí mới từ 2026-09-29): `Factory-Data-Automation-v-AI-Reporting-Platform/.venv-factory`
  (pytest+pandas+numpy), `MAIA-clean/.venv-maia` (đầy đủ MAIA deps).
  `ApexInspect-AI/.venv-apex2` (py3.12, có pytest). `.venv-apex` cũ (không pytest) **đã xóa**.
- Qdrant phải chạy ở port 6333 (mặc định trong `config.py`) thì test mới xanh.

## Hành động kế tiếp — Phase 11

Manufacturing Data Product: ApexInspect → Factory → MiniERP.

1. Xác định cấu trúc thực tế của 3 repo (chưa khảo sát sâu).
2. Tạo shared contracts: `inspection-events.v1.json`, `factory-kpis.v1.json`,
   `manufacturing-events.v1.json`.
3. Mỗi data product cần: owner, producer, consumer, schema, version,
   compatibility, SLA/SLO, security class, retention.
4. Compatibility test bắt buộc.
5. **Không gọi đây là full Data Mesh** — ghi đúng phạm vi đã làm.


## Phase 6 — Factory Data Platform: COMPLETE

Commits (repo `Factory-Data-Automation-v-AI-Reporting-Platform`, branch `main`):

- `521ad5b` — Gold layer, 4 dataset, 20/20 test
- `958c6a0` — Catalog registry + Lineage + Marts, 78/78 targeted test

Kết quả: Targeted **78 passed**. Full suite **213 passed, 2 failed, 5 skipped**.
2 failure là **pre-existing**, xác nhận bằng `git stash` trên HEAD `521ad5b`:

- `tests/test_scale_and_perf.py::test_fda_013_scale_10x_pipeline_survives`
- `tests/test_silver_parquet.py::test_silver_parquet_smaller_than_csv`

Evidence: `docs/evidence/factory-v3/`. Generator: `docs/_gen_factory_v3_evidence.py`.

## Phase 7 — CreditFlow Feature Store: COMPLETE

Baseline trước khi làm: HEAD `847f8f45738663ce323074e7cf02e156f6206e7f`, worktree clean.

Commit: `c2d87d3` (repo `CreditFlow`, branch `main`).

Kết quả:

- Feature store tests: **36 passed**
- Full suite: **410 passed, 5 skipped, 0 failed** — không regression
- Evidence chứng minh bằng runtime, không phải log:
  - `leakage_invariant_holds: true` — thêm 3 sự kiện tương lai, feature tại T **không đổi một byte**
  - `train_serve_parity_holds: true` — 8 feature × 3 mốc thời gian, 0 mismatch
  - `ttl_behaviour_correct: true` — hết hạn trả `null`, không trả số cũ

Files:

- `pipeline/feature_store/definitions.py` — 8 feature + 3 raw source event
- `pipeline/feature_store/offline_store.py` — lọc theo as_of ở đúng 1 chỗ
- `pipeline/feature_store/online_store.py` — trạng thái hiện tại, TTL, `now_fn` injectable
- `tests/test_feature_store.py`

Evidence: `docs/evidence/creditflow-p7/`. Generator: `docs/_gen_creditflow_p7_evidence.py`.

### Quyết định thiết kế đáng ghi lại — Phase 7

**Online store KHÔNG tính lại feature từ raw event.** Nó phục vụ đúng giá trị đã
materialize tại `as_of`. Đây là đường đi thực tế: pipeline tính feature rồi đẩy
sang phục vụ. Nếu online tự tính lại từ raw event thì nó sẽ khác offline, và
đó chính là bug train/serve skew mà ta đang cố tránh.

Có một test riêng (`test_online_recompute_matches_offline_when_history_available`)
chứng minh: **khi** online có đủ lịch sử thì `compute_feature` khớp offline
chính xác — chứng minh hai store dùng chung công thức, không phải chỉ trùng hợp.

### Bug thật đã phát hiện và sửa trong Phase 7

1. **Store từ chối raw event** — `write()` kiểm tra tên trong `definitions`, nên
   `loan_issued` bị từ chối dù feature dựa vào nó. Sửa: thêm `RAW_EVENTS` và
   `WRITABLE_NAMES` tách bạch "sự kiện nguồn" với "feature dẫn xuất".
2. **`_visible_history` chỉ duyệt feature, bỏ qua raw event** — mọi feature cửa
   sổ sẽ ra 0. Sửa: duyệt hợp nhất cả hai.
3. **`WRITABLE_NAMES` đặt trước `FEATURE_BY_NAME`** — `NameError` lúc import.
   Sửa: đặt sau, kèm comment giải thích vì sao thứ tự quan trọng.
4. **`assert_no_future_data` sai ngữ nghĩa** — nó kiểm tra dữ liệu gốc, nên
   store có sự kiện tương lai (hoàn toàn bình thường) là fail. Sửa: kiểm tra
   lịch sử **trả về cho người tính feature**.

## Điểm cần nhớ về quy trình

- `editor` giới hạn **6000 ký tự** mỗi lần ghi. File dài phải chia 2 phần.
- `insert_line` có thể **ăn mất dòng tại đúng vị trí chèn** (không chỉ cuối file).
  Sau mỗi `insert_line` phải: `py_compile` → xem `tail` → xem vùng chèn.
  Đã xảy ra **6 lần**, gây mất `return`, mất `}`, sinh orphan, mất `if __name__`.
- **Heredoc với ký tự Unicode làm hỏng terminal.** Tránh `cat <<'EOF'` với tiếng Việt.
- **Commit message nhiều dòng dùng `-m` làm hỏng terminal.** Ghi message vào file
  trước rồi `git commit -F <file>`.
- Venv dùng chung: `/Users/mainguyenbinhtan/Downloads/Projects/Factory-Data-Automation-v-AI-Reporting-Platform/.venv-factory/bin/python`
  (có pytest + pandas + numpy)

## Hành động kế tiếp chính xác — Phase 8

Canonical base = `MAIA-clean` worktree (HEAD `54b87d89ef350fc24a31427fa265f5942f7684fc`,
823 passed / 0 failed / 1 skipped). Dirty `MAIA/` giữ nguyên làm historical evidence.

1. Capture HEAD + status + baseline suite của `MAIA-clean`.
2. Implement `AsyncPostgresSaver` cho thread/checkpoint state.
3. Long-term memory: persistent `Store` (tách biệt khỏi thread state).
4. RAG: Qdrant/index (tách biệt khỏi hai cái trên).
5. Runtime test bắt buộc — KHÔNG mock:
   `create thread → state → interrupt → terminate process → restart → resume → same state`
6. Evidence → commit → clean worktree → Phase 11.

## 2026-09-29 — Dọn cấu trúc Projects (agent cleanup, đợt 1)

- 6 file `.md` lẻ ở root → `docs/briefs/`.
- `.gitignore` (Projects) mở rộng. Xóa `docs/__pycache__/`.
- Tạo `docs/_HANDOFF-2026-09-29-cleanup.md` (chi tiết).
- ⚠️ Git root = HOME (`/Users/mainguyenbinhtan`), không phải `Projects/` — chỉ commit trong repo con.

## 2026-09-29 — Gom file về đúng project (đợt 2, user duyệt)

- `.venv-airflow`, `.venv-factory`, `.airflow/` → `Factory-Data-Automation-v-AI-Reporting-Platform/`
- `.venv-apex2` → `ApexInspect-AI/`; **xóa** `.venv-apex` (trùng, không pytest, 226M)
- `.venv-maia` → `MAIA-clean/` (canonical, không phải `MAIA/` dirty)
- k8s tách: `10-` → `MAIA-clean/k8s/`, `20-` → `CreditFlow/k8s/`,
  `30-` → `ApexInspect-AI/k8s/`, `40-` + `factory-data-src/` → `Factory/.../k8s/`,
  `Dockerfile.factory` + `requirements.factory.txt` → `Factory/.../docker/`
- **Xóa** `README.md` root (user chốt; thông tin portfolio nằm ở đây + handoff).
- Sửa ref cứng: 7 script `docs/_*.py` (xem `docs/_HANDOFF-2026-09-29-restructure.md`),
  `Factory/.../.airflow/airflow.cfg` (dags_folder, base_log_folder),
  `.gitignore` của Factory / Apex / MAIA-clean.
- GIỮ ở ngoài: `docs/` shared (generator trỏ cứng), `k8s-manifests/00-namespace.yaml`
  (namespace chung), `.kilo/` (worktree tool-managed).
- Venv dời chỗ → gọi qua `<venv>/bin/python -m ...`, không gọi console script trực tiếp (shebang cũ gãy).

## 2026-09-29 — Dọn đợt 3 (user duyệt từng mục)

- Xóa `.venv-local/` (stray 14M, không ref), 14 file `docs/_commit_msg*.txt` (temp đã commit xong),
  `docs/__pycache__/`.
- `.gitignore` (Projects): gộp venv lẻ thành `.venv-*/`.
- GIỮ: `spark-benchmark` / `manufacturing-contracts` (clean), `MAIA-clean`
  (`M .gitignore` + `?? k8s/` chờ chủ repo commit), stub `k8s-manifests/`, mọi `docs/_*.py`.
- Root hiện tại: 10 repo + `docs/` + stub `k8s-manifests/`. Chi tiết: `docs/_HANDOFF-2026-09-29-cleanup3.md`.

