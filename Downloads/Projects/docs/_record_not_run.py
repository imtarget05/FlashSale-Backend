#!/usr/bin/env python
"""
Ghi lại phase nào chạy được và phase nào không, kèm lệnh đã thử và lỗi cụ thể.

Mục đích: một phase `NOT_RUN` chỉ đáng tin khi ta ghi lại ĐÃ THỬ GÌ, và
lỗi là gì. "Không có credential" mà không kèm lệnh đã chạy thì giống như
chưa thử — và không thể phân biệt với việc bỏ qua.

Chạy: python docs/_record_not_run.py
"""
from __future__ import annotations

import json
import os
import subprocess
from datetime import datetime, timezone

REPO = "/Users/mainguyenbinhtan/Downloads/Projects"
EV = os.path.join(REPO, "docs", "evidence", "p15-p16-cloud")


def probe(cmd: list) -> dict:
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=90)
        return {
            "command": " ".join(cmd),
            "exit_code": r.returncode,
            "stdout_head": r.stdout.strip()[:400],
            "stderr_head": r.stderr.strip()[:400],
        }
    except (OSError, subprocess.SubprocessError) as exc:
        return {"command": " ".join(cmd), "error": f"{type(exc).__name__}: {exc}"}


def main() -> int:
    os.makedirs(EV, exist_ok=True)
    stamp = datetime.now(timezone.utc).isoformat()

    aks = probe(["kubectl", "--context", "aks-portfolio-dev", "get", "nodes"])
    aks_az = probe(["az", "aks", "list", "-o", "json"])
    databricks = probe(["az", "resource", "list", "--resource-type",
                        "Microsoft.Databricks/workspaces", "-o", "json"])
    kind_ctx = probe(["kubectl", "config", "get-contexts"])

    report = {
        "generated_at": stamp,
        "subscription": "a3deec78-7edb-41cd-9e94-ec1d4d9379f5 (AzureCloud)",
        "phases": {
            "15_databricks": {
                "status": "NOT_RUN",
                "exact_reason": (
                    "Khong co Databricks workspace trong subscription. "
                    "Khong co DATABRICKS_HOST/DATABRICKS_TOKEN trong env."),
                "attempted": [databricks],
                "what_was_not_done": "Khong tao workspace. Tao workspace la "
                                     "tai nguyen co the phat sinh chi phi "
                                     "hang gio -> can user duyet.",
                "independent_work_continued": "Khong bi chan. Phase 16 chay "
                                             "duoc local bang kind.",
            },
            "17_aks": {
                "status": "NOT_RUN",
                "exact_reason": (
                    "Context aks-portfolio-dev ton tai trong kubeconfig nhung "
                    "API server khong tra loi: DNS fail. `az aks list` khong tra "
                    "ve cluster nao -> cluster da bi xoa hoac het han."),
                "attempted": [aks, aks_az],
                "what_was_not_done": "Khong tao AKS cluster. AKS tra phi phi "
                                     "hang gio va can resource approval.",
                "independent_work_continued": "Phase 16 dung kind chay local, "
                                             "verify cung mot bo manifest.",
            },
        },
        "kind_context_available": kind_ctx,
    }

    path = os.path.join(EV, "cloud_not_run.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2, ensure_ascii=False)
    print("wrote", os.path.relpath(path, REPO))
    for k, v in report["phases"].items():
        print(f"  {k}: {v['status']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
