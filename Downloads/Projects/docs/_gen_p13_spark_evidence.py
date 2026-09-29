#!/usr/bin/env python
"""
Sinh evidence cho Phase 13 — Spark benchmark.

Chay: python docs/_gen_p13_spark_evidence.py
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
from datetime import datetime, timezone

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BENCH = os.path.join(REPO, "spark-benchmark")
EV = os.path.join(REPO, "docs", "evidence", "p13-spark")


def write(rel: str, content: str) -> None:
    p = os.path.join(EV, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as fh:
        fh.write(content)


def main() -> int:
    stamp = datetime.now(timezone.utc).isoformat()
    results_path = os.path.join(BENCH, "results.json")

    if not os.path.isfile(results_path):
        print("Thieu results.json — chay run_bench.py truoc", file=sys.stderr)
        return 1

    with open(results_path, encoding="utf-8") as fh:
        data = json.load(fh)

    rows = []
    for r in data["results"]:
        rows.append({
            "size": r["size"],
            "rows": r["rows"],
            "input_bytes": r["input_bytes"],
            "polars_best_s": r["polars"]["best_s"],
            "spark_best_s": r["spark"]["best_s"],
            "polars_all_s": r["polars"]["all_s"],
            "spark_all_s": r["spark"]["times_s"],
            "speedup_polars_over_spark": r["speedup_polars_over_spark"],
            "polars_python_peak_mb": r["polars"]["extra_peak_mb"],
            "spark_python_peak_mb": r["spark"]["extra_peak_mb"],
            "process_peak_rss_mb": r["process_peak_rss_mb"],
            "parity": r["parity"]["parity"],
            "parity_tolerance": r["parity"].get("float_tolerance"),
        })

    all_parity = all(r["parity"] for r in rows)
    winner = "polars" if rows[-1]["speedup_polars_over_spark"] > 1 else "spark"

    write("benchmark_results.json", json.dumps({
        "generated_at": stamp,
        "environment": data["environment"],
        "dataset": data["dataset"],
        "results": rows,
        "all_parity_ok": all_parity,
        "winner_at_every_size": winner,
        "winner_note": (
            "Polars thang o ca ba kich thuoc tren may nay. Day la ket qua THAT, "
            "khong phai ket qua mong doi — va no KHONG phai lý do de bo Spark. "
            "Spark thuoc ve bai toan khac: du lieu lon hon may nay, phan bo, va "
            "chay tren cluster."),
        "measurement_caveats": [
            "extra_peak_mb do bang tracemalloc, chi do Python heap. Spark chay "
            " tren JVM nen con so nay KHONG phan anh bo nho thuc cua Spark — "
            "process_peak_rss_mb moi la con so gan dung hon, va no la toan boc "
            "process (Spark JVM + Python).",
            "Thoi gian la 'best of N' sau warm-up. P50/mean co trong results.json.",
            "Cung mot may, cung mot Spark master (local[4]). Ket qua khong "
            "chuyen sang may khac ma khong do lai.",
            "O local[4] thi chua co y nghia gi ve hanh vi khi scale ngang — "
            "do la cau hoi rieng, khong suy ra duoc tu day.",
        ],
    }, indent=2, ensure_ascii=False))

    pr = subprocess.run(
        [sys.executable, "-m", "pytest", "tests/", "-q", "--no-header",
         "-p", "no:cacheprovider"],
        capture_output=True, text=True, cwd=BENCH, timeout=300)
    write("tests/pytest.txt", pr.stdout + pr.stderr)

    # copy results.json goc vao evidence
    with open(results_path, encoding="utf-8") as src:
        write("raw_results.json", src.read())

    files = []
    for root, _, names in os.walk(EV):
        for n in sorted(names):
            if n != "hashes.sha256":
                files.append(os.path.join(root, n))
    lines = []
    for f in sorted(files):
        with open(f, "rb") as fh:
            lines.append(f"{hashlib.sha256(fh.read()).hexdigest()}  "
                         f"{os.path.relpath(f, EV)}")
    with open(os.path.join(EV, "hashes.sha256"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")

    print("EVIDENCE:", os.path.relpath(EV, REPO))
    print("parity all OK:", all_parity, "| winner:", winner,
          "| tests exit:", pr.returncode)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
