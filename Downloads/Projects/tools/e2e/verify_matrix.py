#!/usr/bin/env python3
"""Gộp mọi bằng chứng E2E thành ma trận, và báo PASS chỉ khi có file thật.

Về vì sao script này tự kiểm lại thay vì tin báo cáo
-----------------------------------------------------
Một bảng tổng hợp tay luôn có xu hướng trôi khỏi sự thật theo thời gian: một
dòng được viết khi chạy được, rồi code đổi, rồi dòng đó thành sai mà không ai
sửa. Nên ở đây mỗi ô trong ma trận được suy ra từ FILE evidence cụ thể, và
dòng nào không có file thì in `NO EVIDENCE` chứ không in PASS.

Đó cũng là lý do không có ô nào được ghi tay. Nếu muốn ép một dòng thành
PASS, cách duy nhất là tạo evidence tương ứng.

Cách đọc
--------
UNIT / INTEGRATION / E2E / AZURE là bốn cột độc lập, không suy ra nhau.
Một dự án có unit test xanh chưa có nghĩa E2E của nó đã chạy, và ngược lại.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
E2E = ROOT / "docs" / "evidence" / "e2e"
AZURE = ROOT / "docs" / "evidence" / "azure"

PASS, FAIL, NONE = "PASS", "FAIL", "NO EVIDENCE"

# (project, nguồn evidence unit, nguồn integration, nguồn E2E, nguồn Azure)
PROJECTS = {
    "CreditFlow": ("creditflow", None, "creditflow", "creditflow"),
    "Factory": ("factory", None, "factory", "factory"),
    "ApexInspect": ("apexinspect", "apexinspect", "apexinspect", None),
    "FlashSale": ("flashsale", "flashsale", "flashsale", None),
    "MAIA": ("maia", "maia", "maia", "maia"),
    "MiniERP": ("minierp", "minierp", "minierp", None),
    "Helpdesk": ("helpdesk", "helpdesk", "helpdesk", None),
}

# Các dòng không sinh ra từ file E2E, mà từ probe chuyên biệt đã chạy.
# Ghi rõ nguồn để không ai tưởng chúng là "unit test" của ai đó.
SPECIAL = {
    "Security E2E (negative paths)": "docs/evidence/azure/security/",
    "Observability E2E (3 apps)": "docs/evidence/azure/observability/",
}


def status_of(path: Path) -> str:
    if not path.exists():
        return NONE
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (ValueError, OSError):
        return NONE
    if "status" in data:
        return PASS if data["status"] == "VERIFIED" else data["status"]
    if "failed" in data:
        return PASS if not data["failed"] else FAIL
    return PASS


def first_scenario(directory: Path) -> dict | None:
    if not directory.exists():
        return None
    for candidate in sorted(directory.glob("e2e-*.json")):
        if "trace" in candidate.name:
            continue
        try:
            return json.loads(candidate.read_text(encoding="utf-8"))
        except (ValueError, OSError):
            continue
    return None


def verdict(data: dict | None) -> str:
    """Một scenario chỉ PASS khi chính nó nói VERIFIED."""
    if data is None:
        return NONE
    return PASS if data.get("status") == "VERIFIED" else str(
        data.get("status", NONE))


def main() -> int:
    print(f"{'PROJECT':16} {'UNIT':>12} {'INTEGRATION':>14} {'E2E':>12} {'AZURE':>12}")
    print("-" * 72)

    rows = {}
    for project, (unit_dir, int_dir, e2e_dir, azure_dir) in PROJECTS.items():
        unit = status_of(E2E / unit_dir / "unit.json") if unit_dir else NONE
        integration = (status_of(E2E / int_dir / "integration.json")
                       if int_dir else NONE)
        e2e = verdict(first_scenario(E2E / e2e_dir))
        if azure_dir:
            dep = AZURE / azure_dir / "deployment.json"
            azure = status_of(dep) if dep.exists() else NONE
        else:
            azure = "N/A"
        rows[project] = {"unit": unit, "integration": integration,
                         "e2e": e2e, "azure": azure}
        print(f"{project:16} {unit:>12} {integration:>14} {e2e:>12} {azure:>12}")

    print()
    for label, rel in SPECIAL.items():
        directory = ROOT / rel
        files = sorted(directory.glob("*.json")) if directory.exists() else []
        if not files:
            print(f"{label:34} {NONE}")
            continue
        parts = []
        for f in files:
            data = json.loads(f.read_text(encoding="utf-8"))
            name = f.stem.split("-")[0]
            parts.append(f"{name}={data.get('status', '?')}")
        print(f"{label:34} {', '.join(parts)}")

    out = {
        "generated_from": "docs/evidence/{e2e,azure}",
        "note": "Every cell is derived from an evidence file. A cell with no "
                "file behind it reads NO EVIDENCE, never PASS.",
        "projects": rows,
        "special": {label: sorted((ROOT / rel).glob("*.json")) and
                    [json.loads(f.read_text(encoding="utf-8")).get("status")
                     for f in sorted((ROOT / rel).glob("*.json"))]
                    for label, rel in SPECIAL.items()},
    }
    target = E2E / "final" / "matrix.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print(f"\nwritten: {target.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
