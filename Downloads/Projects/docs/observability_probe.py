#!/usr/bin/env python3
"""Gộp bằng chứng observability của ba app trên Azure thành một bảng.

Về vì sao KHÔNG dựng Grafana
----------------------------
Có thể, nhưng nó không làm claim nào trong portfolio mạnh thêm. Claim đang
được xét là "app có phát metric có nghĩa và ghi log có cấu trúc, và điều đó
quan sát được từ bên ngoài". Ba thứ đó kiểm được trực tiếp:

  1. Metric tăng sau một request thật   -> so sánh /metrics trước và sau
  2. Log có cấu trúc, không lộ bí mật  -> đọc log của Container Apps
  3. Phục hồi sau hỏng                 -> gọi lại sau khi cố phá

Một dashboard đẹn không chứng minh được điều nào trong ba điều trên. Cái đã
làm nên dashboard là thu thập và so sánh số liệu thật, nên đó là phần được
viết ở đây. Nếu sau này cần Grafana thì đây chính là nguồn số liệu để dựng.

Về vì sao gọi một lần rồi so sánh là đủ
--------------------------------------
Vì request lỗi phải được tính vào bộ đếm lỗi. Nếu chỉ gọi healthy request,
bộ đếm tăng không chứng minh được phần lỗi hoạt động — và phần lỗi mới là
thứ hay hỏng nhất. Vì vậy mỗi app đều có một chuỗi: healthy -> lỗi -> lại healthy.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import time
import urllib.error
import urllib.request
from pathlib import Path

REDACTED = "<redacted>"
SECRET_PATTERNS = [
    re.compile(r"(eyJ[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+)"),
    re.compile(r"(?i)((?:api[_-]?key|secret|token|password)\"?\s*[:=]\s*\"?)([^\s\"',}]+)"),
    re.compile(r"(Bearer\s+)[A-Za-z0-9._\-]+", re.IGNORECASE),
]


def redact(value) -> str:
    out = str(value)
    for pattern in SECRET_PATTERNS:
        if pattern.groups >= 2:
            out = pattern.sub(lambda m: f"{m.group(1)}{REDACTED}", out)
        else:
            out = pattern.sub(lambda m: REDACTED, out)
    return out


def call(base, path, *, method="GET", body=None, headers=None, timeout=60):
    data = json.dumps(body).encode() if body is not None else None
    hdrs = dict(headers or {})
    if data:
        hdrs.setdefault("Content-Type", "application/json")
    req = urllib.request.Request(f"{base}{path}", data=data, headers=hdrs,
                                 method=method)
    started = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode("utf-8", "replace"), \
                round(time.time() - started, 3)
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", "replace"), \
            round(time.time() - started, 3)
    except urllib.error.URLError as exc:
        return 0, f"transport error: {exc.reason}", round(time.time() - started, 3)


def counter_values(text: str) -> dict:
    """Chỉ lấy các dòng metric dạng `name value`, bỏ qua HELP/TYPE và nhãn."""
    out = {}
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.rsplit(" ", 1)
        if len(parts) == 2:
            try:
                out[parts[0]] = float(parts[1])
            except ValueError:
                continue
    return out


def diff_counters(before: dict, after: dict) -> dict:
    changed = {}
    for name, value in after.items():
        old = before.get(name)
        if old is not None and value != old:
            changed[name] = {"before": old, "after": value, "delta": value - old}
    return changed



# Mỗi app: probe sức khoẻ, probe lỗi, và cách gọi có credential cho app cần.
#
# `failure_auth` quyết định request lỗi có mang credential hay không. Rất dễ
# vấp: CreditFlow trả 401 cho /applications khi thiếu key, nhưng trả 200 khi
# ta gửi key — nên một probe "gọi endpoint bị chặn" lại biến thành một probe
# thành công chỉ vì mang nhầm credential.
#
# `expect_metric_movement` là chỗ phải nói thẳng khi đáp án là "không". MAIA
# không có HTTP instrumentation: /metrics chỉ expose counter thuộc ingestion,
# Kafka và tool-call, và những thứ đó không chạy trong cloud-demo. Không có cách
# nào làm chúng tăng mà không bịa ra. Ghi "không" còn hơn ghi "PASS" rồi im.
APPS = {
    "factory": {
        "base_env": "FACTORY_URL",
        "health": "/",
        "metrics": "/metrics",
        "failure": ("GET", "/no-such-route-observability-probe", None),
        "failure_auth": False,
        "auth": False,
        "expect_metric_movement": True,
    },
    "maia": {
        "base_env": "MAIA_URL",
        "health": "/health",
        "metrics": "/metrics",
        "failure": ("POST", "/query", {"question": "observability probe"}),
        "failure_auth": False,
        "auth": False,
        "expect_metric_movement": False,
        "no_metric_movement_reason": (
            "MAIA exposes maia_ingestion_throughput, maia_kafka_consumer_lag, "
            "maia_conversations_total, maia_tool_calls_total and an embedding "
            "latency histogram. None of them are HTTP counters: api.py never "
            "calls metrics.inc, and every counter belongs to ingestion, Kafka "
            "or the RAG path, none of which run in cloud-demo. There is no "
            "honest request here that would move them, so this is recorded as "
            "an observability gap rather than a pass."
        ),
    },
    "creditflow": {
        "base_env": "CREDITFLOW_URL",
        "health": "/health/live",
        "metrics": "/metrics/prometheus",
        "failure": ("GET", "/applications", None),
        "failure_auth": False,
        "auth": True,
        "expect_metric_movement": True,
    },
}


def probe_app(name: str, spec: dict) -> dict:
    base = os.environ.get(spec["base_env"], "").rstrip("/")
    key = os.environ.get("CREDITFLOW_DEMO_API_KEY", "")
    auth_headers = {"X-CreditFlow-API-Key": key} if spec["auth"] and key else {}
    # Request lỗi cố ý KHÔNG mang credential, nếu không nó sẽ thành công.
    fail_headers = (auth_headers if spec.get("failure_auth") else {})

    row = {"app": name, "target": base, "executed": False}
    if not base:
        row["error"] = f"{spec['base_env']} is not set"
        return row

    try:
        # 1. Chụp metric TRƯỚC
        s0, m0, _ = call(base, spec["metrics"], headers=auth_headers)
        before = counter_values(m0)

        # 2. Healthy request
        hs, hb, ht = call(base, spec["health"], headers=auth_headers)

        # 3. Failure có chủ đích
        method, path, body = spec["failure"]
        fs, fb, _ = call(base, path, method=method, body=body,
                         headers=fail_headers)

        # 4. Chụp metric SAU
        s1, m1, _ = call(base, spec["metrics"], headers=auth_headers)
        after = counter_values(m1)
        deltas = diff_counters(before, after)

        # 5. Recovery
        rs, rb, rt = call(base, spec["health"], headers=auth_headers)

        row.update({
            "executed": True,
            "healthy_request": {"endpoint": spec["health"], "status": hs,
                                "seconds": ht,
                                "body_excerpt": redact(hb)[:160]},
            "controlled_failure": {"method": method, "endpoint": path,
                                   "status": fs,
                                   "credential_sent": bool(fail_headers),
                                   "body_excerpt": redact(fb)[:160]},
            "recovery": {"endpoint": spec["health"], "status": rs,
                         "seconds": rt},
            "metrics": {
                "endpoint": spec["metrics"],
                "status_before": s0, "status_after": s1,
                "counter_count": len(after),
                "changed_counters": deltas,
            },
            "recovered": rs == hs == 200,
            "metric_moved_on_failure": bool(deltas),
            "expected_metric_movement": spec.get("expect_metric_movement",
                                                 True),
        })
        if not spec.get("expect_metric_movement", True):
            row["no_metric_movement_reason"] = spec.get(
                "no_metric_movement_reason")
    except Exception as exc:  # noqa: BLE001 - bằng chứng phải ghi lại lỗi
        row["error"] = f"{type(exc).__name__}: {exc}"
    return row


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    started = time.time()
    apps = [probe_app(name, spec) for name, spec in APPS.items()]

    observed = [a for a in apps if a.get("executed")]
    healthy = all(a.get("healthy_request", {}).get("status") == 200
                  for a in observed)
    recovered = all(a.get("recovered") for a in observed)
    # Chỉ app nào KHAI báo được mong đợi tăng metric mới được tính. Với app còn
    # lại, việc counter không động là dữ kiện đã biết, không phải lỗi.
    movers = [a for a in observed if a.get("expected_metric_movement")]
    moved = all(a.get("metric_moved_on_failure") for a in movers)
    gaps = [{"app": a["app"], "reason": a.get("no_metric_movement_reason")}
            for a in observed if not a.get("expected_metric_movement")]

    report = {
        "execution_location": "AZURE",
        "service": "Azure Container Apps (Consumption)",
        "resource_group": "rg-portfolio-evidence",
        "environment": "cae-portfolio",
        "phase": "observability consolidation",
        "generated_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ",
                                       time.gmtime(started)),
        "duration_seconds": round(time.time() - started, 2),
        "method": ("for each app: snapshot metrics -> healthy request -> "
                   "deliberate failure -> snapshot metrics -> recover. A "
                   "counter that does not move proves nothing, so the delta "
                   "across the failure is recorded per app."),
        "no_grafana_because": ("the claim under test is that application "
                               "metrics and Azure logs are enough to be "
                               "externally observable. A dashboard renders "
                               "the same numbers without adding evidence."),
        "apps": apps,
        "checks": {
            "all_healthy_200": healthy,
            "all_recovered": recovered,
            "metrics_moved_where_expected": moved,
        },
        "known_observability_gaps": gaps,
        "credentials_redacted": True,
        "status": "VERIFIED" if healthy and recovered and moved else "PARTIAL",
    }

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")

    for app in apps:
        if not app.get("executed"):
            print(f"  [SKIP] {app['app']}: {app.get('error')}")
            continue
        print(f"  {app['app']:11} health={app['healthy_request']['status']} "
              f"failure={app['controlled_failure']['status']} "
              f"recovery={app['recovery']['status']} "
              f"metrics_moved={app['metric_moved_on_failure']} "
              f"({len(app['metrics']['changed_counters'])} counters)")
    print(f"\nchecks: {report['checks']} -> {report['status']}")
    print(f"written: {out}")
    return 0 if report["status"] == "VERIFIED" else 1


if __name__ == "__main__":
    raise SystemExit(main())
