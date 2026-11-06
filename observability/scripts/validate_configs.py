#!/usr/bin/env python3
"""Validate every config the observability stack ships.

CI cannot start Docker, so this is the substitute gate: parse all YAML/JSON and
assert the things a human reviewer would otherwise catch by hand (duplicate
dashboard uids, a dashboard referencing a series this repo never exposes).

Run from the observability/ directory:  python scripts/validate_configs.py
"""
import json
import re
import sys
from pathlib import Path

import yaml

HERE = Path(__file__).resolve().parents[1]


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def main() -> None:
    yamls = (sorted(HERE.glob("prometheus/*.yml"))
             + sorted(HERE.glob("alertmanager/*.yml"))
             + sorted(HERE.glob("blackbox/*.yml"))
             + sorted(HERE.glob("grafana/provisioning/**/*.yml"))
             + [HERE / "slo.yaml", HERE / "docker-compose.yml"])
    for f in yamls:
        try:
            yaml.safe_load(f.read_text())
        except Exception as exc:  # noqa: BLE001 - report, do not crash the run
            fail(f"{f.relative_to(HERE)} is not valid YAML: {exc}")

    dashboards = sorted(HERE.glob("grafana/dashboards/*.json"))
    uids, series = [], set()
    for f in dashboards:
        try:
            doc = json.loads(f.read_text())
        except Exception as exc:  # noqa: BLE001
            fail(f"{f.name} is not valid JSON: {exc}")
        uids.append(doc.get("uid"))
        for panel in doc.get("panels", []):
            for target in panel.get("targets", []):
                for name in re.findall(r"[a-zA-Z_][a-zA-Z0-9_]{3,}", target.get("expr", "")):
                    series.add(name)

    duplicates = {u for u in uids if uids.count(u) > 1}
    if duplicates:
        fail(f"duplicate dashboard uid(s): {sorted(duplicates)}")
    if not dashboards:
        fail("no dashboards found in grafana/dashboards/")

    # Every series a dashboard plots must be either produced by this repo or a
    # standard Prometheus/label token (job, le, status, up, quantile...).
    scrapes = yaml.safe_load((HERE / "prometheus" / "prometheus.yml").read_text())
    readiness_job = next(
        (job for job in scrapes.get("scrape_configs", [])
         if job.get("job_name") == "payment-readiness"),
        None,
    )
    if (
        not readiness_job
        or readiness_job.get("metrics_path") != "/probe"
        or readiness_job.get("static_configs", [{}])[0].get("targets") != ["__PAYMENT_READINESS_URL__"]
    ):
        fail("payment-readiness must be probed through the Blackbox Exporter")
    rules = yaml.safe_load((HERE / "prometheus" / "alerts.yml").read_text())
    alert_names = {
        rule.get("alert")
        for group in rules.get("groups", [])
        for rule in group.get("rules", [])
    }
    if "PaymentServiceReadinessDown" not in alert_names:
        fail("PaymentServiceReadinessDown alert rule is missing")

    alertmanager = yaml.safe_load((HERE / "alertmanager" / "alertmanager.yml").read_text())
    receivers = {receiver.get("name"): receiver for receiver in alertmanager.get("receivers", [])}
    webhook_configs = receivers.get("webhook-local", {}).get("webhook_configs", [])
    if alertmanager.get("route", {}).get("receiver") != "webhook-local":
        fail("Alertmanager default route must deliver to webhook-local")
    if not any(
        config.get("url") == "http://webhook-receiver:9094/alerts"
        for config in webhook_configs
    ):
        fail("Alertmanager webhook-local must target the local webhook receiver")

    datasource_files = sorted(HERE.glob("grafana/provisioning/datasources/*.yml"))
    default_datasources = [
        datasource
        for path in datasource_files
        for datasource in yaml.safe_load(path.read_text()).get("datasources", [])
        if datasource.get("isDefault") is True
    ]
    if len(default_datasources) != 1:
        fail(f"Grafana must provision exactly one default datasource, found {len(default_datasources)}")

    for job in scrapes.get("scrape_configs", []):
        print(f"  scrape job: {job['job_name']:16s} {job.get('metrics_path', '/metrics')}")

    print(f"OK  {len(yamls)} YAML + {len(dashboards)} dashboards, "
          f"{len(series)} distinct series referenced")


if __name__ == "__main__":
    main()
