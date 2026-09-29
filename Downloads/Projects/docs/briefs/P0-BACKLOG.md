# P0 Implementation Backlog

> **Nguồn:** phần còn lại của [DESIGN-BRIEF-v2-frozen.md](./DESIGN-BRIEF-v2-frozen.md) — mục K.2 (6 điểm yếu còn tồn tại).
> **Nguyên tắc:** P0 đóng **gap giữa thiết kế và code thật**, không phải thêm công nghệ mới. Mỗi hạng mục phải kết thúc bằng **bằng chứng kiểm chứng được**, không phải bằng "đã viết code".
> **Ngày:** 2026-09-29

---

## Ưu tiên tuyệt đối: R3 — biến claim thành evidence

Trước khi làm gì khác, phải biết cái gì trong ma trận là **thật**. Hiện mỗi ô ✅/🟡 chỉ là kết luận khi đọc source, ai cũng nói vậy được.

| Việc | File | Xong khi nào |
|------|------|---------------|
| Viết script tự kiểm chứng ma trận | `docs/verify_matrix.py` | Chạy được, in ra bảng 12×7 kèm file/thứ dòng bằng chứng |
| Kiểm chứng claim MCP (12) | Kiểm tra `protocol.py` có đủ method, có `resources/*` không | Output JSON liệt kê method tìm thấy |
| Kiểm chứng agent persistence (R2) | Grep `checkpointer` / `Store` trong `agent/` | Xác nhận đúng những gì tài liệu nói |
| Kiểm chứng ACR security (R1) | Grep `AcrPull` / `anonymous` trong IaC | Xác nhận không còn anonymous pull |
| Kiểm chứng data contract | Load `app/data_contracts/` và validate | Danh sách contract + trạng thái |

**Đây là điều kiện tiên quyết.** Không làm được thì mọi ô ✅ trong ma trận vẫn chỉ là lời hứa.

---

## P0-A — Đóng 6 lỗi còn lại (K.2)

| ID | Việc | Repo | File cần tạo/sửa | Xong khi nào |
|----|------|------|-------------------|---------------|
| **P0-A1** | R1: chốt quota Azure bằng số đo thật | — | `docs/AZURE-COST-MODEL.md` | Có bảng "dịch vụ → quota → nguồn → ngày kiểm tra". Sai thì ghi sai, không đoán |
| **P0-A2** | R3: Gold layer | Factory | `app/etl/gold.py` + `tests/test_gold_layer.py` | Test idempotent pass, 4 bảng gold sinh ra, dữ liệu không mất từ silver |
| **P0-A3** | R3: Catalog + lineage | Factory | `app/catalog/registry.py`, `lineage.py` + test | `--lineage production` trả về quan hệ raw→silver→gold |
| **P0-A4** | R3: Agent persistence đúng | MAIA | `AsyncPostgresSaver` + `Store` + test restart | Test: instantiate lại agent → context hội thoại **còn nguyên** |
| **P0-A5** | R3: MCP version pinning | MAIA | `docs/MCP-COMPATIBILITY.md` + conformance test | Test chạy được, ghi rõ version đã test |
| **P0-A6** | R3: Audit hash chain | ApexInspect | Port từ CreditFlow `ledger` + test | Test: sửa 1 record → verify chain **hỏng** (chứng minh tamper-evident) |

**A6 là sửa lỗ hổng bảo mật → làm sớm nhất trong nhóm này**, không cần benchmark.

---

## P0-B — Đóng điểm yếu đã biết (không nằm trong K.2 nhưng cần cho nhất quán)

| ID | Việc | Repo | Xong khi nào |
|----|------|------|---------------|
| **P0-B1** | R4: chạy lại benchmark ApexInspect | ApexInspect | Có `benchmark_results.json` mới kèm điều kiện máy. **Không reproduce được thì ghi rõ không được** |
| **P0-B2** | R4: chạy lại 608 test MAIA | MAIA | CI xanh, đính kèm log. Không chạy được thì ghi rõ |
| **P0-B3** | R5: ADR cho 3 quyết định loại bỏ phương án | — | `docs/adr/` có: không-dùng-Azure-Service-Bus · giữ-RabbitMQ · AI-Search-chỉ-cho-demo |
| **P0-B4** | §12 Core: latency SLO + overload policy | ApexInspect | Có P50/P95/P99 + chính sách khi quá tải **viết ra giấy** |
| **P0-B5** | Feature store + point-in-time test | CreditFlow | `pipeline/feature_store/` + `test_feature_store.py` chứng minh không rò rỉ feature tương lai |

