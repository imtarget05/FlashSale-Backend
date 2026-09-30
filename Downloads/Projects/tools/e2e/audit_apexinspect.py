"""ApexInspect canonical audit: real ONNX inference, latency percentiles,
trigger boundaries, fail-closed behaviour, corrupt model, invalid frame,
multi-camera overload.

Every number here is measured. Nothing is read from the README.
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path("/Users/mainguyenbinhtan/Downloads/Projects/ApexInspect-AI")
sys.path.insert(0, str(ROOT))
os.environ.setdefault("MODEL_PATH", str(ROOT / "models/yolov8n_pcb_defect.onnx"))

import numpy as np  # noqa: E402

from src.vision.detector import PCBDefectDetector  # noqa: E402

OUT = Path("/Users/mainguyenbinhtan/Downloads/Projects/docs/evidence/e2e/apex-canonical-audit.json")
RESULTS: list[dict] = []


def check(cid, desc, status, detail, oracle):
    RESULTS.append({"id": cid, "description": desc, "status": status,
                    "detail": detail, "oracle_source": oracle})
    print(f"  [{status}] {cid} {desc}: {detail}")


def pct(values, p):
    """Nearest-rank percentile. Defined explicitly so the number is auditable."""
    if not values:
        return None
    s = sorted(values)
    k = max(1, int(-(-p * len(s) // 100)))
    return round(s[k - 1], 3)


model = ROOT / "models/yolov8n_pcb_defect.onnx"
model_sha = hashlib.sha256(model.read_bytes()).hexdigest() if model.exists() else None
print(f"\n[A1] model integrity")
print(f"  model sha256={str(model_sha)[:16]}  bytes={model.stat().st_size if model.exists() else 0}")

det = PCBDefectDetector(model_path=str(model))
check("A1", "real ONNX model loads and reports non-degraded",
      "PASS" if det.use_onnx else "FAIL",
      f"use_onnx={det.use_onnx} degraded={det.degraded} "
      f"conf_threshold={det.conf_threshold} model={model.name} "
      f"sha256={str(model_sha)[:16]}",
      "onnxruntime InferenceSession on the committed .onnx artifact")

H, W = 640, 640
rng = np.random.default_rng(42)
frames = [rng.integers(0, 255, (H, W, 3), dtype=np.uint8) for _ in range(30)]

print("\n[A2] inference + latency percentiles on real frames")
# Warmup: first call pays graph-optimisation and allocator cost, which is not
# steady-state latency. Measuring it would overstate P99 for the wrong reason.
for f in frames[:5]:
    det.infer(f)
lat = []
n_det = 0
for f in frames:
    t0 = time.perf_counter()
    out = det.infer(f)
    lat.append((time.perf_counter() - t0) * 1000)
    # infer() -> (annotated_frame, detections|None, latency_ms|None).
    # None means "no verdict" and is deliberately different from [].
    n_det += len(out[1] or [])
p50, p95, p99 = pct(lat, 50), pct(lat, 95), pct(lat, 99)
print(f"  n={len(lat)}  P50={p50}ms  P95={p95}ms  P99={p99}ms  "
      f"max={round(max(lat),3)}ms  detections={n_det}")
check("A2", "ONNX inference runs on real frames and reports latency percentiles",
      "PASS" if p50 is not None and p50 > 0 else "FAIL",
      f"30 frames of 640x640x3, 5 warmup discarded. P50={p50}ms P95={p95}ms "
      f"P99={p99}ms max={round(max(lat),3)}ms. Percentiles are nearest-rank "
      f"over per-frame wall time, not a mean.",
      "time.perf_counter around det.detect(), nearest-rank percentile")
print("\n[A3] trigger threshold on the REAL detector")
# An earlier version of this check subclassed PCBDefectDetector and asserted against
# the stub's own logic. That proves the stub is self-consistent and says
# nothing about the product, so it was removed. This asserts on the real
# detector: the threshold must be a real parameter that changes authorised
# output, and it must come from configuration rather than a hardcoded literal.
import inspect  # noqa: E402

src_init = inspect.getsource(PCBDefectDetector.__init__)
thr_is_configurable = "conf_threshold" in src_init
obs = []
for thr in (0.0, 0.5, 0.95):
    d2 = PCBDefectDetector(model_path=str(model), conf_threshold=thr)
    hits = 0
    for f in frames[:10]:
        o = d2.infer(f)[1]
        if isinstance(o, tuple):
            o = o[0]
        hits += len(o or [])
    obs.append({"threshold": thr, "detections_over_10_frames": hits})
mono = all(obs[i]["detections_over_10_frames"] >= obs[i + 1]["detections_over_10_frames"]
           for i in range(len(obs) - 1))
check("A3", "confidence threshold is a real parameter on the production detector",
      "PASS" if thr_is_configurable and mono else "PARTIAL",
      f"threshold sweep on the real ONNX detector over 10 frames: {obs}. "
      f"Detections monotonically non-increasing in threshold={mono}; "
      f"conf_threshold configurable in __init__={thr_is_configurable}. On "
      f"random-noise frames the absolute counts are low, so this confirms the "
      f"trigger is wired and monotone, NOT that detection accuracy is good; "
      f"that would need labelled frames.",
      "real PCBDefectDetector threshold sweep, no stub subclass")

print("\n[A4] fail-closed on a corrupt model")
tmp = ROOT / "models/_audit_corrupt.onnx"
tmp.write_bytes(b"this is not a protobuf model, it is 12 bytes of junk")
try:
    bad = PCBDefectDetector(model_path=str(tmp))
    # A detector that cannot load its model must not silently claim to work.
    # Report exactly what it says so the contract is visible either way.
    check("A4", "corrupt model degrades instead of returning confident output",
          "PASS" if bad.degraded and not bad.use_onnx else "FAIL",
          f"corrupt model -> use_onnx={bad.use_onnx} degraded={bad.degraded}. "
          f"A detector that reports healthy on an unreadable model would let "
          f"a broken deploy pass as a working one.",
          "PCBDefectDetector on a non-protobuf .onnx file")
except Exception as exc:  # fail-closed by raising is also acceptable
    check("A4", "corrupt model degrades instead of returning confident output",
          "PASS",
          f"corrupt model raised {type(exc).__name__} at load time. Failing "
          f"loudly is the correct fail-closed behaviour.",
          "PCBDefectDetector on a non-protobuf .onnx file")
finally:
    tmp.unlink(missing_ok=True)

print("\n[A5] invalid frames")
bad_frames = {
    "empty_array": np.zeros((0, 0, 3), dtype=np.uint8),
    "wrong_dtype": np.zeros((64, 64, 3), dtype=np.float32),
    "grayscale_2d": np.zeros((64, 64), dtype=np.uint8),
    "single_pixel": np.zeros((1, 1, 3), dtype=np.uint8),
}
invalid = {}
for name, f in bad_frames.items():
    try:
        det.infer(f)
        invalid[name] = "returned normally"
    except Exception as exc:
        invalid[name] = f"{type(exc).__name__}"
raised = sum(1 for v in invalid.values() if v != "returned normally")

# Follow up on the one frame that did NOT raise. infer()'s own contract says
# detections=None means "no verdict could be reached" and [] means "inspected,
# no defects found". A quality-rejected frame is a no-verdict case, so it must
# return None. Trace what it actually returns, because [] and None are opposite
# safety signals for a defect-inspection system.
one_px = np.zeros((1, 1, 3), dtype=np.uint8)
res_1px = det.infer(one_px)
dets_1px = res_1px[1]
rejected_but_empty = dets_1px == []
check("A5b", "quality-rejected frame reports no-verdict, not a clean pass",
      "FAIL" if rejected_but_empty else "PASS",
      f"a 1x1 frame is rejected by check_frame (laplacian variance 0.0) yet "
      f"infer() returns detections={dets_1px!r}. infer() documents None as "
      f"'no verdict' and [] as 'inspected, no defects found', so a rejected "
      f"frame is currently reported to the caller as a PASS. For a defect "
      f"inspection system that is a false negative: a camera fault or blank "
      f"frame reads as a board with no defects. "
      f"{'Source: src/vision/detector.py:313 returns [] on the quality-reject branch.' if rejected_but_empty else 'Returns None as documented.'}",
      "real detector on a frame that check_frame rejects")

check("A5", "invalid frames are rejected rather than silently mis-detected",
      "PASS" if raised == len(bad_frames) else "PARTIAL",
      f"{raised}/{len(bad_frames)} malformed frames raised instead of "
      f"returning a detection: {invalid}. Any frame that returns normally is "
      f"reported here rather than counted as handled. See A5b for the case "
      f"that returns normally without raising.",
      "real detector on empty / float32 / 2-D / 1x1 frames")
print("\n[A6] multi-camera overload — does latency degrade gracefully?")
# One camera is a benchmark. A line has many. The question is not "is it fast"
# but "does throughput hold when concurrency rises, and does latency stay
# bounded" — a per-frame cost that grows linearly with camera count means one
# slow board stalls every other camera.
per_cam = {}
for n_cams in (1, 4, 8):
    t0 = time.perf_counter()
    for _ in range(n_cams):
        det.infer(frames[0])
    elapsed = (time.perf_counter() - t0) * 1000
    per_cam[n_cams] = {"total_ms": round(elapsed, 2),
                       "ms_per_camera": round(elapsed / n_cams, 2)}
# Sequential, so per-camera cost should stay roughly flat. A rising per-camera
# cost under sequential load would indicate per-request state growth.
costs = [v["ms_per_camera"] for v in per_cam.values()]
flat = costs[-1] <= costs[0] * 1.5
fps = {n: round(1000.0 / v["ms_per_camera"], 2) for n, v in per_cam.items()}
check("A6", "per-camera inference cost stays flat as camera count rises",
      "PASS" if flat else "PARTIAL",
      f"sequential load: {per_cam}. Per-camera cost {costs[0]}ms -> "
      f"{costs[-1]}ms across 1->8 cameras (within 1.5x: {flat}); implied "
      f"throughput {fps} fps. Flat cost means one board does not slow the "
      f"others. This is a SEQUENTIAL microbenchmark, not a real concurrent "
      f"line test, so it does not establish behaviour under thread or process "
      f"contention.",
      "real detector, 640x640 frame, sequential")

# ----------------------------------------------------------------- verdict
blocking = [c for c in RESULTS if c["status"] == "FAIL"]
partial = [c for c in RESULTS if c["status"] in ("PARTIAL", "NOT_RUN")]
status = "FAIL" if blocking else ("PARTIAL" if partial else "PASS")

import subprocess  # noqa: E402
head = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "HEAD"],
                      capture_output=True, text=True).stdout.strip()
dirty = subprocess.run(["git", "-C", str(ROOT), "status", "--porcelain"],
                       capture_output=True, text=True).stdout.strip()

payload = {
    "project": "ApexInspect-AI",
    "audit": "canonical revalidation",
    "status": status,
    "generated_at": datetime.now(timezone.utc).isoformat(),
    "commit": head,
    "working_tree_dirty": bool(dirty),
    "model": {"path": model.name, "sha256": model_sha,
              "bytes": model.stat().st_size if model.exists() else 0,
              "sha256_note": "model artifact is NOT covered by the git commit "
                             "unless tracked; verify separately"},
    "latency_ms": {"p50": p50, "p95": p95, "p99": p99,
                   "max": round(max(lat), 3), "n_frames": len(lat),
                   "warmup_discarded": 5,
                   "input": "random noise 640x640x3 — measures compute cost, "
                            "not detection accuracy"},
    "multi_camera": per_cam,
    "checks": RESULTS,
}
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(payload, indent=2))
met = sum(1 for c in RESULTS if c["status"] == "PASS")
print(f"\n{met}/{len(RESULTS)} checks PASS — ApexInspect audit {status}")
if blocking:
    print("failing: " + ", ".join(c["id"] for c in blocking))
print(f"evidence: {OUT}")
sys.exit(0 if status == "PASS" else 1)
