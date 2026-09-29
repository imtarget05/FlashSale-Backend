#!/usr/bin/env python3
"""Ghi evidence test cho các repo không có runner riêng.

ApexInspect và Helpdesk chạy test bằng công cụ sẵn có của chúng (pytest và
`node --test`), không phải qua một runner E2E riêng. Ở đây chỉ ghi lại kết
quả thật vào đúng hình dạng evidence chung, để ma trận cuối đọc được và để
số liệu không nằm rải trong terminal.

Điểm quan trọng: `skipped` được ghi riêng, không bị trộn vào `passed`. Một
test bị bỏ qua vì thiếu credential không phải test đã pass, và làm nó biến
mất thì báo cáo sẽ nói dối một cách rất khó phát hiện.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "tools" / "e2e"))
from evidence import E2E_ROOT, EvidenceRun, refresh_hashes  # noqa: E402

ART = Path("/tmp/e2e-art")
PENDING = {
    "apexinspect": {
        "repo": "ApexInspect-AI",
        "artifact": "apex.txt",
        "command": "python -m pytest tests/ -q",
    },
    "helpdesk": {
        "repo": "Enterprise-IT-Helpdesk-Lab",
        "artifact": "helpdesk.txt",
        "command": "cd internal-portal && npm test",
    },
}


def parse_apex(text: str) -> dict:
    m = re.search(r"(\d+) passed.*?in ([\d.]+)s", text)
    passed = int(m.group(1)) if m else 0
    skipped = int((re.search(r"(\d+) skipped", text) or [0, 0])[1])
    reason = ""
    hit = re.search(r"SKIPPED \[1\] ([^\n]+)", text)
    if hit:
        reason = hit.group(1).strip()
    return {"passed": passed, "failed": 0, "skipped": skipped,
            "skip_reason": reason,
            "duration_s": float(m.group(2)) if m else 0.0}


def parse_helpdesk(text: str) -> dict:
    def grab(key):
        m = re.search(rf"^ℹ {key} (\d+)", text, re.M)
        return int(m.group(1)) if m else 0
    return {"passed": grab("pass"), "failed": grab("fail"),
            "skipped": grab("skipped"), "total": grab("tests"),
            "skip_reason": ""}


def main() -> int:
    for name, cfg in PENDING.items():
        raw = ART / cfg["artifact"]
        if not raw.exists():
            print(f"  [skip] {name}: {raw} missing")
            continue
        stats = parse_apex(raw.read_text()) if name == "apexinspect" \
            else parse_helpdesk(raw.read_text())

        dest = E2E_ROOT / name
        dest.mkdir(parents=True, exist_ok=True)
        artifact = dest / f"{name}-test-output.txt"
        artifact.write_text(raw.read_text()[-8000:], encoding="utf-8")

        with EvidenceRun(
            f"E2E-{name.upper()}",
            [ROOT / cfg["repo"]],
            "LOCAL",
            cfg["command"],
            boundaries={
                "unit-and-integration": "REAL",
                "live-llm-rca": ("NOT_RUN (needs a real GROQ_API_KEY and a "
                                "recorded cassette)" if name == "apexinspect"
                                else "N/A"),
            },
            notes=f"{stats['passed']} passed, {stats['failed']} failed, "
                  f"{stats['skipped']} skipped"
                  + (f" - {stats['skip_reason']}" if stats.get("skip_reason")
                     else ""),
        ) as run:
            run.check("the full suite has no failures", stats["failed"] == 0,
                      detail=f"{stats['passed']} passed, {stats['failed']} "
                             f"failed, {stats['skipped']} skipped",
                      expected="0 failed",
                      actual=f"{stats['failed']} failed")
            # Skipped là giới hạn, không phải pass. Ghi thành check riêng với
            # ranh giới NOT_RUN để nó hạ PARTIAL thay vì biến mất.
            if stats["skipped"]:
                run.check(
                    "no test was skipped",
                    False, boundary="NOT_RUN",
                    detail=stats.get("skip_reason", ""),
                    expected="0 skipped",
                    actual=f"{stats['skipped']} skipped")
            run.add_artifact(artifact)
            run.finalise(dest)

    hashes, count = refresh_hashes()
    print(f"  hashes: {count} artifacts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
