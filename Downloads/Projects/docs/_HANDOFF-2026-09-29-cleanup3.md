# HANDOFF — Dọn đợt 3, 2026-09-29 (user duyệt từng mục)

## Đã xóa

- `.venv-local/` (14M, chỉ pip+PyYAML, không file nào trỏ tới) — venv stray ở root.
- `docs/_commit_msg*.txt` (14 files) — message temp đã `git commit -F` xong, nội dung
  đã nằm trong lịch sử commit các repo con.
- `docs/__pycache__/` — regenerable (sinh ra từ lần verify `py_compile` trước).

## Đã sửa

- `.gitignore` (Projects): gộp 5 dòng venv lẻ thành `.venv-*/` — venv stray sau này
  tự bị ignore ở mọi tầng.

## Cố tình GIỮ (user chốt)

- `spark-benchmark`, `MAIA-clean`, `manufacturing-contracts` — repo đã commit sạch,
  xóa là mất phase 8/11/12/13. Chỉ kiểm tra status, không đụng.
  - `MAIA-clean` hiện có 2 thay đổi chưa commit (do đợt 2): `M .gitignore`
    (thêm `.venv-maia/`) + `?? k8s/` (manifest mới dời vào). Chủ repo commit khi sẵn sàng.
  - `spark-benchmark`, `manufacturing-contracts`: clean, không gì thay đổi.
- `k8s-manifests/` stub (`00-namespace.yaml` + README) — user bảo bỏ qua, để yên.
- Mọi script `docs/_*.py` giữ nguyên (đợt 2 đã sửa ref venv mới, verify CLEAN).

## Trạng thái root hiện tại

Chỉ còn 10 repo project + `docs/` + stub `k8s-manifests/`. Không còn venv,
không còn file lẻ, không còn `__pycache__` ở root và `docs/`.
