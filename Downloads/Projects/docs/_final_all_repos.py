#!/usr/bin/env python
"""
FULL 7-REPO TEST PASS + final matrix.

Chạy suite của cả 7 repo trên HEAD hiện tại, ghi kết quả vào một bảng duy
nhất. Pre-existing failure phải được reproduce trên commit cũ — không gọi
là "pre-existing" chỉ vì nó đã ở đó.

Chạy: python docs/_final_all_repos.py
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import time
from datetime import datetime, timezone

REPO = "/Users/mainguyenbinhtan/Downloads/Projects"
EV = os.path.join(REPO, "docs", "evidence", "final")
FV = "/Users/mainguyenbinhtan/Downloads/Projects/Factory-Data-Automation-v-AI-Reporting-Platform/.venv-factory/bin/python"
MV = "/Users/mainguyenbinhtan/Downloads/Projects/MAIA-clean/.venv-maia/bin/python"
AV = "/Users/mainguyenbinhtan/Downloads/Projects/ApexInspect-AI/.venv-apex2/bin/python"
DSN_MAIA = os.environ.get("MAIA_POSTGRES_DSN", "")


def head(path):
    return subprocess.run(["git", "-C", path, "rev-parse", "--short", "HEAD"],
                          capture_output=True, text=True, timeout=30).stdout.strip()


def dirty(path):
    r = subprocess.run(["git", "-C", path, "status", "--porcelain"],
                       capture_output=True, text=True, timeout=60)
    return [l for l in r.stdout.splitlines() if l.strip()]


def run(cmd, cwd, env=None, timeout=1800):
    t0 = time.perf_counter()
    r = subprocess.run(cmd, capture_output=True, text=True, cwd=cwd,
                       timeout=timeout, env=env)
    out = r.stdout + r.stderr
    dur = time.perf_counter() - t0
    return r.returncode, out, round(dur, 1)


def parse(out, patterns):
    for name, rx in patterns.items():
        m = re.search(rx, out)
        if m:
            return int(m.group(1))
    return None


PY_PATTERNS = {
    "passed": r"(\d+) passed",
    "failed": r"(\d+) failed",
    "skipped": r"(\d+) skipped",
}
DOT_PATTERNS = {"passed": r"[ℹ✔] pass (\d+)", "failed": r"[ℹ✖] fail (\d+)"}
NET_PATTERNS = {
    "passed": r"Passed:\s*(\d+),\s*Failed:\s*\d+.*?Total:\s*\d+",
    "failed": r"Failed:\s*(\d+),\s*Passed:\s*\d+",
    "skipped": r"Skipped:\s*(\d+)",
}

REPOS = [
    {"name": "CreditFlow", "dir": "CreditFlow", "runner": "pytest",
     "py": MV, "cmd": [MV, "-m", "pytest", "tests/", "-q", "-p", "no:cacheprovider"]},
    {"name": "Factory-Data-Automation", "dir": "Factory-Data-Automation-v-AI-Reporting-Platform",
     "runner": "pytest", "py": FV,
     "cmd": [FV, "-m", "pytest", "tests/", "-q", "-p", "no:cacheprovider"]},
    {"name": "ApexInspect-AI", "dir": "ApexInspect-AI", "runner": "node:test",
     "py": None, "cmd": [AV, "-m", "pytest", "tests/", "-q",
                         "-p", "no:cacheprovider"]},
    {"name": "MAIA", "dir": "MAIA-clean", "runner": "pytest", "py": MV,
     "cmd": [MV, "-m", "pytest", "tests/", "-q", "-p", "no:cacheprovider"],
     "env": {"MAIA_POSTGRES_DSN": DSN_MAIA} if DSN_MAIA else None},
    {"name": "MiniERP", "dir": "MiniERP-Manufacturing-Warehouse",
     "runner": "dotnet", "py": None,
     "cmd": ["dotnet", "test", "tests/MiniERP.Api.Tests", "--nologo", "-v", "q",
             "--filter", "Category!=Integration"],
     "env": {"ConnectionStrings__OracleDb":
             "User Id=probe;Password=probe;Data Source=127.0.0.1:1/FREEPDB1;"}},
    {"name": "FlashSale", "dir": "FlashSale-Backend", "runner": "dotnet",
     "py": None, "cmd": ["dotnet", "test", "tests/UnitTests", "--nologo", "-v", "q"]},
    {"name": "Enterprise-Helpdesk", "dir": "Enterprise-IT-Helpdesk-Lab",
     "runner": "node:test", "py": None, "cmd": ["npm", "test"],
     "cwd_sub": "internal-portal"},
]

EXTRA = [
    {"name": "manufacturing-contracts", "dir": "manufacturing-contracts",
     "runner": "pytest", "py": MV,
     "cmd": [MV, "-m", "pytest", "tests/", "-q", "-p", "no:cacheprovider"]},
    {"name": "spark-benchmark", "dir": "spark-benchmark", "runner": "pytest",
     "py": FV, "cmd": [FV, "-m", "pytest", "tests/", "-q", "-p", "no:cacheprovider"]},
]


def main() -> int:
    os.makedirs(EV, exist_ok=True)
    rows = []
    for spec in REPOS + EXTRA:
        path = os.path.join(REPO, spec["dir"])
        cwd = os.path.join(path, spec.get("cwd_sub", ""))
        env = {**os.environ, **spec["env"]} if spec.get("env") else None
        rc, out, dur = run(spec["cmd"], cwd, env)
        pat = (PY_PATTERNS if spec["runner"] == "pytest"
               else DOT_PATTERNS if spec["runner"] == "node:test"
               else NET_PATTERNS)
        p = parse(out, pat)
        f = parse(out, {"failed": pat["failed"]})
        s = parse(out, {"skipped": pat["skipped"]}) if "skipped" in pat else None
        rows.append({
            "repo": spec["name"],
            "commit": head(path),
            "dirty_files": len(dirty(path)),
            "runner": spec["runner"],
            "passed": p, "failed": f, "skipped": s or 0,
            "duration_s": dur,
            "exit": rc,
        })
        print(f"{spec['name']:32} {p} pass / {f} fail / {s or 0} skip "
              f"({dur}s) exit={rc}")

    with open(os.path.join(EV, "all_repos.json"), "w", encoding="utf-8") as fh:
        json.dump({
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "note": ("Mọi con số chạy trên HEAD hiện tại. Pre-existing failure "
                     "chỉ được gọi pre-existing nếu reproduce được trên commit "
                     "trước — xem pre_existing_checks."),
            "rows": rows,
        }, fh, indent=2, ensure_ascii=False)
    print("\nwrote", os.path.join(EV, "all_repos.json"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
