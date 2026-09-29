# HANDOFF — Dọn cấu trúc thư mục 2026-09-29

Gửi: agent dọn dẹp → **tất cả agent khác đang làm các Phase**.
Kênh chính vẫn là `docs/execution-state.md`; file này chỉ báo thay đổi cấu trúc.

## Đã làm (an toàn, không xóa dữ liệu)

1. **Root gọn:** 6 file `.md` lẻ (`DATA-ARCHITECTURES`, `DESIGN-BRIEF`,
   `DESIGN-BRIEF-v2-frozen`, `P0-BACKLOG`, `PORTFOLIO-ARCHITECTURE`,
   `REGRADE-PROMPT`) → `docs/briefs/`. Root giờ chỉ còn các repo con + `docs/` + `k8s-manifests/`.
2. **`.gitignore` mở rộng:** venv (5 cái), `__pycache__`, `*.pyc`, `.ruff_cache`,
   `.airflow/logs/`, `*.db-shm/wal`, `.DS_Store`, `.vscode/`. Trước đó chỉ có 1 dòng `.airflow/`.
3. **Xóa `docs/__pycache__/`** (regenerable).
4. **Tạo `README.md`** ở root Projects (trước đó không có).

## Cố tình KHÔNG động vào — đừng "dọn giúp"

- `.venv-*` (~1.7GB): `.venv-airflow` là Python 3.11 bắt buộc cho Airflow 2.x;
  `.venv-apex` không có pytest. Xóa là test đỏ hàng loạt.
- `MAIA/` (dirty) vs `MAIA-clean/` (canonical): lệch 28 files **có chủ đích**
  theo `execution-state.md` Phase 8. Không gộp, không xóa.
- `docs/_gen_*.py`, `_verify_*.py`, `_p*.py`, `_commit_msg*.txt`: đang bị lẫn với
  tài liệu nhưng `execution-state.md` trỏ cứng `docs/_gen_*.py`. Dời = gãy link
  + gãy generator evidence. Muốn dời thì phải sửa reference trong
  `execution-state.md` cùng lúc — để lại cho quyết định chung.
- `DESIGN-BRIEF.md` trùng byte với `DESIGN-BRIEF-v2-frozen.md`: chưa xóa, chờ
  chủ repo xác nhận bản nào là chuẩn.
- `docs/evidence/`: commit có chủ đích, không phải rác.

## Cảnh báo quan trọng cho mọi agent

- **Git root = HOME (`/Users/mainguyenbinhtan`), KHÔNG phải `Projects/`.**
  Đã kiểm chứng bằng `git rev-parse --show-toplevel`.
  → Chỉ commit bên trong từng repo con. Không bao giờ `git add .` ở home.
- Generator evidence vẫn gọi theo path cũ `docs/_*.py` — giữ nguyên cho tới khi
  refactor có kế hoạch.
- Brief cũ đọc ở `docs/briefs/`, không còn ở root. Script nào hardcode path root
  `*.md` sẽ gãy — grep lại trước khi chạy.
