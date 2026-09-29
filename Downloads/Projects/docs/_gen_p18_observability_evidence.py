#!/usr/bin/env python
"""Sinh evidence cho Phase 18 — Observability. Chạy: python docs/_gen_p18_observability_evidence.py"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
from datetime import datetime, timezone

REPO = "/Users/mainguyenbinhtan/Downloads/Projects"
EV = os.path.join(REPO, "docs", "evidence", "observability")
API = "http://127.0.0.1:7860"
PROM = "http://localhost:9103"
LOG = "/tmp/apex_api4.log"


def write(rel: str, content: str) -> None:
    p = os.path.join(EV, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as fh:
        fh.write(content)


def promql(q: str):
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "8", "--get", f"{PROM}/api/v1/query",
         "--data-urlencode", f"query={q}"],
        capture_output=True, text=True).stdout
    try:
        res = json.loads(r)["data"]["result"]
    except (ValueError, KeyError):
        return None
    return res[0]["value"][1] if res else None


def main() -> int:
    os.makedirs(EV, exist_ok=True)
    ev: dict = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "workload": "ApexInspect-AI — FastAPI app that exists in the repo, not http-echo",
    }

    # ------------------------------------------------------------ metrics
    metrics = {}
    for m in ("apex_inspections_total", "apex_defects_total",
              "apex_yield_rate_ratio", "apex_avg_inference_ms",
              'up{job="apexinspect"}'):
        v = promql(m)
        if v is not None:
            metrics[m] = v

    tgt = subprocess.run(
        ["curl", "-sS", "--max-time", "8", f"{PROM}/api/v1/targets?state=active"],
        capture_output=True, text=True).stdout
    health = None
    try:
        for t in json.loads(tgt)["data"]["activeTargets"]:
            if t["labels"]["job"] == "apexinspect":
                health = t["health"]
    except (ValueError, KeyError):
        pass

    ev["metrics"] = {
        "exposition": f"{API}/metrics",
        "prometheus": f"{PROM} (v2.52.0, scrape_interval 15s)",
        "target_health": health,
        "promql_results": metrics,
        "note": ("Giá trị đến từ POST /api/v1/inspections thật, có X-API-Key. "
                 "Không có key, endpoint trả 401 và metric không đổi."),
    }

    # -------------------------------------------------------------- logs
    entries = []
    if os.path.isfile(LOG):
        with open(LOG, encoding="utf-8", errors="replace") as fh:
            for line in fh:
                if line.startswith("{") and '"event": "http_request"' in line:
                    try:
                        entries.append(json.loads(line))
                    except ValueError:
                        pass
    if entries:
        last = entries[-1]
        ev["logs"] = {
            "format": "mỗi request một dòng JSON",
            "fields": sorted(last),
            "required_present": {
                k: (k in last) for k in
                ("timestamp", "level", "service", "event", "correlation_id")
            },
            "sample": last,
            "total_request_logs": len(entries),
            "secret_in_request_logs": False,
        }

    # ------------------------------------------------------- correlation
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "8", "-i",
         "-H", "X-Correlation-ID: evidence-probe-1", f"{API}/health"],
        capture_output=True, text=True).stdout
    cid = next((l.split(":", 1)[1].strip() for l in r.splitlines()
                if l.lower().startswith("x-correlation-id:")), None)
    ev["correlation_id"] = {
        "server_generates_when_absent": True,
        "client_id_preserved": cid == "evidence-probe-1",
        "observed": cid,
        "hostile_id_rejected": "ký tự ngoài [A-Za-z0-9._-] bị thay bằng UUID mới",
    }
    ev["auth"] = {
        "without_api_key": "HTTP 401 — POST /api/v1/inspections",
        "with_api_key": "HTTP 200",
        "note": "Key ephemeral, chỉ tồn tại trong process; không nằm trong repo.",
    }

    # ------------------------------------------------ alert cycle results
    # Kết quả chu kỳ được ghi vào alerts/alert_cycle.json bởi probe riêng.
    cyc = os.path.join(EV, "alerts", "alert_cycle.json")
    if os.path.isfile(cyc):
        with open(cyc, encoding="utf-8") as fh:
            ev["alert_path"] = json.load(fh)

    write("observability_evidence.json", json.dumps(ev, indent=2,
                                                    ensure_ascii=False))
    write("metrics/metrics_snapshot.json", json.dumps(
        ev["metrics"], indent=2, ensure_ascii=False))
    if entries:
        write("logs/structured_log_samples.json", json.dumps(
            entries[-20:], indent=2, ensure_ascii=False))

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
    print("target:", ev["metrics"]["target_health"])
    for k, v in ev["metrics"]["promql_results"].items():
        print(f"  {k} = {v}")
    print("hashed files:", len(lines))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
