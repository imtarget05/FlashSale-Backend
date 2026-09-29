#!/usr/bin/env python3
"""verify_matrix.py — kiểm chứng claim kiến trúc bằng runtime evidence.

Nguồn claim: docs/regrade-01-baseline.md mục 7 (CLAIM CẦN EVIDENCE).
Script KHÔNG tự suy luận trạng thái từ việc file tồn tại.

Chu trình kiểm mỗi claim:
    claim tồn tại → evidence metadata → evidence_version → artifact tồn tại
      → SHA-256 khớp → commit khớp repo HEAD → runtime status
    → VERIFIED / PARTIAL / FAILED / NOT_RUN / STALE

Dùng:
    python3 docs/verify_matrix.py
    python3 docs/verify_matrix.py --json
    python3 docs/verify_matrix.py --claim D3-FACTORY-BATCH
    python3 docs/verify_matrix.py --phase factory
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EVIDENCE = ROOT / "docs" / "evidence"
EVIDENCE_VERSION_SUPPORTED = {"1.0"}


class Status(str, Enum):
    VERIFIED = "VERIFIED"
    PARTIAL = "PARTIAL"
    FAILED = "FAILED"
    NOT_RUN = "NOT_RUN"
    STALE = "STALE"


# --- Claim spec, lấy từ regrade-01-baseline.md mục 7 -----------------------
# expected    : trạng thái ma trận D.3
# must_prove  : thành phần cần runtime evidence
# known_missing: thành phần đã biết còn thiếu → không thể lên VERIFIED

CLAIMS: list[dict] = [
    # ---- 7.1 — 12 claim ✅ ----
    {"id": "D3-CREDITFLOW-BATCH", "repo": "CreditFlow", "sec": 1, "expected": "VERIFIED", "phase": "later"},
    {"id": "D3-FACTORY-BATCH", "repo": "Factory", "sec": 1, "expected": "VERIFIED", "phase": "factory",
     "must_prove": "ETL discover→load→clean→contract-gate→save end-to-end, sinh parquet ở data/silver/"},
    {"id": "D3-APEX-BATCH", "repo": "ApexInspect", "sec": 1, "expected": "VERIFIED", "phase": "apexinspect"},
    {"id": "D3-FLASHSALE-BATCH", "repo": "FlashSale", "sec": 1, "expected": "VERIFIED", "phase": "later"},
    {"id": "D3-MAIA-BATCH", "repo": "MAIA", "sec": 1, "expected": "VERIFIED", "phase": "maia",
     "must_prove": "doc ingestion file→chunk→Qdrant, search trả kết quả"},
    {"id": "D3-MINIERP-BATCH", "repo": "MiniERP", "sec": 1, "expected": "VERIFIED", "phase": "later"},
    {"id": "D3-FLASHSALE-STREAMING", "repo": "FlashSale", "sec": 2, "expected": "VERIFIED", "phase": "later",
     "must_prove": "producer→RabbitMQ→worker→PG; DLQ; 50req/10stock→10 sales"},
    {"id": "D3-MAIA-STREAMING", "repo": "MAIA", "sec": 2, "expected": "VERIFIED", "phase": "maia",
     "must_prove": "Kafka outbox scheduler/worker, restart từ offset không mất/săn message"},
    {"id": "D3-MINIERP-ELT", "repo": "MiniERP", "sec": 6, "expected": "VERIFIED", "phase": "later",
     "must_prove": "load thô → transform bằng SQL trong engine → 3NF"},
    {"id": "D3-CREDITFLOW-MLTRAINING", "repo": "CreditFlow", "sec": 11, "expected": "VERIFIED", "phase": "later",
     "must_prove": "train thật, metric sinh, model vào MLflow, PSI drift chạy"},
    {"id": "D3-FLASHSALE-REALTIME", "repo": "FlashSale", "sec": 12, "expected": "VERIFIED", "phase": "later",
     "must_prove": "Redis Lua CAS latency đo được; pool cap không vượt"},
    {"id": "D3-MAIA-REALTIME", "repo": "MAIA", "sec": 12, "expected": "VERIFIED", "phase": "maia",
     "must_prove": "RRF dense+BM25 có fused_score; evidence gate→refusal; citation resolve được file"},

    # ---- 7.2 — Data Lake / Warehouse / Lakehouse (7) ----
    {"id": "D3-FACTORY-LAKEHOUSE", "repo": "Factory", "sec": 5, "expected": "PARTIAL", "phase": "factory",
     "must_prove": "silver parquet snappy",
     "known_missing": "ACID, time travel, schema evolution → chưa phải lakehouse"},
    {"id": "D3-FACTORY-DATALAKE", "repo": "Factory", "sec": 7, "expected": "PARTIAL", "phase": "factory",
     "must_prove": "4 lớp ingestion/storage/processing/consumption + data contract + quarantine",
     "known_missing": "lớp ④ Catalog & Search; metadata catalog; lineage"},
    {"id": "D3-MAIA-DATALAKE", "repo": "MAIA", "sec": 7, "expected": "PARTIAL", "phase": "maia",
     "must_prove": "4 lớp cơ bản + HTTP cursor pagination",
     "known_missing": "lớp ④ metadata catalog; lineage end-to-end"},
    {"id": "D3-APEX-DATALAKE", "repo": "ApexInspect", "sec": 7, "expected": "PARTIAL", "phase": "apexinspect",
     "must_prove": "ảnh + kết quả detect được lưu",
     "known_missing": "không có ingestion/processing layer thật"},
    {"id": "D3-MAIA-ELT-PARTIAL", "repo": "MAIA", "sec": 6, "expected": "PARTIAL", "phase": "maia",
     "must_prove": "NL→SQL mini-warehouse",
     "known_missing": "chỉ 1 domain; chưa có transform layer hệ thống"},
    {"id": "D3-FACTORY-DW", "repo": "Factory", "sec": 8, "expected": "PARTIAL", "phase": "factory",
     "must_prove": "silver parquet + KPI export",
     "known_missing": "chưa có staging→marts (mart_oee/quality/inventory)"},
    {"id": "D3-MINIERP-DW", "repo": "MiniERP", "sec": 8, "expected": "PARTIAL", "phase": "later",
     "must_prove": "Oracle 3NF + staging package + dashboard",
     "known_missing": "chưa có marts cho BI → operational DB, chưa phải warehouse"},

    # ---- 7.3 — Data Mesh L1 (6) ----
    {"id": "D3-CREDITFLOW-MESH", "repo": "CreditFlow", "sec": 9, "expected": "PARTIAL", "phase": "later",
     "known_missing": "thiếu L2: catalog, SLO, access policy, federated governance"},
    {"id": "D3-FACTORY-MESH", "repo": "Factory", "sec": 9, "expected": "PARTIAL", "phase": "factory",
     "must_prove": "data contract trong app/data_contracts/ chạy",
     "known_missing": "thiếu L2; chưa có consumer ngoài repo"},
    {"id": "D3-APEX-MESH", "repo": "ApexInspect", "sec": 9, "expected": "PARTIAL", "phase": "apexinspect",
     "must_prove": "MES sync hoạt động", "known_missing": "thiếu L2; chưa có data contract publish"},
    {"id": "D3-MAIA-MESH", "repo": "MAIA", "sec": 9, "expected": "PARTIAL", "phase": "maia",
     "must_prove": "1 data plane marketing chạy", "known_missing": "chưa mở rộng domain; thiếu L2"},
    {"id": "D3-MINIERP-MESH", "repo": "MiniERP", "sec": 9, "expected": "PARTIAL", "phase": "later",
     "must_prove": "3 package ERP_* tồn tại", "known_missing": "thiếu L2; chưa consume data product"},
    {"id": "D3-HELPDESK-MESH", "repo": "Helpdesk", "sec": 9, "expected": "PARTIAL", "phase": "later",
     "must_prove": "docs/11-minierp-integration.md + tickets/",
     "known_missing": "thiếu L2; IT asset chưa thành data product"},

    # ---- 7.4 — Feature Store / ML Training / Lambda-Kappa (4) ----
    {"id": "D3-MAIA-LAMBDA", "repo": "MAIA", "sec": 3, "expected": "PARTIAL", "phase": "maia",
     "must_prove": "batch doc ingestion path chạy",
     "known_missing": "Lambda speed layer không tồn tại — về logic phải là ❌"},
    # Phát hiện trong rerun CLEAN_HEAD: capability MCP thật khác với dirty snapshot.
    {"id": "D3-MAIA-MCP-CAPABILITY", "repo": "MAIA", "sec": 3, "expected": "PARTIAL", "phase": "maia",
     "must_prove": "probe initialize/tools/resources/prompts/server-discover trên clean HEAD",
     "known_missing": "CLEAN HEAD chỉ có tools capability; resources+prompts chỉ tồn tại ở dirty tree"},
    {"id": "D3-MAIA-KAPPA", "repo": "MAIA", "sec": 4, "expected": "PARTIAL", "phase": "maia",
     "must_prove": "Kafka outbox tái xử lý từ log",
     "known_missing": "chưa chứng minh replay từ offset đầu"},
    {"id": "D3-CREDITFLOW-FEATURESTORE", "repo": "CreditFlow", "sec": 10, "expected": "PARTIAL", "phase": "later",
     "must_prove": "pipeline/feature_engineering/features.py chạy",
     "known_missing": "chưa tách offline/online store; chưa có point-in-time correctness"},
    {"id": "D3-APEX-MLTRAINING", "repo": "ApexInspect", "sec": 11, "expected": "PARTIAL", "phase": "apexinspect",
     "must_prove": "trigger engine + MES sync chạy",
     "known_missing": "không có ML training — chỉ inference"},

    # ---- 7.5 — Real-time Inference Core (3) ----
    {"id": "D3-APEX-REALTIME", "repo": "ApexInspect", "sec": 12, "expected": "PARTIAL", "phase": "apexinspect",
     "must_prove": "ONNX inference chạy, sinh latency.json + throughput.json",
     "known_missing": "thiếu P50/P95/P99, queue_depth, deadline_miss_rate, overload policy"},
    # Phát hiện trong PHASE 3: ApexInspect ĐÃ có tamper-evident hash chain.
    # Claim này không có trong regrade-01 nhưng là hệ quả trực tiếp của việc verify,
    # và là thứ roadmap cũ (E.4 bước 3) đề xuất làm. Ghi lại để không mất dấu vết.
    {"id": "D3-APEX-AUDIT", "repo": "ApexInspect", "sec": 9, "expected": "VERIFIED", "phase": "apexinspect",
     "must_prove": "audit hash chain phát hiện được modified/deleted/reordered record"},
    {"id": "D3-CREDITFLOW-REALTIME", "repo": "CreditFlow", "sec": 12, "expected": "PARTIAL", "phase": "later",
     "must_prove": "FastAPI serving trả kết quả",
     "known_missing": "thiếu latency SLO, overload policy, monitoring"},
    {"id": "D3-MINIERP-REALTIME", "repo": "MiniERP", "sec": 12, "expected": "PARTIAL", "phase": "later",
     "must_prove": "outbox fail-soft không rollback nghiệp vụ",
     "known_missing": "thiếu latency SLO + overload policy"},

    # ---- 7.6 — nghi phân loại sai, cần xác minh (3) ----
    {"id": "D3-FLASHSALE-MLTRAINING", "repo": "FlashSale", "sec": 11, "expected": "PARTIAL", "phase": "later",
     "must_prove": "xác nhận KHÔNG có model training", "known_missing": "sai phân loại — nên là ❌"},
    {"id": "D3-MINIERP-MLTRAINING", "repo": "MiniERP", "sec": 11, "expected": "PARTIAL", "phase": "later",
     "must_prove": "xác nhận KHÔNG có model registry", "known_missing": "sai phân loại — nên là ❌"},
    {"id": "D3-FLASHSALE-REALTIME-ALT", "repo": "FlashSale", "sec": 12, "expected": "PARTIAL", "phase": "later",
     "known_missing": "có thể trùng D3-FLASHSALE-REALTIME — cần gộp"},
]

REPO_PATHS = {
    "Factory": "Factory-Data-Automation-v-AI-Reporting-Platform",
    "MAIA": "MAIA",
    "ApexInspect": "ApexInspect-AI",
    "CreditFlow": "CreditFlow",
    "FlashSale": "FlashSale-Backend",
    "MiniERP": "MiniERP-Manufacturing-Warehouse",
    "Helpdesk": "Enterprise-IT-Helpdesk-Lab",
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def repo_head(repo: str) -> str | None:
    d = ROOT / REPO_PATHS.get(repo, "")
    if not d.is_dir():
        return None
    try:
        out = subprocess.run(["git", "-C", str(d), "rev-parse", "HEAD"],
                             capture_output=True, text=True, timeout=10, check=True)
        return out.stdout.strip()
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, FileNotFoundError):
        return None


def load_manifest() -> dict:
    m = EVIDENCE / "manifest.json"
    if not m.is_file():
        return {}
    try:
        return json.loads(m.read_text())
    except json.JSONDecodeError:
        return {}


@dataclass
class Result:
    claim: dict
    status: Status
    reason: str


def evaluate(claim: dict, manifest: dict) -> Result:
    cid = claim["id"]

    # 1. claim có evidence metadata không
    if cid not in manifest:
        return Result(claim, Status.NOT_RUN, "chưa có evidence metadata")

    ev = manifest[cid]

    # 2. evidence_version được hỗ trợ không
    ver = str(ev.get("evidence_version", ""))
    if ver not in EVIDENCE_VERSION_SUPPORTED:
        return Result(claim, Status.NOT_RUN, f"evidence_version không hỗ trợ: {ver!r}")

    # 3. đủ trường bắt buộc không
    required = ["claim_id", "repo", "commit_sha", "command", "executed_at",
                "exit_code", "duration_s", "artifact", "sha256", "status", "env", "notes"]
    missing = [f for f in required if f not in ev]
    if missing:
        return Result(claim, Status.NOT_RUN, f"thiếu trường bắt buộc: {missing}")

    # 4. artifact tồn tại không
    art = EVIDENCE / ev["repo"].lower() / ev["artifact"]
    if not art.is_file():
        return Result(claim, Status.FAILED, f"artifact không tồn tại: {ev['artifact']}")

    # 5. SHA-256 khớp không
    actual = sha256(art)
    if actual != ev["sha256"]:
        return Result(claim, Status.FAILED,
                      f"SHA-256 mismatch: manifest {ev['sha256'][:16]}… ≠ actual {actual[:16]}…")

    # 6. commit khớp repo HEAD không — STALE tuyệt đối không hiện VERIFIED
    head = repo_head(ev["repo"])
    if head is None:
        return Result(claim, Status.NOT_RUN, f"không đọc được git HEAD của {ev['repo']}")
    if ev["commit_sha"] != head:
        return Result(claim, Status.STALE,
                      f"commit lệch: evidence {ev['commit_sha'][:8]} ≠ HEAD {head[:8]}")

    # 7. runtime status
    #
    # QUAN TRỌNG: exit_code KHÔNG quyết định trạng thái claim.
    # Một claim có thể dùng artifact từ full suite (exit=1 vì có test khác fail)
    # mà vẫn chứng minh được hành vi claim mô tả. Ngược lại exit=0 không bảo đảm
    # claim đạt — ví dụ Lambda có thể "chạy" mà vẫn là claim không hợp lệ.
    #
    # Nguồn quyết định là `status` ghi trong manifest, đặt sau khi đọc kết quả.
    # `exit_code` chỉ giữ để truy vết.
    try:
        recorded = Status(ev["status"])
    except ValueError:
        return Result(claim, Status.NOT_RUN, f"status không hợp lệ: {ev['status']!r}")

    if recorded is not Status.VERIFIED:
        suffix = f" [exit={ev['exit_code']}]" if ev["exit_code"] != 0 else ""
        return Result(claim, recorded, f"{ev['notes']}{suffix}")

    # 8. claim còn thành phần thiếu đã biết → giữ PARTIAL
    if claim.get("known_missing"):
        return Result(claim, Status.PARTIAL, claim["known_missing"])

    return Result(claim, Status.VERIFIED,
                  "evidence hợp lệ, commit khớp, không thiếu thành phần")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", action="store_true", dest="as_json")
    ap.add_argument("--claim", help="chỉ kiểm 1 claim id")
    ap.add_argument("--phase", help="lọc theo phase: factory|maia|apexinspect|later")
    args = ap.parse_args()

    manifest = load_manifest()
    claims = CLAIMS
    if args.claim:
        claims = [c for c in claims if c["id"] == args.claim]
        if not claims:
            print(f"BLOCKED: không có claim id '{args.claim}'", file=sys.stderr)
            return 2
    if args.phase:
        claims = [c for c in claims if c["phase"] == args.phase]

    results = [evaluate(c, manifest) for c in claims]
    counts = {s: 0 for s in Status}
    for r in results:
        counts[r.status] += 1

    if args.as_json:
        print(json.dumps({
            "total": len(results),
            "counts": {k.value: v for k, v in counts.items()},
            "results": [
                {"claim_id": r.claim["id"], "repo": r.claim["repo"], "sec": r.claim["sec"],
                 "expected": r.claim["expected"], "status": r.status.value, "reason": r.reason}
                for r in results
            ],
        }, indent=2, ensure_ascii=False))
        return 0

    w = max((len(r.claim["id"]) for r in results), default=20)
    print(f"{'CLAIM ID'.ljust(w)}  {'REPO'.ljust(11)} §  {'EXPECTED'.ljust(8)} STATUS")
    print("-" * (w + 44))
    for r in results:
        print(f"{r.claim['id'].ljust(w)}  {r.claim['repo'].ljust(11)} "
              f"{r.claim['sec']:<2}  {r.claim['expected'].ljust(8)} {r.status.value}")
        if r.status is not Status.VERIFIED and r.reason:
            print(f"{' ' * w}   └─ {r.reason}")
    print("-" * (w + 44))
    print(f"{'TOTAL'.ljust(w)}  {'':11}    {'':8} {len(results)}")
    print("  " + "   ".join(f"{s.value}={counts[s]}" for s in Status))
    return 0


if __name__ == "__main__":
    sys.exit(main())


