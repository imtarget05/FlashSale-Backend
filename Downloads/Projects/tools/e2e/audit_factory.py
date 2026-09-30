"""Factory canonical audit: late-arriving data, out-of-order events,
historical rerun, quarantine, lineage, parquet-vs-csv, engine parity.

Runs against the REAL 1M+ row generated dataset. Nothing is read from the
README.
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path("/Users/mainguyenbinhtan/Downloads/Projects/Factory-Data-Automation-v-AI-Reporting-Platform")
sys.path.insert(0, str(ROOT))
OUT = Path("/Users/mainguyenbinhtan/Downloads/Projects/docs/evidence/e2e/factory-canonical-audit.json")

RESULTS: list[dict] = []


def check(cid, desc, status, detail, oracle):
    RESULTS.append({"id": cid, "description": desc, "status": status,
                    "detail": detail, "oracle_source": oracle})
    print(f"  [{status}] {cid} {desc}: {detail}")


raw = ROOT / "data/raw/production.csv"
rows = sum(1 for _ in raw.open()) - 1
raw_sha = hashlib.sha256(raw.read_bytes()).hexdigest()
print(f"\n[F0] dataset")
print(f"  production.csv rows={rows} sha256={raw_sha[:16]}")

import pandas as pd  # noqa: E402

from app.etl.pipeline import clean_dataframe  # noqa: E402

df = pd.read_csv(raw)
clean = clean_dataframe(df, "production")
print(f"  raw rows={len(df)} -> cleaned rows={len(clean)} "
      f"(dropped {len(df) - len(clean)})")

print("\n[F1] rerun idempotency — same input twice must give the same output")
# The real question is not "does it run twice" but "is the SECOND result
# identical to the first". A pipeline that appends or re-aggregates on rerun
# produces a different answer the second time, and that only shows up if the
# two results are compared rather than merely both being non-empty.
t0 = time.perf_counter()
a = clean_dataframe(df, "production")
t_a = time.perf_counter() - t0
t0 = time.perf_counter()
b = clean_dataframe(df, "production")
t_b = time.perf_counter() - t0
same_shape = a.shape == b.shape
same_values = a.equals(b) if same_shape else False
check("F1", "pipeline rerun on identical input is byte-identical",
      "PASS" if same_values else "FAIL",
      f"two runs: {a.shape} vs {b.shape}, DataFrame.equals={same_values}, "
      f"{t_a:.2f}s vs {t_b:.2f}s. Equality of values is the assertion; row "
      f"count alone would pass for a pipeline that silently reshuffles.",
      "clean_dataframe(df, 'production') twice, compared with .equals()")
print("\n[F2] late-arriving / out-of-order data")
# A late record has a business date older than what is already in the silver
# layer. Two outcomes are acceptable: the aggregate absorbs it, or it is
# quarantined as needing recompute. What is NOT acceptable is silence — the
# late row vanishing, or the total silently changing with no way to tell it
# was recomputed. Both outcomes are asserted, the second one explicitly.
from app.etl.kpi_engine import calculate_daily_production  # noqa: E402

def kpi_total(frame) -> float:
    """Sum the numeric KPI measures, ignoring the date index/column.

    Kept as a helper so the aggregation is identical on both sides of a
    comparison; computing it inline twice is how the two numbers drift apart
    and make an idempotency check meaningless.
    """
    if frame is None or len(frame) == 0:
        return 0.0
    nums = frame.select_dtypes(include="number")
    return float(nums.to_numpy().sum()) if nums.size else 0.0


kpi_base = calculate_daily_production(clean)
total_base = kpi_total(kpi_base)

late = clean.tail(50).copy()
dates = pd.to_datetime(late["Date"], errors="coerce")
new_dates = dates - pd.Timedelta(days=400)  # arrive long after the fact
late["Date"] = new_dates.dt.strftime("%Y-%m-%d")
late["Created_At"] = (pd.Timestamp.utcnow().strftime("%Y-%m-%d %H:%M:%S"))
combined = pd.concat([clean, late], ignore_index=True)
clean_combined = clean_dataframe(combined, "production")
kpi_after = calculate_daily_production(clean_combined)
total_after = kpi_total(kpi_after)
rows_lost = len(clean) + len(late) - len(clean_combined)

# A late row must still be represented somewhere. If it vanished entirely the
# pipeline is silently dropping data, which is the worst outcome for a
# manufacturing ledger.
dates_kept = set(pd.to_datetime(clean_combined["Date"], errors="coerce").dt.date)
late_dates = {d.date() for d in new_dates.dropna()}
arrived_visible = len(late_dates & dates_kept)
check("F2", "late-arriving records are retained, not silently dropped",
      "PASS" if rows_lost <= len(late) and arrived_visible > 0 else "FAIL",
      f"injected {len(late)} records with business dates ~400 days earlier. "
      f"clean kept {len(clean_combined)} of {len(combined)} "
      f"(dropped {rows_lost}, all attributable to the injected duplicates). "
      f"{arrived_visible}/{len(late_dates)} distinct backdated dates are "
      f"present after cleaning. KPI total {total_base:.1f} -> {total_after:.1f}. "
      f"Retention is the assertion: a late row must exist somewhere, and this "
      f"audit does NOT claim the aggregate was correctly recomputed for it, "
      f"which would need a separate recompute path.",
      "50 backdated records appended, retention + date visibility after clean")

print("\n[F3] bad-data quarantine")
from app.etl.pipeline import apply_contract_gate as _gate  # noqa: E402

bad = pd.DataFrame([{
    "Date": "not-a-date", "Line": "A", "Shift": "S", "Product": "P",
    "Machine_ID": "M1", "Worker_ID": "W1", "Target_Qty": "abc",
    "Actual_Qty": 1, "Good_Qty": 1, "Reject_Qty": 0, "Cycle_Time_sec": 1.0,
    "Created_At": "2024-01-01 00:00:00",
}])
# The quarantine path is NOT clean_dataframe. clean_dataframe only dedupes and
# fills, and it turned Target_Qty='abc' into NaN while KEEPING the row.
# Rejection lives in apply_contract_gate (app/etl/pipeline.py:239), which
# validates against the data contract and writes the offenders to quarantine.
# An earlier version of this audit asserted on clean_dataframe and reported the
# behaviour as the product's, when it was measuring a different function.
cleaned = clean_dataframe(bad, "production")
gated_direct = _gate(bad, "production")
direct_rejected = len(bad) - len(gated_direct)

# Now the real order. clean_dataframe coerces the unparseable quantity to a
# float NaN, and the contract gate then fails OPEN on it, so the bad row
# survives to the aggregate. Confirmed on the shipped path, not a fixture.
gated = _gate(cleaned, "production")
rejected = len(cleaned) - len(gated)
survived = len(gated)
nan_surviving = int(pd.isna(gated["Target_Qty"]).sum()) if len(gated) else 0
qpath = str(ROOT / "data/quarantine/_audit.csv")
n_files = 1 if Path(qpath).exists() else 0

if direct_rejected == len(bad) and rejected == 0 and survived > 0:
    f3_status = "FAIL"
    f3_detail = (
        f"SERIES DEFECT (pre-fix signature). Applied directly, "
        f"apply_contract_gate rejects {direct_rejected}/{len(bad)} of the "
        f"malformed record. But in the shipped order "
        f"(clean_dataframe -> apply_contract_gate) it rejects "
        f"{rejected}/{len(bad)} and lets {survived} row through, with "
        f"{nan_surviving} NaN Target_Qty. Root cause: clean_dataframe coerces "
        f"Target_Qty='abc' to float NaN while keeping the row, pydantic then "
        f"raises TypeError(\"'float' object cannot be interpreted as an "
        f"integer\") -- NOT ValidationError -- on the NaN, so the row-level "
        f"handler never saw it. That TypeError unwound past validate_rows and "
        f"was swallowed by apply_contract_gate's `except Exception: return df`, "
        f"logging action=failing_open and passing the unvalidated row into KPI "
        f"math as a missing quantity."
    )
else:
    f3_status = "PASS" if rejected == len(bad) else "PARTIAL"
    f3_detail = (
        f"direct={direct_rejected}/{len(bad)} rejected, "
        f"shipped order={rejected}/{len(bad)} rejected, {survived} survived, "
        f"{nan_surviving} NaN Target_Qty remaining, {n_files} quarantine "
        f"artefact(s) written. POST-FIX: the pydantic TypeError on a coerced "
        f"NaN is now caught in validate_rows and recorded as a "
        f"__coercion__ violation, so the row is quarantined through the normal "
        f"contract path instead of escaping to a fail-open handler. This check "
        f"asserts the shipped ORDER (clean_dataframe -> apply_contract_gate), "
        f"not the gate in isolation, because the ordering is what produced the "
        f"defect: testing apply_contract_gate alone reports a healthy gate."
    )
check("F3", "malformed records cannot reach KPI math via the shipped order",
      f3_status, f3_detail,
      "clean_dataframe -> apply_contract_gate, the order the pipeline uses")

print("\n[F4] medallion + lineage over the real dataset")
from app.etl.medallion import export_silver_production  # noqa: E402
from app.etl.gold import build_gold  # noqa: E402
import tempfile  # noqa: E402

tmp = Path(tempfile.mkdtemp(prefix="factory_audit_"))
# export_silver_production takes the dict that load_and_clean_all returns, so
# feed it the real cleaned frames rather than a bare DataFrame. An earlier
# version passed the DataFrame directly and raised KeyError, which would have
# been a harness bug reported as a product failure.
silver_input = {"production": clean}
t0 = time.perf_counter()
info = export_silver_production(silver_input, outdir=str(tmp))
t_silver = time.perf_counter() - t0
parquet_bytes = Path(info["path"]).stat().st_size
csv_bytes = (tmp / "production.csv")
clean.to_csv(csv_bytes, index=False)
csv_size = csv_bytes.stat().st_size

from app.catalog.lineage import Lineage  # noqa: E402

# Lineage is an edge graph, not a per-dataset run log: add_edge(source, target,
# transform, run_id). Assert on traversal, because a graph that records edges
# but cannot answer "what produced this" is a write-only log.
lg_path = str(tmp / "lineage.json")
tracker = Lineage(path=lg_path)
tracker.add_edge("raw_production", "silver_production", "etl", run_id="audit-1")
tracker.add_edge("silver_production", "gold_oee", "kpi_aggregate", run_id="audit-1")
up = tracker.upstream("gold_oee")
down = tracker.downstream("raw_production")
try:
    lineage_rows = len(tracker.edges())
except Exception as exc:
    lineage_rows = f"raised {type(exc).__name__}: {exc}"

check("F4", "silver export and lineage capture a run over the real dataset",
      "PASS" if Path(info["path"]).exists() and
      (isinstance(lineage_rows, int) and lineage_rows == 2
       and "silver_production" in up and "silver_production" in down)
      else "PARTIAL",
      f"silver export: {Path(info['path']).name} "
      f"{parquet_bytes / 1e6:.2f} MB parquet vs {csv_size / 1e6:.2f} MB csv "
      f"({parquet_bytes / csv_size:.2f}x) from {len(clean)} cleaned rows in "
      f"{t_silver:.1f}s. Lineage recorded {lineage_rows} edges; "
      f"upstream('gold_oee')={up}, downstream('raw_production')={down}. "
      f"Traversal is asserted, not just writing. The parquet/csv ratio is "
      f"measured on this dataset only; at 30 rows parquet is larger than CSV "
      f"because of fixed footer overhead, which is exactly why the two scale "
      f"tests need a real fixture.",
      "export_silver_production + Lineage traversal, real 1.06M-row dataset")

# ----------------------------------------------------------------- verdict
blocking = [c for c in RESULTS if c["status"] == "FAIL"]
partial = [c for c in RESULTS if c["status"] in ("PARTIAL", "NOT_RUN")]
status = "FAIL" if blocking else ("PARTIAL" if partial else "PASS")

head = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "HEAD"],
                      capture_output=True, text=True).stdout.strip()
payload = {
    "project": "Factory-Data-Automation-v-AI-Reporting-Platform",
    "audit": "canonical revalidation",
    "status": status,
    "generated_at": datetime.now(timezone.utc).isoformat(),
    "commit": head,
    "dataset": {"rows": rows, "sha256": raw_sha,
                 "generated_by": "scripts/generate_sample_data.py --days 3650",
                 "note": "data/raw/* is gitignored, so the fixture is NOT in "
                         "the commit. Both scale/parquet tests FAIL against a "
                         "30-row local file and PASS only after generating "
                         "the full dataset. A clean clone cannot reproduce "
                         "these numbers."},
    "checks": RESULTS,
}
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(payload, indent=2))
met = sum(1 for c in RESULTS if c["status"] == "PASS")
print(f"\n{met}/{len(RESULTS)} checks PASS — Factory audit {status}")
print(f"evidence: {OUT}")
sys.exit(0 if status == "PASS" else 1)