---

## Definition of Done cho toàn bộ P0

Không coi P0 xong nếu chưa đạt **cả 4** điều sau:

1. ✅ `verify_matrix.py` chạy xanh, output khớp với ma trịn trong tài liệu
2. ✅ Mỗi ô ✅ trong ma trận **dẫn tới ít nhất 1 file + 1 test cụ thể** có thể chạy
3. ✅ Benchmark đã chạy lại trên máy hiện tại, kèm điều kiện môi trường
4. ✅ 3 ADR đã viết bằng chuẩn `docs/adr/` của repo

**Tiêu chí thành công thật:** sau P0, câu *"repo này có X không?"* phải trả lời được bằng **lệnh kiểm tra chạy được**, không phải bằng cách đọc tài liệu này.

---

## Ngoài phạm vi P0 (chưa làm)

| Việc | Lý do hoãn |
|------|------------|
| Kafka / Spark / Airflow / Kubernetes / Databricks | Chỉ bắt đầu sau khi P0 xong — tránh xây trên nền chưa kiểm chứng |
| 7 architecture diagrams LinkedIn | Phải mô tả **kiến trúc đã chạy**, không phải roadmap |
| Nâng lên Azure Cloud tier / Basic | Vẫn đang ở local-full profile |
| MCP `resources/*` + `prompts/*` | Là capability mở rộng, không phải lỗi tuân thủ spec |

---

# Evidence Plan — quyết định thiết kế, CHƯA triển khai

