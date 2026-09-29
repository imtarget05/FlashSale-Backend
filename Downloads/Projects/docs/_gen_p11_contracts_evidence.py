#!/usr/bin/env python
"""
Evidence cho Phase 11 — manufacturing contracts.

Chay: python docs/_gen_p11_contracts_evidence.py
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
from datetime import datetime, timezone

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTRACTS = os.path.join(REPO, "manufacturing-contracts")
EV = os.path.join(REPO, "docs", "evidence", "p11-contracts")


def write(rel: str, content: str) -> None:
    p = os.path.join(EV, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as fh:
        fh.write(content)


def main() -> int:
    stamp = datetime.now(timezone.utc).isoformat()
    os.chdir(CONTRACTS)

    with open("registry.json", encoding="utf-8") as fh:
        reg = json.load(fh)

    products = []
    for p in reg["data_products"]:
        with open(p["contract"], encoding="utf-8") as fh:
            schema = json.load(fh)
        products.append({
            "id": p["id"],
            "version": p["version"],
            "contract": p["contract"],
            "owner": p["owner"],
            "producer": p["producer"]["system"],
            "consumers": [c["system"] for c in p["consumers"]],
            "grain": p["grain"],
            "security_class": p["security_class"],
            "sla": p["sla"],
            "retention": p["retention"],
            "required_fields": schema.get("required", []),
            "top_level_props": sorted(schema.get("properties", {})),
        })

    write("contracts_summary.json", json.dumps({
        "generated_at": stamp,
        "registry_version": reg["registry_version"],
        "scope_is": reg["scope_boundaries"]["is"],
        "scope_is_not": reg["scope_boundaries"]["is_not"],
        "compatibility_policy": reg["compatibility_policy"],
        "delivery_semantics": reg["delivery_semantics"],
        "data_products": products,
    }, indent=2, ensure_ascii=False))

    pr = subprocess.run([sys.executable, "-m", "pytest", "tests/", "-v",
                         "--no-header", "-p", "no:cacheprovider"],
                        capture_output=True, text=True, timeout=300)
    out = pr.stdout + pr.stderr
    write("tests/pytest.txt", out)
    code = pr.returncode
    tail = [l for l in out.splitlines() if " passed" in l or " failed" in l]
    summary = tail[-1] if tail else "unknown"

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
    print("products:", len(products), "| tests exit:", code, "|", summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
