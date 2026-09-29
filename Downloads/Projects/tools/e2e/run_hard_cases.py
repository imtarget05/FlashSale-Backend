#!/usr/bin/env python3
"""Chạy TỪNG hard case của §26 bằng test thật trong repo, rồi chấm gate.

Về vì sao file này tồn tại
--------------------------
Checklist hard case viết ra rất dễ tạo cảm giác đã làm xong: mỗi dòng đều
nghe rất hợp lý. Nhưng một dòng checklist không phải bằng chứng. Hầu hết các
hard case đó ĐÃ CÓ test trong repo từ trước — chúng chỉ chưa từng được chạy
và ghi lại. Vì vậy ở đây mỗi case ánh xạ tới một test cụ thể, test đó được
chạy thật, và điểm chỉ được cấp khi test đó PASS với ranh giới REAL.

Điểm không được cấp khi:
  - test tồn tại nhưng không chạy (thiếu dependency)
  - test pass nhưng ở ranh giới MOCKED
  - chỉ có tên test mà không có kết quả runtime

Bảng ánh xạ ở HARD_CASES là phần cần sửa tay. Phần còn lại là máy làm, và
nếu một test tên đó biến mất thì gate tụt xuống 1 chứ không âm thầm giữ điểm.
"""
from __future__ import annotations

import json
import os
import shlex
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "tools" / "e2e"))
from evidence import E2E_ROOT, EvidenceRun  # noqa: E402

P11 = "/Users/mainguyenbinhtan/.local/bin/python3.11"

# case_id -> (project, repo_dir, kind, target, boundary, invariant)
#
# kind: "pytest" (dùng -k), "node" (dùng --test-name-pattern)
# boundary: REAL | SIMULATED | NOT_RUN
HARD_CASES = {
    # ---------------- CreditFlow ----------------
    "CF-01 future leakage": (
        "CreditFlow", "CreditFlow", "pytest",
        "tests/test_feature_store.py -k 'future or as_of or no_future'",
        "REAL",
        "an event after as_of never appears in materialised history"),
    "CF-02 train/serve parity": (
        "CreditFlow", "CreditFlow", "pytest",
        "tests/test_features.py", "REAL",
        "offline and online feature computation agree"),
    "CF-03 threshold by business cost": (
        "CreditFlow", "CreditFlow", "pytest",
        "tests/ -k 'threshold or cost'", "REAL",
        "decision threshold follows business cost, not a fixed 0.5"),
    "CF-06 audit tamper": (
        "CreditFlow", "CreditFlow", "pytest",
        "tests/ -k 'audit or tamper or chain'", "REAL",
        "a modified audit record is detected"),

    # ---------------- MAIA ----------------
    "MAIA-04 cross-tenant": (
        "MAIA", "MAIA-clean", "pytest",
        "tests/test_tenant_authorization.py", "REAL",
        "a tenant-A actor cannot act for tenant B, and writes nothing"),
    # `test_guarded_generate_fallback_on_no_evidence` và
    # `test_refusal_path_logs_refusal_reason` là hai mặt của MAIA-01: một cái
    # kiểm hành vi (trả lời không có bằng chứng thì từ chối), cái còn lại kiểm
    # tín hiện vận hành (lý do từ chối có được ghi log không).
    "MAIA-01 no-evidence refusal": (
        "MAIA", "MAIA-clean", "pytest",
        "tests/test_loops.py -k 'no_evidence or invalid_cite' "
        "--ignore=tests/test_llama_index_dataplane.py",
        "REAL", "no supporting evidence yields refusal, not an answer"),
    "MAIA-08 qdrant unavailable": (
        "MAIA", "MAIA-clean", "pytest",
        "tests/test_tracing.py -k 'refusal or fallback or offline' "
        "--ignore=tests/test_llama_index_dataplane.py",
        "REAL", "missing Qdrant degrades explicitly instead of fabricating"),
    "MAIA-05 agent restart": (
        "MAIA", "MAIA-clean", "pytest",
        "tests/test_langgraph_agent.py -k 'resume or checkpoint or "
        "insufficient' --ignore=tests/test_llama_index_dataplane.py",
        "REAL", "agent state survives an interrupt and resumes"),

    # ---------------- ApexInspect ----------------
    "APEX-03 PLC unavailable": (
        "ApexInspect", "ApexInspect-AI", "pytest",
        "tests/test_plc_bridge.py -k 'unavailable or hardware'",
        "SIMULATED", "an unreachable PLC is refused, not faked"),
    "APEX-04 audit tamper": (
        "ApexInspect", "ApexInspect-AI", "pytest",
        "tests/test_audit_chain.py", "REAL",
        "one-byte tamper and truncation are both detected"),

    # ---------------- Factory ----------------
    "FAC-01 quarantine": (
        "Factory", "Factory-Data-Automation-v-AI-Reporting-Platform", "pytest",
        "tests/test_quarantine.py", "REAL",
        "invalid rows are quarantined and counted, never silently dropped"),
    "FAC-03 contracts": (
        "Factory", "Factory-Data-Automation-v-AI-Reporting-Platform", "pytest",
        "tests/test_contracts.py", "REAL",
        "a schema break fails loudly instead of corrupting"),

    # ---------------- Helpdesk ----------------
    "HD-04 RBAC": (
        "Helpdesk", "Enterprise-IT-Helpdesk-Lab/internal-portal", "node",
        "test/auth-boundary.test.js", "REAL",
        "an unauthorised role cannot mutate protected state"),
    "HD-03 alert dedupe": (
        "Helpdesk", "Enterprise-IT-Helpdesk-Lab/internal-portal", "node",
        "test/ticket-alert.test.js", "REAL",
        "a repeated alert updates one incident instead of creating many"),
}


