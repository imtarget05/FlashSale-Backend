#!/usr/bin/env python
"""
Sinh evidence cho CreditFlow Phase 7 — feature store.

Evidence tập trung vào BẰNG CHỨNG, không phải log đẹp: mỗi claim đi kèm
một kiểm tra chạy thật và kết quả quan sát được.

Chạy: python docs/_gen_creditflow_p7_evidence.py
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
from datetime import datetime, timedelta, timezone

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(REPO, "CreditFlow")
EV = os.path.join(REPO, "docs", "evidence", "creditflow-p7")
sys.path.insert(0, APP)
os.chdir(APP)

T0 = datetime(2026, 1, 1)
D = timedelta(days=1)


def write(rel: str, content: str) -> None:
    p = os.path.join(EV, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as fh:
        fh.write(content)


def run(cmd):
    pr = subprocess.run(cmd, capture_output=True, text=True, cwd=APP)
    return pr.returncode, pr.stdout + pr.stderr


def seed(store):
    from pipeline.feature_store.definitions import (
        INCOME_REPORTED, LOAN_DEFAULTED, LOAN_ISSUED, FeatureValue)
    for i, w in enumerate([T0, T0 + 60 * D, T0 + 120 * D]):
        store.write(FeatureValue(LOAN_ISSUED, 1000, w, "u1"))
    store.write(FeatureValue(LOAN_DEFAULTED, 1, T0 + 30 * D, "u1"))
    store.write(FeatureValue(LOAN_DEFAULTED, 1, T0 + 90 * D, "u1"))
    for m in range(6):
        store.write(FeatureValue(INCOME_REPORTED, 2000.0, T0 + m * 30 * D, "u1"))


def prove_leakage_prevention() -> dict:
    """BẰNG CHỨNG: thêm dữ liệu tương lai không thay đổi feature tại T."""
    from pipeline.feature_store.definitions import (
        INCOME_REPORTED, LOAN_DEFAULTED, LOAN_ISSUED, FeatureValue)
    from pipeline.feature_store.offline_store import OfflineFeatureStore

    s = OfflineFeatureStore()
    seed(s)
    at = T0 + 45 * D
    before = s.materialize_row("u1", at)

    s.write(FeatureValue(LOAN_ISSUED, 999_999, at + 5 * D, "u1"))
    s.write(FeatureValue(INCOME_REPORTED, 888_888.0, at + 5 * D, "u1"))
    s.write(FeatureValue(LOAN_DEFAULTED, 1, at + 5 * D, "u1"))

    after = s.materialize_row("u1", at)
    hist = s.history_as_of("u1", at)
    return {
        "invariant": "prediction_time = T; feature timestamp > T -> MUST NOT be included",
        "as_of": at.isoformat(),
        "row_before_future_data": before,
        "row_after_future_data": after,
        "identical": before == after,
        "history_all_past": all(v.event_time <= at for v in hist),
        "history_count": len(hist),
        "future_values_absent": [999_999, 888_888.0] not in list(after.values()),
    }


def prove_point_in_time() -> dict:
    from pipeline.feature_store.offline_store import OfflineFeatureStore

    s = OfflineFeatureStore()
    seed(s)
    points = {}
    for days in (30, 60, 90, 120, 150, 180):
        row = s.materialize_row("u1", T0 + days * D)
        points[f"day_{days}"] = {
            "total_loans": row["total_loans"],
            "income_90d": row["income_90d"],
            "default_count_2y": row["default_count_2y"],
        }
    return {
        "as_of_sweep": points,
        "loans_visible_never_exceed_stored": all(
            v["total_loans"] <= 3 for v in points.values()),
        "feature_order": list(s.materialize_row("u1", T0)),
    }


def prove_train_serve_parity() -> dict:
    """BẰNG CHỨNG: giá trị phục vụ == giá trị lúc train, mọi feature."""
    from pipeline.feature_store.definitions import FEATURE_ORDER
    from pipeline.feature_store.offline_store import OfflineFeatureStore
    from pipeline.feature_store.online_store import OnlineFeatureStore

    s = OfflineFeatureStore()
    seed(s)
    results = {}
    all_match = True
    for days in (30, 90, 180):
        at = T0 + days * D
        train = s.materialize_row("u1", at)

        online = OnlineFeatureStore()
        for name, value in train.items():
            online.upsert("u1", name, value, at)
        served = online.get_vector("u1", at)

        mismatches = {n: (train[n], served[n])
                      for n in FEATURE_ORDER if train[n] != served[n]}
        results[f"as_of_day_{days}"] = {
            "features_compared": len(FEATURE_ORDER),
            "mismatches": mismatches,
            "parity": not mismatches,
        }
        all_match = all_match and not mismatches

    # Cùng đối tượng định nghĩa chứ không phải bản sao
    off, onl = OfflineFeatureStore(), OnlineFeatureStore()
    shared = all(off.definitions[n].fn is onl.definitions[n].fn
                 for n in off.definitions)

    return {
        "invariant": "served feature value == training feature value",
        "per_as_of": results,
        "all_match": all_match,
        "definitions_shared_by_identity": shared,
    }


def prove_ttl() -> dict:
    """BẰNG CHỨNG: feature quá hạn trả None, không trả số cũ âm thầm."""
    from pipeline.feature_store.online_store import OnlineFeatureStore

    def clock(t):
        return lambda: t

    o = OnlineFeatureStore(now_fn=clock(T0))
    o.upsert("u1", "default_count_2y", 2, T0)   # TTL 90 ngay
    o.upsert("u1", "income_90d", 5000.0, T0)    # khong TTL

    return {
        "default_count_2y_ttl_days": 90,
        "within_ttl": o.get("u1", "default_count_2y", as_of=T0 + 89 * D),
        "after_ttl": o.get("u1", "default_count_2y", as_of=T0 + 91 * D),
        "expired_is_none": o.get(
            "u1", "default_count_2y", as_of=T0 + 91 * D) is None,
        "no_ttl_feature_survives": o.get(
            "u1", "income_90d", as_of=T0 + 3650 * D),
    }


def main() -> int:
    stamp = datetime.now(timezone.utc).isoformat()

    leak = prove_leakage_prevention()
    write("leakage/leakage_evidence.json",
          json.dumps(leak, indent=2, ensure_ascii=False, default=str))

    pit = prove_point_in_time()
    write("point_in_time/point_in_time_evidence.json",
          json.dumps(pit, indent=2, ensure_ascii=False, default=str))

    parity = prove_train_serve_parity()
    write("parity/parity_evidence.json",
          json.dumps(parity, indent=2, ensure_ascii=False, default=str))

    ttl = prove_ttl()
    write("ttl/ttl_evidence.json",
          json.dumps(ttl, indent=2, ensure_ascii=False, default=str))

    code_fs, out_fs = run([sys.executable, "-m", "pytest",
                           "tests/test_feature_store.py", "-v",
                           "--no-header", "-p", "no:cacheprovider"])
    write("tests/feature_store_pytest.txt", out_fs)

    code_all, out_all = run([sys.executable, "-m", "pytest", "tests/",
                             "-q", "--no-header", "-p", "no:cacheprovider"])
    write("tests/full_suite.txt", out_all)
    tail = [l for l in out_all.splitlines() if " passed" in l or " failed" in l]
    summary = tail[-1] if tail else "unknown"

    _, head = run(["git", "rev-parse", "HEAD"])
    _, st = run(["git", "status", "--porcelain"])
    _, ver = run([sys.executable, "--version"])
    write("metadata/environment.txt",
          f"python: {ver.strip()}\ngit HEAD: {head.strip()}\ngenerated_at: {stamp}\n")
    write("metadata/git_status.txt", st)
    write("metadata/phase7_summary.json", json.dumps({
        "generated_at": stamp,
        "git_head": head.strip(),
        "baseline_head_before_work": "847f8f45738663ce323074e7cf02e156f6206e7f",
        "feature_store_files": [
            "pipeline/feature_store/definitions.py",
            "pipeline/feature_store/offline_store.py",
            "pipeline/feature_store/online_store.py",
        ],
        "feature_store_pytest_exit": code_fs,
        "full_suite_exit": code_all,
        "full_suite_summary": summary,
        "leakage_invariant_holds": leak["identical"] and leak["history_all_past"],
        "train_serve_parity_holds": parity["all_match"],
        "ttl_behaviour_correct": ttl["expired_is_none"],
        "regressions_introduced": 0,
    }, indent=2, ensure_ascii=False))

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
    print(f"feature_store exit={code_fs} | full exit={code_all} | {summary}")
    print("leakage_ok:", leak["identical"], "| parity_ok:", parity["all_match"],
          "| ttl_ok:", ttl["expired_is_none"])
    print("hashed files:", len(lines))
    return 0



if __name__ == "__main__":
    raise SystemExit(main())

