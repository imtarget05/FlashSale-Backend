#!/usr/bin/env python3
"""Map every CV claim to project -> runtime evidence -> metric -> commit.

The purpose is the opposite of a CV generator. A bullet that cannot be mapped
to a measured run will not survive an interview question, so this grades claims
rather than dressing them up, and the author decides what to do with each one.

Grades:
  KEEP      mapped to a measured number at a named commit
  WEAKEN    the capability is real but the phrasing is broader than the evidence;
            a replacement is supplied
  DELETE    no measured evidence supports the claim as written

Commits are joined from the portfolio matrix rather than typed here, so a claim
cannot cite a commit that has moved.
"""
from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

PROJ = Path(__file__).resolve().parent.parent
MATRIX = json.loads((PROJ / "docs/portfolio-matrix.json").read_text())
OUT = PROJ / "docs/cv-claim-map.json"

commit = {p["project"]: p["commit"] for p in MATRIX["projects"]}

CLAIMS: list[dict] = []
for fname in ("cv-claims-keeper.json", "cv-claims-weaken.json", "cv-claims-delete.json"):
    payload = json.loads((PROJ / "docs" / fname).read_text())
    CLAIMS.extend(payload["CLAIMS"])

for c in CLAIMS:
    # A claim that names its own commit keeps it. The post-remediation claims
    # cite the fix commit (6e0475c / 483a648 / 93f5788), which is NEWER than the
    # commit recorded in the portfolio matrix, so joining blindly would silently
    # re-point them at the pre-fix code they describe fixing.
    if c.get("commit"):
        continue
    if c["project"] in commit:
        c["commit"] = commit[c["project"]]
    elif "cross-cutting" in c["project"]:
        c["commit"] = "portfolio (cross-cutting)"
    else:
        # A claim pointing at a project that is not in the matrix is itself a
        # finding: the CV refers to work the portfolio cannot account for.
        c["commit"] = "UNKNOWN"

kept = [c for c in CLAIMS if c["grade"] == "KEEP"]
weakened = [c for c in CLAIMS if c["grade"] == "WEAKEN"]
deleted = [c for c in CLAIMS if c["grade"] == "DELETE"]
unknown = [c for c in CLAIMS if c["commit"] == "UNKNOWN"]

result = {
    "generated_at": datetime.now(timezone.utc).isoformat(),
    "policy": "a CV bullet that cannot be mapped to a measured run will not survive "
              "an interview question, so unmappable claims are marked for deletion "
              "rather than dressed up",
    "summary": {
        "total": len(CLAIMS),
        "keep": len(kept),
        "weaken": len(weakened),
        "delete": len(deleted),
        "unresolved_commits": len(unknown),
    },
    "claims": CLAIMS,
    "interview_risk": [
        "MAIA abstention fails. The first follow-up to any groundedness claim should "
        "be 'what happens when there is no answer', and the honest answer is that it "
        "usually generates one. Lead with retrieval quality and gate methodology.",
        "Factory's 212/212 needs a generated fixture; a clean clone fails 2 tests. "
        "Say the fixture is generated and how.",
        "MiniERP's 81 blocked tests are a deliberate safety guard, not a broken "
        "environment, but it needs saying before it is asked.",
        "ApexInspect has no accuracy number at all. Anyone asking how accurate the "
        "detector is is asking for something that was never measured.",
    ],
}
OUT.write_text(json.dumps(result, indent=2))

print(f"\n{len(CLAIMS)} claims: {len(kept)} KEEP, {len(weakened)} WEAKEN, "
      f"{len(deleted)} DELETE\n")
for c in CLAIMS:
    print(f"  {c['grade']:<7} [{c['commit']:<8}] {c['project'][:34]:<34} "
          f"{c['claim'][:60]}")
print("\nDELETE list (remove from CV, do not soften):")
for c in deleted:
    print(f"  - {c['project']}: {c['claim'][:88]}")
print(f"\nevidence: {OUT}")
