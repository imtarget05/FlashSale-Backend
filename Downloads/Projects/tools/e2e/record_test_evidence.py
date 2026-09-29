#!/usr/bin/env python3
"""Ghi evidence test cho các repo dùng pytest hoặc `node --test`.

Điểm quan trọng: `skipped` được ghi riêng, không trộn vào `passed`. Một test
bị bỏ qua vì thiếu credential hoặc thiếu service không phải test đã pass, và
để nó biến mất khỏi báo cáo là cách nói dối khó phát hiện nhất.

Điểm thứ hai, và đây là lý do lần chạy này đáng ghi lại: MAIA đi từ
"832 passed / 1 failed" lên "842 passed / 0 failed" chỉ sau khi bật Qdrant
thật. Sáu test vốn bị skip vì lý do "Qdrant not available" hoá ra là các test
retrieval và tenant — tức là đúng những test bảo vệ business invariant và
tenant isolation. Một con số xanh chạy thiếu Qdrant đẹp hơn nhiều, và ít
giá trị hơn nhiều so với việc bật dependency lên và chạy thật.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "tools" / "e2e"))
from evidence import E2E_ROOT, EvidenceRun, refresh_hashes  # noqa: E402

ART = Path("/tmp/e2e-art")

SUITES = {
    "creditflow": {
        "repo": "CreditFlow",
        "artifact": "creditflow.txt",
        "command": "python -m pytest tests/ -q",
        "boundaries": {
            "feature-store-point-in-time": "REAL (sqlite-backed, in-process)",
            "postgres-checkpoint": "NOT_RUN (no Postgres in this environment)",
            "llm-provider-vcr": "NOT_RUN (cassette not recorded)",
            "frontend-build": "NOT_RUN (node_modules absent)",
        },
    },
    "maia": {
        "repo": "MAIA",
        "artifact": "maia.txt",
        "command": "QDRANT_URL=http://localhost:6333 python -m pytest tests/ "
                   "-q --ignore=tests/test_llama_index_dataplane.py",
        "boundaries": {
            "qdrant-vector-store": "REAL (qdrant container; retrieval and "
                                   "tenant isolation tests actually ran)",
            "llama-index-dataplane": "NOT_RUN (llama-index not installed; one "
                                     "test file excluded explicitly)",
            "opentelemetry": "NOT_RUN (sdk not installed)",
        },
    },
    "factory": {
        "repo": "Factory-Data-Automation-v-AI-Reporting-Platform",
        "artifact": "factory.txt",
        "command": "python -m pytest tests/ -q",
        "boundaries": {
            "pipeline-contracts-catalog": "REAL",
            "scale-10x": "REAL but UNSATISFIED: test_scale_and_perf.py "
                         "requires data/raw/production.csv with >1000 rows; "
                         "the committed fixture has 30, so the test fails on "
                         "its own precondition, not on pipeline behaviour",
            "parquet-smaller-than-csv": "REAL but UNSATISFIED: at 30 rows the "
                                        "parquet footer overhead exceeds the "
                                        "CSV, so the size assertion inverts",
            "llm-sql-tool": "NOT_RUN (needs LAN vLLM)",
        },
    },
    "apexinspect": {
        "repo": "ApexInspect-AI",
        "artifact": "apex.txt",
        "command": "python -m pytest tests/ -q",
        "boundaries": {
            "unit-and-integration": "REAL",
            "plc-bridge": "REAL (pymodbus against a simulated PLC, not "
                          "hardware)",
            "live-llm-rca": "NOT_RUN (needs a real GROQ_API_KEY and a "
                            "recorded cassette)",
        },
    },
    "helpdesk": {
        "repo": "Enterprise-IT-Helpdesk-Lab",
        "artifact": "helpdesk.txt",
        "command": "cd internal-portal && npm test",
        "boundaries": {"unit-and-integration": "REAL (JSON file persistence)"},
    },
}


def parse_pytest(text: str) -> dict:
    def grab(pattern, default=0):
        m = re.search(pattern, text)
        return int(m.group(1)) if m else default
    dur = re.search(r"in ([\d.]+)s", text)
    return {
        "passed": grab(r"(\d+) passed"),
        "failed": grab(r"(\d+) failed"),
        "skipped": grab(r"(\d+) skipped"),
        "duration_s": float(dur.group(1)) if dur else 0.0,
        "skip_reasons": re.findall(r"^SKIPPED \[\d+\] ([^\n]+)", text, re.M),
    }


def parse_node(text: str) -> dict:
    def grab(key):
        m = re.search(rf"^ℹ {key} (\d+)", text, re.M)
        return int(m.group(1)) if m else 0
    return {"passed": grab("pass"), "failed": grab("fail"),
            "skipped": grab("skipped"), "total": grab("tests"),
            "duration_s": grab("duration_ms") / 1000.0, "skip_reasons": []}


def main() -> int:
    for name, cfg in SUITES.items():
        raw = ART / cfg["artifact"]
        if not raw.exists():
            print(f"  [skip] {name}: {raw} missing")
            continue
        text = raw.read_text(encoding="utf-8", errors="ignore")
        stats = parse_node(text) if name == "helpdesk" else parse_pytest(text)

        dest = E2E_ROOT / name
        dest.mkdir(parents=True, exist_ok=True)
        artifact = dest / f"{name}-test-output.txt"
        artifact.write_text(text[-8000:], encoding="utf-8")

        with EvidenceRun(
            f"E2E-{name.upper()}", [ROOT / cfg["repo"]], "LOCAL",
            cfg["command"], boundaries=cfg["boundaries"],
            notes=f"{stats['passed']} passed, {stats['failed']} failed, "
                  f"{stats['skipped']} skipped",
        ) as run:
            run.check("the full suite has no failures", stats["failed"] == 0,
                      detail=f"{stats['passed']} passed, {stats['failed']} "
                             f"failed, {stats['skipped']} skipped, "
                             f"{stats['duration_s']:.1f}s",
                      expected="0 failed", actual=f"{stats['failed']} failed")
            if stats["skipped"]:
                run.check("no test was skipped", False, boundary="NOT_RUN",
                          detail="; ".join(stats["skip_reasons"][:3])
                          or "see artifact",
                          expected="0 skipped",
                          actual=f"{stats['skipped']} skipped")
            run.add_artifact(artifact)
            run.finalise(dest)

    hashes, count = refresh_hashes()
    print(f"  hashes: {count} artifacts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