def run_pytest(repo: Path, env_key: str, target: str, qdrant: bool):
    """Chạy một target pytest.

    `target` là chuỗi kiểu "test_x.py -k 'foo or bar'". Nó PHẢI được tách
    bằng shlex trước khi truyền vào subprocess: truyền nguyên chuỗi sẽ khiến
    pytest coi toàn bộ "test_x.py -k 'foo'" là một đường dẫn file và báo
    usage error với rc=4 — trông giống test fail nhưng thực ra chưa chạy gì.
    Đó là loại lỗi âm thầm nguy hiểm nhất: mọi case đều đỏ và không case nào
    cho biết nguyên nhân.
    """
    py = ROOT / f".e2e-envs/{env_key}/bin/python"
    if not py.exists():
        return 127, "", f"no venv for {env_key}"
    env = dict(os.environ)          # giữ PATH/HOME/TMPDIR của hệ thống
    if qdrant:
        env["QDRANT_URL"] = "http://localhost:6333"
    args = [str(py), "-m", "pytest", *shlex.split(target), "-q", "--no-header",
            "-p", "no:cacheprovider"]
    try:
        p = subprocess.run(args, cwd=repo, capture_output=True, text=True,
                           timeout=600, env=env)
        return p.returncode, (p.stdout + p.stderr)[-2500:], ""
    except subprocess.TimeoutExpired:
        return 124, "", "timed out"


def run_node(repo: Path, target: str):
    if not (repo / "node_modules").exists():
        return 127, "", "node_modules absent"
    env = dict(os.environ)
    try:
        p = subprocess.run(["node", "--test", *shlex.split(target)], cwd=repo,
                           capture_output=True, text=True, timeout=600, env=env)
        return p.returncode, (p.stdout + p.stderr)[-2500:], ""
    except subprocess.TimeoutExpired:
        return 124, "", "timed out"



ENV_KEY = {"CreditFlow": "creditflow", "MAIA": "maia", "ApexInspect": "apex",
           "Factory": "factory"}


def main() -> int:
    results = {}
    by_project: dict[str, list] = {}

    for case_id, (project, repo_dir, kind, target, boundary,
                  invariant) in HARD_CASES.items():
        repo = ROOT / repo_dir
        print(f"  {case_id} ... ", end="", flush=True)
        if kind == "pytest":
            code, out, err = run_pytest(repo, ENV_KEY[project], target,
                                        qdrant=(project == "MAIA"))
        else:
            code, out, err = run_node(repo, target)
        passed = code == 0
        results[case_id] = {
            "project": project, "command": f"{kind}: {target}",
            "returncode": code, "passed": passed, "boundary": boundary,
            "invariant": invariant, "error": err,
            "output_tail": out[-600:] if out else "",
        }
        by_project.setdefault(project, []).append(case_id)
        print("PASS" if passed else f"FAIL(rc={code}) {err}")

    out_dir = E2E_ROOT / "final"
    out_dir.mkdir(parents=True, exist_ok=True)

    with EvidenceRun("E2E-HARDCASES", [ROOT], "LOCAL",
                     "python tools/e2e/run_hard_cases.py",
                     boundaries={"note": "each case names the exact test it "
                                         "exercises; the gate score derives "
                                         "from these results, not from prose"},
                     notes=f"{sum(1 for r in results.values() if r['passed'])}"
                           f"/{len(results)} hard cases passed") as run:
        for case_id, r in results.items():
            run.check(
                case_id, r["passed"],
                boundary=r["boundary"] if r["passed"] else "REAL",
                expected="exit 0",
                actual=f"rc={r['returncode']} {r['error']}",
                detail=r["invariant"])
        target = out_dir / "hard-case-results.json"
        target.write_text(json.dumps(results, indent=2), encoding="utf-8")
        run.add_artifact(target)
        run.finalise(out_dir)

    print("\nper project:")
    for project, cases in sorted(by_project.items()):
        ok = sum(1 for c in cases if results[c]["passed"])
        print(f"  {project:12} {ok}/{len(cases)} hard cases")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
