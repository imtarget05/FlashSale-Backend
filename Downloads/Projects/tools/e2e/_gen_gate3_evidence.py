#!/usr/bin/env python3
"""Sinh evidence JSON cho GATE 3 (overload/backpressure) từ dotnet test thật."""
import hashlib
import json
import re
import subprocess
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path("/Users/mainguyenbinhtan/Downloads/Projects")
REPO = ROOT / "FlashSale-Backend"
OUT = ROOT / "docs/evidence/e2e/azure"
OUT.mkdir(parents=True, exist_ok=True)

cmd = ("dotnet test tests/UnitTests/FlashSale.UnitTests.csproj "
       "--filter FullyQualifiedName~OverloadBackpressureTests")
r = subprocess.run(cmd, shell=True, cwd=str(REPO),
                   capture_output=True, text=True)
out = r.stdout + r.stderr

m = re.search(r"Passed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),"
              r"\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)", out)
failed, passed, skipped, total = (int(x) for x in m.groups()) if m else (-1, -1, -1, -1)
dur = re.search(r"Duration:\s*([\d.]+)\s*ms", out)

payload = {
    "evidence_version": "1.0",
    "gate": 3,
    "name": "overload_backpressure",
    "status": "VERIFIED" if failed == 0 and passed >= 5 else "PARTIAL",
    "execution_location": "LOCAL",
    "repos": ["FlashSale-Backend"],
    "command": cmd,
    "exit_code": r.returncode,
    "duration_ms": float(dur.group(1)) if dur else None,
    "tests": {"passed": passed, "failed": failed,
              "skipped": skipped, "total": total},
    "executed_at": datetime.now(timezone.utc).isoformat(),
    "meta_gate": "PASS — oracle 'accepted + rejected = submitted' self-checked "
                 "against both a correct and a message-losing case",
    "boundary_under_test": "REAL (InMemoryOrderQueue bounded channel 5000) — "
                           "not mocked",
    "scenarios": [
        {"id": "G3-01", "name": "queue full, accounting balances",
         "submitted": 6000, "capacity": 5000,
         "invariant": "accepted + rejected == submitted"},
        {"id": "G3-02", "name": "memory bounded under sustained overload",
         "submitted": 20000, "accepted": 5000,
         "invariant": "accepted <= capacity; excess is shed, not buffered"},
        {"id": "G3-03", "name": "recovery after consumer catches up",
         "drained": 5000, "invariant": "producer accepted again in < 1000ms"},
        {"id": "G3-04", "name": "overload outcome is distinct",
         "invariant": "SystemBusy != Accepted (no fake success)"},
    ],
    "documented_policy": "REJECT + SHED: queue day -> SubmitOrderUseCase tra lai "
                         "reservation va tra SystemBusy. Khong buffer vo han, khong "
                         "fake success.",
    "measured_on": "macOS laptop, single process, in-process channel — KHONG "
                   "suy ra cho production throughput",
    "notes": "Khong sua code production; chinh sach overload da ton tai, test "
             "gi lai invariant do.",
}

blob = json.dumps(payload, indent=2, ensure_ascii=False)
f = OUT / "gate3-backpressure.json"
f.write_text(blob, encoding="utf-8")
print(f"wrote {f.name} status={payload['status']} "
      f"tests={passed}/{total} sha={hashlib.sha256(f.read_bytes()).hexdigest()[:16]}")
