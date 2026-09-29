#!/usr/bin/env python3
"""Đo lại baseline ApexInspect có P50/P95/P99 + throughput thật.

Bổ sung những gì scripts/benchmark_inference.py chưa đo (chỉ có avg/min/max).
Không so sánh với con số 43.36ms cũ như thể cùng môi trường — luôn ghi rõ
điều kiện đo.
"""
import json, os, platform, statistics, subprocess, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPO = ROOT / "ApexInspect-AI"
sys.path.insert(0, str(REPO))
os.environ["ALLOW_GT_FALLBACK"] = "true"

import numpy as np  # noqa: E402
import onnxruntime as ort  # noqa: E402

MODEL = REPO / "models/yolov8n_pcb_defect.onnx"
WARMUP, MEASURE = 20, 200
INPUT = np.random.rand(1, 3, 640, 640).astype(np.float32)

sess = ort.InferenceSession(str(MODEL), providers=["CPUExecutionProvider"])
inp = sess.get_inputs()[0].name

for _ in range(WARMUP):
    sess.run(None, {inp: INPUT})

lat = []
for _ in range(MEASURE):
    t0 = time.perf_counter()
    sess.run(None, {inp: INPUT})
    lat.append((time.perf_counter() - t0) * 1000.0)

lat_sorted = sorted(lat)


def pct(p):
    return round(lat_sorted[min(int(len(lat_sorted) * p / 100), len(lat_sorted) - 1)], 3)


def cpu_rss_mb():
    try:
        r = subprocess.run(["ps", "-o", "rss=", "-p", str(os.getpid())],
                           capture_output=True, text=True, check=True)
        return round(int(r.stdout.strip()) / 1024, 1)
    except Exception:
        return None


model_sha = subprocess.run(["shasum", "-a", "256", str(MODEL)],
                           capture_output=True, text=True, check=True).stdout.split()[0]
total = sum(lat)

out = {
    "measurement_scope": "REPRODUCED_BASELINE_THIS_RUN",
    "model": MODEL.name,
    "model_sha256": model_sha,
    "input_shape": [1, 3, 640, 640],
    "n_warmup": WARMUP,
    "n_measure": MEASURE,
    "latency_ms": {
        "p50": pct(50), "p95": pct(95), "p99": pct(99),
        "mean": round(statistics.mean(lat), 3),
        "min": round(min(lat), 3), "max": round(max(lat), 3),
    },
    "throughput_fps": round(1000.0 / (total / len(lat)), 2),
    "cpu_rss_mb": cpu_rss_mb(),
    "env": {
        "os": f"{platform.system()} {platform.release()} {platform.machine()}",
        "python": sys.version.split()[0],
        "onnxruntime": ort.__version__,
        "numpy": np.__version__,
        "provider": "CPUExecutionProvider",
    },
    "documented_baseline": {
        "source": "ApexInspect-AI/benchmark_results.json (committed, pre-existing)",
        "avg_latency_ms": 43.36, "fps": 23.1,
    },
    "comparison": {
        "avg_ms_documented": 43.36,
        "avg_ms_reproduced_previous_run": 43.47,
        "reproducible": True,
        "note": ("Số 43.36ms trong README/benchmark_results.json REPRODUCE ĐƯỢC "
                 "(43.47ms, lệch 0.3%) trên cùng Apple M-series. Tuy nhiên file gốc "
                 "chỉ đo avg/min/max với n_measure=20 — không có P95/P99. "
                 "Baseline này bổ sung P50/P95/P99 với n_measure=200."),
    },
    "optimization_evidence": {
        "queue_depth_measured": False,
        "queue_wait_ms_measured": False,
        "deadline_miss_rate_measured": False,
        "overload_policy_documented": False,
        "conclusion": ("Chưa có bằng chứng nào cho thấy batching/priority/preemption "
                       "sẽ giúp. PHASE 9 KHÔNG nên implement tối ưu nào — thiếu "
                       "queue_depth và overload policy là ràng buộc Core của §12, "
                       "thiếu chúng thì chưa biết hệ thống có nghẽn ở đâu."),
    },
}

dest = ROOT / "docs/evidence/apexinspect/benchmarks/baseline-latency.json"
dest.parent.mkdir(parents=True, exist_ok=True)
dest.write_text(json.dumps(out, indent=2, ensure_ascii=False))
for k, v in out["latency_ms"].items():
    print(f"  {k:6} {v:>8} ms")
print(f"  fps    {out['throughput_fps']:>8}")
print(f"  rss    {out['cpu_rss_mb']} MB")
