# HANDOFF — Gom file về đúng project 2026-09-29 (đợt 2)

Tiếp theo `_HANDOFF-2026-09-29-cleanup.md`. Root giờ CHỈ còn thư mục project + `docs/` + stub `k8s-manifests/`.

## Đã dời

| Từ root | Về |
|---|---|
| `.venv-airflow`, `.venv-factory`, `.airflow/` | `Factory-Data-Automation-v-AI-Reporting-Platform/` |
| `.venv-apex2` | `ApexInspect-AI/` (giữ tên cũ để ít gãy ref) |
| `.venv-maia` | `MAIA-clean/` (canonical base, không phải `MAIA/` dirty) |
| `10-maia-api.yaml` | `MAIA-clean/k8s/` |
| `20-creditflow-api.yaml` | `CreditFlow/k8s/` |
| `30-apexinspect-api.yaml` | `ApexInspect-AI/k8s/` |
| `40-factory-api-real.yaml` + `factory-data-src/` | `Factory/.../k8s/` |
| `Dockerfile.factory` + `requirements.factory.txt` | `Factory/.../docker/` (cạnh `Dockerfile` + `docker-compose.yml` sẵn có) |

## Đã xóa (có duyệt)

- `.venv-apex` (226M, py3.14, không pytest) — trùng với `.venv-apex2` (py3.12, có pytest). User chốt xóa.
- `README.md` root — user chốt xóa. Thông tin portfolio (bảng cấu trúc, cảnh báo git-root,
  danh sách việc chưa làm) đã chuyển vào `execution-state.md` mục này + file handoff này.

## Đã sửa theo (không là gãy)

- `docs/_final_all_repos.py`, `_gen_p14_evidence.py`, `_p14_airflow_probe.py`,
  `_p18_alert_cycle.py`, `_gen_manifest_apex.py`, `_gen_manifest_maia_clean.py`,
  `_p12b_durable_dedupe_probe.py` — trỏ venv path mới. Nhân tiện sửa luôn ref
  `.venv-apex` (đã xóa) trong `_gen_manifest_apex.py` → `.venv-apex2` mới.
- `Factory/.../.airflow/airflow.cfg` — `dags_folder`, `base_log_folder` theo path mới.
- `.gitignore` của Factory / ApexInspect-AI / MAIA-clean — thêm venv + `.airflow/` tương ứng.

## Cố tình GIỮ ở ngoài (user chốt)

- `docs/` shared — generator ghi cứng `docs/evidence`, dời là gãy.
- `k8s-manifests/00-namespace.yaml` — namespace chung portfolio, không thuộc project nào.
- `.kilo/worktrees/accidental-dime` — worktree do tool Kilo quản lý, đăng ký trong git home; dời tay là gãy.

## Lưu ý cho agent khác

- Venv bị dời chỗ → console scripts trong `<venv>/bin/*` (shebang absolute cũ) có thể gãy.
  Luôn gọi qua `<venv>/bin/python -m ...` (pytest, airflow, uvicorn...) thay vì gọi trực tiếp.
- ⚠️ Git root vẫn = HOME. Chỉ commit trong từng repo con. Venv/`.airflow` đã gitignore ở cả 2 tầng.
