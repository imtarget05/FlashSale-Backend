"""Tổng hợp và kiểm tra bằng chứng Phase 19 của MAIA trên Azure.

Hai việc, tách bạch:
  1. In ra bảng các case và trạng thái degradation, để đọc được bằng mắt.
  2. AUDIT bí mật: quét chính file evidence xem có JWT hay mật khẩu lọt vào
     không. Đây là điều kiện để evidence được chấp nhận — một file bằng chứng
     chứa credential thì không phải bằng chứng, nó là một sự cố.
"""
import json
import re
import sys
from pathlib import Path

EVIDENCE = Path(sys.argv[1] if len(sys.argv) > 1
                else "docs/evidence/azure/security/maia-phase19.json")

JWT = re.compile(r"eyJ[A-Za-z0-9_\-]{6,}\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+")
ASSIGNED_SECRET = re.compile(
    r'(?i)"?(?:password|secret|api[_-]?key|token)"?\s*[:=]\s*"([^"]{8,})"')


def main() -> int:
    data = json.loads(EVIDENCE.read_text(encoding="utf-8"))
    facts = data.get("degradation_and_findings", {})

    print(f"suite    : {data.get('suite')}")
    print(f"location : {data.get('execution_location')} / {data.get('service')}")
    print(f"commit   : {data.get('commit_sha')}")
    print(f"digest   : {data.get('image_digest')}")
    print(f"revision : {data.get('revision')}")
    print(f"result   : {data.get('passed')}/{data.get('total')} "
          f"-> {data.get('status')}")
    print()

    print(f"{'case':34} {'status':>7}  {'verdict':7}  leak")
    print("-" * 70)
    for row in data.get("results", []):
        leak = ",".join(row.get("leaked", [])) or "-"
        print(f"{row['case'][:34]:34} {str(row['actual_status']):>7}  "
              f"{row['verdict']:7}  {leak}")
    print()

    if facts:
        rag = facts.get("rag_query_path", {})
        ready = facts.get("readiness", {})
        auth = facts.get("auth_write_path", {})
        defect = facts.get("known_defect", {})

        print("DEGRADATION (recorded, not hidden)")
        print(f"  RAG /query functional : {rag.get('functional')} "
              f"(HTTP {rag.get('status')})")
        print(f"  /ready honest degrade : {ready.get('honest_degradation')} "
              f"({ready.get('reported')})")
        print(f"  auth write path works : {auth.get('functional')} "
              f"(HTTP {auth.get('status')})")
        print(f"  known defect          : {defect.get('exception')}")
        print()

    raw = EVIDENCE.read_text(encoding="utf-8")
    jwt_hits = JWT.findall(raw)
    secret_hits = [m for m in ASSIGNED_SECRET.findall(raw)
                   if m.lower() not in ("redacted", "<redacted>")
                   and not m.startswith("Probe-")]

    print("SECRET AUDIT of the evidence file")
    print(f"  JWT values present    : {len(jwt_hits)}")
    print(f"  assigned secrets      : {len(secret_hits)}")
    clean = not jwt_hits and not secret_hits
    print(f"  verdict               : {'CLEAN' if clean else 'LEAK DETECTED'}")

    if not clean:
        print("\nEvidence must not be accepted while this fails.")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
