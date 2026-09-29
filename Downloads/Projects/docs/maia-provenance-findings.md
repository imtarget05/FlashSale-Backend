# MAIA Provenance Findings — vì sao cần rerun CLEAN_HEAD

> **Ngày:** 2026-09-29 · Triggers bởi: 14 file dirty trong MAIA không có bằng chứng nguồn gốc
> **Kết luận ngắn:** chạy test trên dirty working tree đã tạo ra **2 claim sai**. Clean HEAD là canonical.

---

## 1. Vấn đề gốc

`MAIA/` có 14 file dirty (9 modified + 5 untracked) với mtime **07:56–07:58**, tức là **trước** khi verification bắt đầu (08:51). Không có bằng chứng cho biết chúng do ai hay tại sao.

Cách xử lý: dựng linked worktree sạch, **không** reset/stash/checkout working tree gốc.

```bash
git worktree add --detach ../MAIA-clean 54b87d89ef350fc24a31427fa265f5942f7684fc
```

Kết quả: `MAIA-clean` HEAD = `54b87d8…`, `git status --porcelain=v1` = **0 dòng**. `MAIA/` gốc vẫn nguyên 14 file dirty.

---

## 2. So sánh hai bằng chứng

| | DIRTY snapshot | CLEAN HEAD |
|---|---|---|
| **Vị trí evidence** | `docs/evidence/maia/` | `docs/evidence/maia-clean-head/` |
| **evidence_version** | 1.0 | **2.0** |
| **Vai trò** | lịch sử / chẩn đoán | **canonical / reproducible** |
| **Full suite** | 895 passed, 0 failed | **823 passed, 0 failed** |
| **Chênh lệch** | +72 test do 5 file untracked | baseline thật |
| **Trạng thái** | GIỮ LẠI, không xoá | dùng cho claim |

---

## 3. PHÁT HIỆN: 5 file untracked chỉ tồn tại ở dirty tree

| File | clean HEAD | dirty |
|---|---|---|
| `tests/test_mcp_resources_prompts.py` | ❌ không có | ✅ có |
| `tests/test_mcp_dispatch_prompts.py` | ❌ không có | ✅ có |
| `tests/test_session_registry.py` | ❌ không có | ✅ có |
| `src/maia/agent/session_registry.py` | ❌ không có | ✅ có |
| `src/maia/mcp/servers/prompts_server.py` | ❌ không có | ✅ có |

---

## 4. ERRATA #1 BỊ VÓ — tôi đã kết luận sai

**Tôi nói trong REGRADE #2:**
> "MAIA ĐÃ CÓ `resources/*` và `prompts/*` — 28/28 test pass"

**Điều đó chỉ đúng với dirty tree.** Probe trực tiếp trên CLEAN HEAD:

```
initialize         OK   2025-06-18
tools/list         OK   ['tools']
resources/list     -32601 unknown method 'resources/list'
prompts/list       -32601 unknown method 'prompts/list'
server/discover    -32601 unknown method 'server/discover'
```

`src/maia/mcp/protocol.py` ở clean HEAD **không hề có** `METHOD_RESOURCES_LIST` hay `METHOD_PROMPTS_LIST` — chỉ có `initialize`, `initialized`, `ping`, `tools/list`, `tools/call`.

**Sự thật đúng:**
> Committed code (HEAD `54b87d8`) chỉ có **tools capability**. Resources/prompts là **tính năng chưa commit**, tồn tại trong working tree.

**Nếu không rerun clean HEAD, tôi sẽ mang claim sai này vào portfolio** — và nó sẽ fail ngay khi interviewer clone repo.

---

## 5. Ảnh hưởng tới REGRADE #2

| Tiêu chí | Điểm #2 cũ | Điểm #2 đúng | Lý do |
|---|---|---|---|
| #1 Chính xác kỹ thuật | 17 | **16** | Errata #1 không còn là errata — tôi đã "sửa" sai. Capability thật là tools-only. |
| #6 MCP/RAG/Agent | 9 | **8** | Đánh giá dựa trên dirty tree → không reproduce được trên HEAD |

**REGRADE #2 điều chỉnh: 87 → 86/100**

Chi tiết trong `docs/regrade-02-implementation-backed.md`.

---

## 6. Bài học

**Bằng chứng chỉ có giá trị nếu reproduce được từ committed code.**

| Working tree | Reproduce từ HEAD | Giá trị |
|---|---|---|
| dirty | ❌ không | chẩn đoán |
| clean | ✅ có | canonical |

Cùng một commit SHA, nhưng **working tree khác nhau cho ra kết luận khác nhau** — và cả hai đều trông hợp lệ nếu không kiểm tra.

Đây là loại lỗi mà `git status --porcelain` bắt được trong 1 giây, nhưng mất hàng giờ nếu không nghĩ tới.

---

## 7. Việc cần quyết định

5 file untracked kia trông như **tính năng đang làm dở** (session registry, MCP prompts server). Ba lựa chọn:

| Lựa chọn | Hành động | Hệ quả |
|----------|-----------|--------|
| **A. Giữ dirty** | Commit 14 file vào branch mới | Clean HEAD có resources/prompts. Claims của tôi lại đúng. |
| **B. Bỏ dirty** | `git worktree remove` + xoá 5 file untracked | HEAD sạch là canonical. Phải cập nhật claim về tools-only. |
| **C. Giữ nguyên** | Không đụng, dùng clean HEAD làm canonical | Đã làm hiện tại. 5 file mất dấu vết khi clone. |

**Hiện tại: C** — an toàn nhất, không mất gì, và claim đã được sửa cho đúng.
