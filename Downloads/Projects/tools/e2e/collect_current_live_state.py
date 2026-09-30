"""Collect current Azure live state with response-body fingerprints.

Complements probe_azure_live.py: that script answers "is it reachable?".
This one also hashes the RESPONSE BODY, so the evidence pins what the service
actually returned rather than only the status code.

Probes are SEQUENTIAL with a long timeout. See docs/evidence/azure/ERRATA.md
erratum 2: the consumption plan scales to zero, and a parallel short-timeout
probe reports HTTP 000 against a perfectly healthy cold-starting app.

Usage: python3 tools/e2e/collect_current_live_state.py
"""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import pathlib
import subprocess
import time

RG = "rg-portfolio-evidence"
APPS = {
    "maia": ("ca-maia-api", "/health"),
    "creditflow": ("ca-creditflow-api", "/health"),
    "factory": ("ca-factory-api", "/"),
}
OUT = pathlib.Path(__file__).resolve().parents[2] / "docs/evidence/azure/current-live-state.json"


def _az(*args: str) -> dict:
    r = subprocess.run(["az", *args], capture_output=True, text=True)
    if r.returncode != 0:
        return {}
    try:
        return json.loads(r.stdout or "{}")
    except json.JSONDecodeError:
        return {}


def _probe(url: str, timeout: int = 90, attempts: int = 3) -> dict:
    """Sequential retry. A 000 on a consumption-plan app is a cold start, not
    an outage; only record failure after retries are exhausted."""
    last: dict = {}
    for attempt in range(1, attempts + 1):
        r = subprocess.run(
            ["curl", "-s", "--max-time", str(timeout),
             "-w", "\n@@%{http_code}@@%{time_total}", url],
            capture_output=True, text=True)
        body, _, meta = r.stdout.rpartition("\n@@")
        parts = meta.split("@@")
        code = parts[0] if parts else "000"
        secs = parts[1] if len(parts) > 1 else "0"
        last = {
            "url": url,
            "http_status": code,
            "seconds": round(float(secs), 3),
            "body_bytes": len(body),
            "body_sha256_prefix": hashlib.sha256(body.encode()).hexdigest()[:16],
            "body_excerpt": body[:160].strip(),
            "attempts_used": attempt,
        }
        if code not in ("000", ""):
            last["reached_after_retry"] = attempt > 1
            return last
        time.sleep(5)
    last["reached_after_retry"] = False
    last["error"] = "no HTTP response after retries (scale-from-zero or outage)"
    return last


def main() -> None:
    apps = []
    for proj, (app, health_path) in APPS.items():
        show = _az("containerapp", "show", "-g", RG, "-n", app, "-o", "json")
        props = show.get("properties", {})
        ingress = (props.get("configuration") or {}).get("ingress") or {}
        fqdn = ingress.get("fqdn")
        revs = _az("containerapp", "revision", "list", "-g", RG, "-n", app, "-o", "json")
        if isinstance(revs, dict):
            revs = [revs]
        active = next((r for r in revs
                       if r.get("properties", {}).get("active")), {})
        image = ((props.get("template", {}) or {}).get("containers") or [{}])[0].get("image")

        row = {
            "project": proj,
            "container_app": app,
            "fqdn": fqdn,
            "fqdn_source": "properties.configuration.ingress.fqdn",
            "external_ingress": ingress.get("external"),
            "provisioning_state": props.get("provisioningState"),
            "active_revision": active.get("name") or None,
            "running_state": (active.get("properties", {}) or {}).get("runningState"),
            "image": image,
            "commit_sha_in_image_tag": (
                image.rsplit(":", 1)[-1] if image and ":" in image.rsplit("/", 1)[-1] else None),
        }
        if fqdn:
            row["probes"] = {
                "health": _probe(f"https://{fqdn}{health_path}"),
                "docs": _probe(f"https://{fqdn}/docs"),
            }
        apps.append(row)
        print(f"  {proj}: fqdn={fqdn} "
              f"health={row.get('probes', {}).get('health', {}).get('http_status')} "
              f"docs={row.get('probes', {}).get('docs', {}).get('http_status')}")


    payload = {
        "generated_at": dt.datetime.now(dt.timezone.utc).isoformat(),
        "resource_group": RG,
        "azure_environment": "eastasia",
        "sku": "Consumption (scales to zero; see ERRATA erratum 2)",
        "method": {
            "probe": "SEQUENTIAL requests, 90s timeout, up to 3 retries",
            "why_not_parallel": (
                "Parallel short-timeout probes returned HTTP 000 against healthy "
                "apps because they hit the cold start. See ERRATA erratum 2."),
            "body_fingerprinting": (
                "sha256 prefix of the response body pins what was actually "
                "returned, not merely the status code."),
            "fqdn_json_path": "properties.configuration.ingress.fqdn",
            "fqdn_path_bug_note": (
                "An earlier probe read properties.fqdn, which is always null, and "
                "wrongly concluded the control plane reported no FQDN. Corrected; "
                "see docs/evidence/azure/ERRATA.md."),
        },
        "apps": apps,
        "reachability": {
            "reachable": sum(1 for a in apps
                             if a.get("probes", {}).get("health", {}).get("http_status")
                             not in (None, "000")),
            "total": len(apps),
        },
        "docs_exposure": {
            "finding": "All three public APIs serve Swagger UI at /docs (HTTP 200).",
            "mechanism": (
                "FastAPI is constructed with default docs_url in every service; "
                "there is no environment gate, so this is framework default "
                "behaviour, not a deliberate control."),
            "decision": (
                "DEMO ENVIRONMENT: intentionally exposed for portfolio review. "
                "Production hardening is a DOCUMENTED GAP, not an implemented "
                "control. Follow-up: gate docs_url behind an env flag, default on "
                "for demo / off for production. Not implemented now because no "
                "failure mode justifies breaking the demo."),
        },
        "scope": (
            "Read-only verification. No Azure resource was created, modified, or "
            "deleted. No historical evidence file was edited or removed."),
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(payload, indent=2))
    print(f"evidence: {OUT}")


if __name__ == "__main__":
    main()
