#!/usr/bin/env python3
"""Per-namespace resource-request inventory from a rendered manifest set (P03 gate).

Why this exists: the AKS-SRE-Platform (P03) capacity gate needs to know how much
CPU/memory a namespace DEMANDS before the cluster exists to schedule it. The
rendered manifest is the desired state, so it is the only honest source —
summing `resources.requests` per workload (scaled by `spec.replicas`) gives the
schedulers-eye view of what a namespace must be able to fit. Requests, not
limits, are the schedulable quantity: a pod with a 2-CPU limit but a 100m request
schedules onto a 0.5-CPU node and then gets CPU-throttled, which is a runtime
problem, not a scheduling one.

Offline by construction: input is a rendered YAML file (or the prod overlay,
rendered with `kubectl kustomize` — no API server, no kubeconfig). Exit code:

  0  every workload container declares cpu+memory requests
  1  at least one container is missing requests — the table would silently
     under-count demand, so the gate fails instead of printing a lie

Usage: capacity-inventory.py [rendered.yaml] [--json]
"""
import json
import os
import re
import subprocess
import sys

try:
    import yaml
except ImportError:
    sys.exit("PyYAML is required: pip install pyyaml")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OVERLAY = os.path.join(
    ROOT, "infrastructure", "kubernetes", "overlays", "prod")

WORKLOAD_KINDS = ("Deployment", "StatefulSet", "DaemonSet", "Job")


def cpu_millicores(v):
    if v is None:
        return None
    s = str(v).strip()
    if s.endswith("m"):
        return int(s[:-1])
    return int(float(s) * 1000)


def mem_mib(v):
    if v is None:
        return None
    s = str(v).strip()
    factors = {"Ki": 1 / 1024, "Mi": 1, "Gi": 1024,
               "Ti": 1024 * 1024, "K": 1 / 1024, "M": 1, "G": 1024}
    for suffix, f in factors.items():
        if s.endswith(suffix):
            return int(float(s[: -len(suffix)]) * f)
    return int(float(s) / (1024 * 1024))  # plain bytes


def fmt_cpu(m):
    return "unset" if m is None else (f"{m}m" if m % 1000 else f"{m // 1000}")


def fmt_mem(m):
    return "unset" if m is None else (f"{m}Mi" if m < 1024 else f"{m / 1024:g}Gi")


def load_docs(path_or_none):
    if path_or_none:
        with open(path_or_none) as fh:
            text = fh.read()
    else:
        r = subprocess.run(["kubectl", "kustomize", DEFAULT_OVERLAY],
                           capture_output=True, text=True, timeout=120)
        if r.returncode != 0:
            sys.exit(f"kustomize render failed:\n{r.stderr}")
        text = r.stdout
    return [d for d in yaml.safe_load_all(text) if d]


def inventory(docs):
    rows = []
    for d in docs:
        kind, meta = d.get("kind"), d.get("metadata") or {}
        if kind not in WORKLOAD_KINDS:
            continue
        name = meta.get("name") or "<unnamed>"
        ns = meta.get("namespace") or "<none>"
        spec = d.get("spec") or {}
        replicas = int(spec.get("replicas") or 1)  # a Job has none -> 1
        pod = ((spec.get("template") or {}).get("spec") or {})
        for c in pod.get("containers") or []:
            cname = c.get("name") or "<unnamed>"
            res = c.get("resources") or {}
            req, lim = res.get("requests") or {}, res.get("limits") or {}
            rows.append({
                "namespace": ns,
                "workload": f"{kind}/{name}",
                "container": cname,
                "replicas": replicas,
                "cpu_request_m": cpu_millicores(req.get("cpu")),
                "mem_request_mib": mem_mib(req.get("memory")),
                "cpu_limit_m": cpu_millicores(lim.get("cpu")),
                "mem_limit_mib": mem_mib(lim.get("memory")),
            })
    return rows


def main():
    args = [a for a in sys.argv[1:]]
    as_json = "--json" in args
    paths = [a for a in args if a != "--json"]
    if len(paths) > 1:
        sys.exit("usage: capacity-inventory.py [rendered.yaml] [--json]")
    rows = inventory(load_docs(paths[0] if paths else None))

    missing = [r for r in rows
               if r["cpu_request_m"] is None or r["mem_request_mib"] is None]
    if as_json:
        print(json.dumps({"rows": rows, "missing_requests": [m["namespace"] + "/" + m["workload"] for m in missing]},
                         indent=2))
    else:
        print(f"{'namespace':<18} {'workload':<28} {'container':<14} "
              f"{'rep':>3} {'cpu req':>8} {'mem req':>9} {'cpu lim':>8} {'mem lim':>9}")
        for r in rows:
            print(f"{r['namespace']:<18} {r['workload']:<28} {r['container']:<14} "
                  f"{r['replicas']:>3} {fmt_cpu(r['cpu_request_m']):>8} "
                  f"{fmt_mem(r['mem_request_mib']):>9} {fmt_cpu(r['cpu_limit_m']):>8} "
                  f"{fmt_mem(r['mem_limit_mib']):>9}")
        print()
        totals = {}
        for r in rows:
            t = totals.setdefault(r["namespace"], {"cpu": 0, "mem": 0})
            t["cpu"] += (r["cpu_request_m"] or 0) * r["replicas"]
            t["mem"] += (r["mem_request_mib"] or 0) * r["replicas"]
        print(f"{'namespace':<18} {'TOTAL cpu requests':>20} {'TOTAL mem requests':>22}"
              + ("   (replica-scaled)" if rows else ""))
        for ns, t in sorted(totals.items()):
            print(f"{ns:<18} {fmt_cpu(t['cpu']):>20} {fmt_mem(t['mem']):>22}")

    if missing:
        print("\nFAIL: containers without resource requests (scheduling math would under-count):",
              file=sys.stderr)
        for m in missing:
            print(f"  - {m['namespace']}/{m['workload']}/{m['container']}", file=sys.stderr)
        return 1
    if not rows:
        print("FAIL: no workload objects found in input", file=sys.stderr)
        return 1
    print(f"\nOK: {len(rows)} containers across {len(totals)} namespace(s); "
          f"all declare cpu+memory requests.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
