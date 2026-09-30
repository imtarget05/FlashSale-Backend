#!/usr/bin/env python3
"""GATE 8 aggregate — close Gate 8 across CreditFlow (8A) and MAIA (8B).

Gate 8 is only PASS if BOTH sub-gates are verified AND the control
mechanism itself was shown to fail on a bad candidate. A green metric with
no negative test is not a gate.

This does NOT re-run 8A or 8B. It reads their evidence and re-derives the
aggregate, so the aggregate cannot disagree with the sub-gates. Re-running
them here would create two sources of truth for the same claim.
"""
from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path

PROJ = Path(__file__).resolve().parent.parent
EV = PROJ / "docs/evidence/e2e"
CF = EV / "gate8a-creditflow-model-quality.json"
MA = EV / "gate8b-maia-rag-quality.json"
OUT = PROJ / "docs/gate8-ai-ml-eval.json"

checks: list[dict] = []


def load(p: Path) -> dict:
    if not p.exists():
        print(f"  MISSING evidence: {p}")
        return {}
    return json.loads(p.read_text())


def add(name: str, ok: bool, detail: str, blocking: bool = True):
    checks.append({"requirement": name, "met": bool(ok), "detail": detail,
                   "blocking": blocking})
    print(f"  [{'OK ' if ok else 'GAP'}] {name}: {detail}")


cf, ma = load(CF), load(MA)
print("\n[G8] reading sub-gate evidence")
print(f"  8A CreditFlow: {CF.name} -> {cf.get('status', 'MISSING')}")
print(f"  8B MAIA:       {MA.name} -> {ma.get('status', 'MISSING')}")

print("\n[G8A] CreditFlow sub-gate")
c = {x["id"]: x for x in cf.get("checks", [])}
neg_cf = cf.get("negative_tests", {})
add("CreditFlow dataset integrity verified",
    c.get("A1", {}).get("status") == "PASS",
    f"dataset sha256={cf.get('integrity', {}).get('dataset_sha256', '?')[:16]}")
add("CreditFlow approved candidate PASS",
    c.get("A3", {}).get("status") == "PASS",
    f"norm_cost={cf.get('baseline', {}).get('normalized_business_cost')}, "
    f"recall={cf.get('baseline', {}).get('recall_positive')}")
add("CreditFlow negative candidate REJECTED",
    neg_cf.get("creditflow_rejected") is True,
    f"threshold 0.50 -> norm_cost="
    f"{cf.get('negative_candidate', {}).get('normalized_business_cost')}")
add("CI exits non-zero on CreditFlow quality failure",
    neg_cf.get("ci_exit_nonzero_on_fail") is True
    and neg_cf.get("full_harness_exit_on_bad_candidate") == 1,
    f"gate-decision exit!=0; full-harness exit="
    f"{neg_cf.get('full_harness_exit_on_bad_candidate')}")
lg = cf.get("leakage_guard", {})
add("CreditFlow train/eval leakage guard passed",
    lg.get("train_eval_overlap") == 0,
    f"overlap={lg.get('train_eval_overlap')}, train={lg.get('train_count')}, "
    f"eval={lg.get('eval_count')}")
print("\n[G8B] MAIA sub-gate")
m = {x["id"]: x for x in ma.get("checks", [])}
neg_ma = ma.get("negative_tests", {})
add("MAIA dataset integrity verified",
    m.get("B1", {}).get("status") == "PASS",
    f"golden sha256={ma.get('integrity', {}).get('golden_set_sha256', '?')[:16]}, "
    f"corpus sha256={ma.get('integrity', {}).get('corpus_sha256', '?')[:16]}")
add("MAIA approved candidate PASS (retrieval scope)",
    m.get("B4", {}).get("status") == "PASS",
    f"recall@3={ma.get('approved', {}).get('recall_at_k')}, "
    f"mrr={ma.get('approved', {}).get('mrr')} "
    f"(ceiling={ma.get('oracle_ceiling_recall')})")
add("MAIA negative candidate REJECTED",
    neg_ma.get("maia_rejected") is True,
    f"dense-only -> recall@3="
    f"{ma.get('negative_candidate', {}).get('recall_at_k')}")