> **Trạng thái: PLAN ONLY.** Chưa tạo `docs/evidence/`, chưa chạy test nào.
> **Lý do chưa làm:** reviewer phải định nghĩa evidence requirement (mục 6 của REGRADE #1) **trước**, implementation đáp ứng **sau**. Tạo schema bây giờ là thiết kế evidence để khớp với kết quả mong muốn — làm hỏng giá trị của REGRADE #2.
>
> **Frozen:** `DESIGN-BRIEF-v2-frozen.md` · SHA-256 `f2a7c054b85f34961091401da8881b1dfb29aecdd004766df5c47811830cf137`

---

## 1. Cơ chế đã verify (không phải giả định)

Tôi đã đọc tài liệu gốc, không suy đoán:

| Cơ chế | Xác nhận | Nguồn |
|---------|---------|--------|
| `pytest --junitxml=PATH` | ✅ Có | pytest docs |
| `junit_family=legacy\|xunit1\|xunit2` | ✅ Có, `xunit2` hợp lệ | pytest docs + kiểm tra cục bộ (**pytest 9.1.1** đã có sẵn) |
| GH Actions artifact trả SHA-256 digest | ✅ Có | upload-artifact README: *"The Digest column will display the SHA256 digest of the artifact being uploaded"* |
| Artifact bất biến (immutable) | ✅ Có, nhưng **ghi đè được** (khi overwrite sẽ có **ID mới**, bản cũ không còn tồn tại) | upload-artifact README |

⚠️ **Hệ quả của "overwrite tạo ID mới":** không thể dựa vào digest của *artifact trên CI* để làm evidence lâu dài. Digest đó chỉ có giá trị trong đúng một workflow run. Vì vậy **evidence lâu dài phải tự sinh digest cục bộ** (SHA-256 của file trong repo), còn digest của CI chỉ dùng để chống bị can thiệp trong lúc chạy.

---

## 2. Cấu trúc dự kiến — SẼ TẠO SAU khi có mục (6)

⚠️ **Cấu trúc này là dự kiến, chưa phải bản cuối.** Mục (6) của REGRADE #1 có thể yêu cầu claim khác, hoặc gộp/khác cấu trúc. Khi đó **spec của reviewer thắng**, không phải cấu trúc này.

```
docs/
├── regrade-01-baseline.md        # kết quả chấm #1 + mục (6)
├── verify_matrix.py              # máy kiểm chứng claim
└── evidence/
    ├── manifest.json             # claim → repo → commit → command → kết quả → artifact → digest
    ├── factory/
    │   ├── tests/junit.xml
    │   ├── benchmarks/benchmark.json
    │   ├── logs/
    │   └── hashes.sha256
    ├── maia/
    │   ├── tests/junit.xml
    │   ├── rag-eval/
    │   ├── mcp/
    │   ├── agent/
    │   └── hashes.sha256
    └── apexinspect/
        ├── tests/junit.xml
        ├── benchmarks/{latency.json,throughput.json}
        ├── failure-tests/
        └── hashes.sha256
```

### Metadata tối thiểu mỗi evidence item

```json
{
  "claim_id": "D3-FACTORY-BATCH",
  "repo": "Factory-Data-Automation",
  "commit_sha": "<git rev-parse HEAD>",
  "command": "pytest --junitxml=docs/evidence/factory/tests/junit.xml",
  "executed_at": "<ISO-8601 UTC>",
  "exit_code": 0,
  "artifact": "tests/junit.xml",
  "sha256": "<digest file sau khi chạy>",
  "status": "PASS"
}
```

**Trường bắt buộc thêm so với đề xuất ban đầu:**

| Trường | Vì sao cần |
|--------|------------|
| `evidence_version` | Để schema có thể migrate mà không phá verifier cũ |
| `env` (python version, OS, commit của từng repo) | Không có thì "reproduce được" là vô nghĩa |
| `duration_s` | Phân biệt test nhanh với test bị treo |
| `notes` | Chỗ ghi claim **không** reproduce được, thay vì giấu |

---

## 3. Chu trình trạng thái — 5 mức

```text
✅ VERIFIED   evidence tồn tại, commit khớp, artifact còn hợp lệ, PASS
🟡 PARTIAL    ô 🟡 trong ma trận — đã test được nhưng còn thiếu thành phần
❌ FAILED     đã chạy và thất bại
⚪ NOT_RUN    chưa chạy
🟠 STALE      từng PASS, nhưng commit hiện tại ≠ commit sinh evidence
```

**`STALE` là mức quan trọng nhất về dài hạn** — nó biến ma trận kiến trúc từ bảng Markdown tĩnh thành **living verification matrix**. Một claim PASS từ 6 tháng trước, trên commit khác, **không còn là VERIFIED**.

### Quy tắc xác định STALE

```
claim.evidence.commit_sha  ≠  repo HEAD hiện tại
                          → STALE (không phải VERIFIED, dù test từng xanh)
```

Điều này cũng bảo vệ chống việc vô tình báo cáo điểm cũ.

---

## 4. Luồng kiểm của `verify_matrix.py`

Không "tin" README. Mỗi claim đi qua 5 bước:

```
claim
  ↓  evidence có tồn tại trong manifest.json không?    → không: ⚪ NOT_RUN
  ↓  commit_sha khớp repo HEAD?                        → không: 🟠 STALE
  ↓  artifact có tồn tại + SHA-256 khớp ghi trong manifest?  → không: 🟠 STALE / ❌ FAILED
  ↓  exit_code == 0?                                    → không: ❌ FAILED
  ↓  claim có đủ thành phần bắt buộc?                 → thiếu: 🟡 PARTIAL
✅ VERIFIED
```

`verify_matrix.py` chỉ báo cáo trạng thái. **Không tự suy luận pass từ việc file tồn tại.**

---

## 5. Thứ tự thực thi sau khi có spec

```text
REGRADE #1 trả về mục (6)
   ↓
Đối chiếu spec với cấu trúc §2 ở đây → điều chỉnh nếu lệch
   ↓
Tạo schema + manifest rỗng + verify_matrix.py
   ↓
Chạy Factory  → evidence + commit SHA
   ↓
Chạy MAIA     → evidence + commit SHA
   ↓
Chạy ApexInspect → evidence + commit SHA
   ↓
verify_matrix.py xuất bảng trạng thái 12×7 mới
   ↓
REGRADE #2 (cùng rubric 100 điểm, kèm evidence)
```

**Quy tắc bất di bất dịch:** không thêm claim nào ngoài spec của mục (6). Nếu tôi nghĩ ra claim hay thêm vào → ghi vào backlog P2, **không** làm ngay. Nếu làm ngay thì vòng chấm sau không còn là mốc đo trung lập.

---

## 6. Rủi ro đã biết

| Rủi ro | Giảm thiểu |
|--------|------------|
| Test fail vì thiếu dependency/env | Ghi `NOT_RUN` + lý do, **không** sửa test cho xanh rồi báo PASS |
| Benchmark không reproduce | Ghi rõ kèm điều kiện máy; không so với con số 43.36ms cũ như thể cùng môi trường |
| Sửa code làm cũ hỏng evidence cũ | Đó chính là `STALE` hoạt động đúng — không phải bug |
| Evidence tăng điểm nhưng thực chất không nâng năng lực | REGRADE #2 phải hỏi *"claim nào được chứng minh, claim nào chỉ được viết ra"* |

