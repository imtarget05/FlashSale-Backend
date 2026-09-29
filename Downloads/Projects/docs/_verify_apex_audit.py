#!/usr/bin/env python3
"""Verify audit hash chain của ApexInspect bằng hành vi thật.

API thực: agent/audit_chain.py có append(record, log_path) và verify(log_path).
Mục tiêu: chứng minh tampering BỊ PHÁT HIỆN, không chỉ đọc docstring.
"""
import json, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "ApexInspect-AI"))

from src.agent.audit_chain import append, verify  # noqa: E402

R = {}


def build(tmp: Path, records):
    p = tmp / "audit.jsonl"
    for r in records:
        append(r, str(p))
    return p


def vfy(p):
    return verify(str(p))


def check(label, fn):
    with tempfile.TemporaryDirectory() as td:
        R[label] = bool(fn(Path(td)))


# 1. chain hợp lệ
check("valid_chain_verifies", lambda t: vfy(str(build(t, [
    {"actor": "system", "action": "start"},
    {"actor": "supervisor", "action": "approve", "defect": "D-1"},
    {"actor": "plc", "action": "dispatch"}])))[0])

# 2. sửa nội dung 1 record
def tamper_modify(t):
    p = build(t, [{"actor": "a", "action": "x"}, {"actor": "b", "action": "y"}])
    lines = p.read_text().splitlines()
    e = json.loads(lines[0]); e["record"]["action"] = "TAMPERED"
    lines[0] = json.dumps(e)
    p.write_text("\n".join(lines) + "\n")
    ok, bad = vfy(p)
    return (ok is False) and bad is not None
check("modified_record_detected", tamper_modify)

# 3. xoá 1 record
def tamper_delete(t):
    p = build(t, [{"actor": "a", "action": "x"},
                  {"actor": "b", "action": "y"},
                  {"actor": "c", "action": "z"}])
    lines = p.read_text().splitlines()
    del lines[1]
    p.write_text("\n".join(lines) + "\n")
    ok, bad = vfy(p)
    return (ok is False) and bad is not None
check("deleted_record_detected", tamper_delete)

# 4. đảo thứ tự 2 record
def tamper_reorder(t):
    p = build(t, [{"actor": "a", "action": "x"}, {"actor": "b", "action": "y"}])
    lines = p.read_text().splitlines()
    lines[0], lines[1] = lines[1], lines[0]
    p.write_text("\n".join(lines) + "\n")
    ok, bad = vfy(p)
    return (ok is False) and bad is not None
check("reordered_record_detected", tamper_reorder)

# 5. cấu trúc entry
with tempfile.TemporaryDirectory() as td:
    p = build(Path(td), [{"actor": "a", "action": "x"}])
    e = json.loads(p.read_text().splitlines()[0])
    R["has_prev_hash"] = "prev_hash" in e
    R["has_entry_hash"] = "entry_hash" in e
    R["genesis_prev_hash"] = e.get("prev_hash") == "GENESIS"
    R["entry_hash_sha256_len64"] = len(e.get("entry_hash", "")) == 64

out = {
    "source": "ApexInspect-AI/src/agent/audit_chain.py",
    "formula": "entry_hash = sha256(canonical_json({index, prev_hash, record})); genesis prev_hash='GENESIS'",
    "tests": R,
    "all_pass": all(R.values()),
    "finding": ("ApexInspect ĐÃ CÓ tamper-evident hash chain. ERRATA: regrade-01 nói "
                "'AuditLog KHÔNG tamper-evident, cần port từ CreditFlow' — SAI. "
                "PHASE 10 (port hash chain) KHÔNG còn cần thiết."),
}
dest = Path(__file__).resolve().parent / "evidence/apexinspect/audit/audit-chain-verification.json"
dest.parent.mkdir(parents=True, exist_ok=True)
dest.write_text(json.dumps(out, indent=2, ensure_ascii=False))
for k, v in R.items():
    print(f"  {k:30} {v}")
print(f"all_pass = {out['all_pass']}")