add("CI exits non-zero on MAIA retrieval failure",
    neg_ma.get("ci_exit_nonzero_on_fail") is True,
    f"gate-decision exit!=0; full-harness exit on bad candidate="
    f"{neg_ma.get('full_harness_exit_on_bad_candidate')}")
add("MAIA deterministic metrics repeatable",
    (ma.get("repeatability", {}).get("identical")) is True,
    "second retrieval pass produced identical recall/mrr/citation set")
add("MAIA citation validity: no fabricated chunk ids",
    ma.get("citation_validity", {}).get("validity") == 1.0
    and ma.get("citation_validity", {}).get("fabricated_id_rejected") is True,
    f"{ma.get('citation_validity', {}).get('distinct_citations')} citations, "
    f"{ma.get('citation_validity', {}).get('unresolvable')} unresolvable")

# NOT_RUN is tracked as its own requirement — NOT dropped, NOT promoted to a
# pass. B8 is a real gap: the shipped golden set has no deterministic oracle
# for correct refusal, so anti-hallucination is NOT_MEASURED.
a = ma.get("abstention", {})
# B8 IS blocking for the aggregate. Abstention is the core anti-hallucination
# property of a RAG system; a gate that cannot measure it has not verified
# the thing it exists to verify. It is marked non-blocking at the 8B level
# only because 8B's own declared scope is retrieval — but the portfolio-level
# claim "AI/ML evaluation" covers abstention, so it cannot be waived here.
add("MAIA abstention measured (anti-hallucination oracle)", False,
    f"status={a.get('status')}: "
    f"{a.get('unanswerable_with_usable_oracle')} of {a.get('unanswerable_rows')} "
    f"unanswerable rows carry gold_keywords; refusal accuracy is "
    f"NOT_MEASURED because it needs the LLM generation stage. 0/0 is not a "
    f"pass.", blocking=True)

blocking_failures = [x for x in checks if not x["met"] and x["blocking"]]
open_gaps = [x["requirement"] for x in checks if not x["met"]]
# Overall follows BOTH the requirement matrix and the sub-gates' own status.
# A PARTIAL sub-gate can never yield a PASS aggregate.
if blocking_failures or "PARTIAL" in (cf.get("status"), ma.get("status")):
    overall = "PARTIAL"
else:
    overall = "PASS"

gate8 = {
    "gate": 8,
    "title": "AI/ML Evaluation Gate (CreditFlow model quality + MAIA RAG)",
    "status": overall,
    "generated_at": datetime.now(timezone.utc).isoformat(),
    "sub_gates": {
        "8A_creditflow_model_quality": {
            "status": cf.get("status"), "commit": cf.get("commit"),
            "repo": cf.get("commit_repo"), "evidence": str(CF.relative_to(PROJ)),
            "dataset_sha256": cf.get("integrity", {}).get("dataset_sha256"),
            "model_sha256": cf.get("integrity", {}).get("model_sha256"),
        },
        "8B_maia_rag_quality": {
            "status": ma.get("status"), "commit": ma.get("commit"),
            "repo": ma.get("commit_repo"), "evidence": str(MA.relative_to(PROJ)),
            "dataset_sha256": ma.get("integrity", {}).get("golden_set_sha256"),
            "corpus_sha256": ma.get("integrity", {}).get("corpus_sha256"),
            "blocking_scope": ma.get("blocking_scope"),
        },
    },
    "acceptance_matrix": checks,
    "open_gaps": open_gaps,
    "portfolio_acceptance_suite": ("8/8 VERIFIED" if overall == "PASS"
                                   else "7/8 VERIFIED + GATE 8 PARTIAL"),
    "interpretation": (
        "Both sub-gates demonstrate the control mechanism works: a known-bad "
        "candidate is rejected and CI exits non-zero, while the approved "
        "candidate passes. Gate 8 is PARTIAL rather than PASS because MAIA "
        "abstention has no deterministic oracle in the shipped golden set, so "
        "anti-hallucination behaviour is NOT_MEASURED, not verified."
    ),
}
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(gate8, indent=2))
met = sum(1 for x in checks if x["met"])
print(f"\n{met}/{len(checks)} requirements met — GATE 8 {overall}")
print(f"blocking failures: {blocking_failures or 'none'}")
print(f"open gaps: {open_gaps or 'none'}")
print(f"evidence: {OUT}")
sys.exit(0 if overall == "PASS" else 1)
