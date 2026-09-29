#!/usr/bin/env python3
"""Xuất final verification matrix (JSON + Markdown) từ verify_matrix + manifest."""
import json, subprocess, sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "docs"))
import verify_matrix as vm  # noqa: E402

man = vm.load_manifest()
results = [vm.evaluate(c, man) for c in vm.CLAIMS]
counts = Counter(r.status.value for r in results)
frozen = subprocess.run(["shasum", "-a", "256", str(ROOT / "DESIGN-BRIEF-v2-frozen.md")],
                        capture_output=True, text=True, check=True).stdout.split()[0]

final = {
    "generated_at": datetime.now(timezone.utc).isoformat(),
    "frozen_baseline_sha256": frozen,
    "frozen_baseline_intact": frozen.startswith("f2a7c054"),
    "total_claims": len(results),
    "counts": dict(counts),
    "claims": [{
        "claim_id": r.claim["id"], "repo": r.claim["repo"], "sec": r.claim["sec"],
        "design_status": r.claim["expected"], "runtime_status": r.status.value,
        "reason": r.reason,
        "command": man.get(r.claim["id"], {}).get("command"),
        "artifact": man.get(r.claim["id"], {}).get("artifact"),
        "sha256": man.get(r.claim["id"], {}).get("sha256"),
        "commit_sha": man.get(r.claim["id"], {}).get("commit_sha"),
    } for r in results],
}
(ROOT / "docs/evidence/final-matrix.json").write_text(
    json.dumps(final, indent=2, ensure_ascii=False))

L = ["# Final Verification Matrix", "",
     f"Generated: `{final['generated_at']}`", "",
     f"Frozen baseline SHA-256: `{frozen}`", "",
     f"**Baseline intact:** {'YES' if final['frozen_baseline_intact'] else 'VIOLATION'}", "",
     f"**Total claims:** {len(results)}  ",
     "**Counts:** " + " · ".join(f"{k}={v}" for k, v in sorted(counts.items())), "",
     "| Claim | Repo | § | Design | Runtime | Artifact |",
     "|---|---|---|---|---|---|"]
for r in results:
    cid = r.claim["id"]
    art = man.get(cid, {}).get("artifact", "—")
    L.append(f"| `{cid}` | {r.claim['repo']} | {r.claim['sec']} | "
             f"{r.claim['expected']} | **{r.status.value}** | `{art}` |")
(ROOT / "docs/evidence/final-matrix.md").write_text("\n".join(L))
print(json.dumps(dict(counts), indent=2))
print("frozen intact:", final["frozen_baseline_intact"])
