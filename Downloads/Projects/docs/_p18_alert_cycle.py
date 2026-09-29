#!/usr/bin/env python
"""
Chu kỳ alert THẬT: failure -> metric -> alert -> recovery.

Không giả lập trạng thái alert. Script này thực sự giết process API, chờ
Prometheus phát hiện, chờ rule kích hoạt, rồi khởi động lại và chờ alert
tự resolve.

Ghi kết quả vào docs/evidence/observability/alerts/alert_cycle.json

Chạy: ApexInspect-AI/.venv-apex2/bin/python docs/_p18_alert_cycle.py
"""
from __future__ import annotations

import json
import os
import subprocess
import time
from datetime import datetime, timezone

REPO = "/Users/mainguyenbinhtan/Downloads/Projects"
APEX = os.path.join(REPO, "ApexInspect-AI")
PY = os.path.join(APEX, ".venv-apex2", "bin", "python")
PROM = "http://localhost:9103"
AM = "http://localhost:9303"
OUT = os.path.join(REPO, "docs", "evidence", "observability",
                   "alerts", "alert_cycle.json")


def promql(q: str):
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "8", "--get", f"{PROM}/api/v1/query",
         "--data-urlencode", f"query={q}"],
        capture_output=True, text=True).stdout
    try:
        res = json.loads(r)["data"]["result"]
    except (ValueError, KeyError):
        return None
    return res[0]["value"][1] if res else None


def alert_state(job: str):
    r = subprocess.run(
        ["curl", "-sS", "--max-time", "8", f"{PROM}/api/v1/alerts"],
        capture_output=True, text=True).stdout
    try:
        for a in json.loads(r)["data"]["alerts"]:
            if a["labels"].get("job") == job:
                return a["state"], a["labels"]["alertname"]
    except (ValueError, KeyError):
        pass
    return None, None


def am_has(job: str) -> bool:
    r = subprocess.run(["curl", "-sS", "--max-time", "8", f"{AM}/api/v2/alerts"],
                       capture_output=True, text=True).stdout
    try:
        return any(job in str(a["labels"]) for a in json.loads(r))
    except ValueError:
        return False


def wait(pred, tries=24, gap=5):
    for i in range(1, tries + 1):
        v = pred()
        if v:
            return v, i
        time.sleep(gap)
    return None, tries


def main() -> int:
    cyc = {"started_at": datetime.now(timezone.utc).isoformat(),
           "job": "apexinspect"}
    before = promql('up{job="apexinspect"}')
    cyc["step_1_before_kill"] = {"up": before,
                                 "alert": alert_state("apexinspect")}

    # --- failure injection: kill the real process
    subprocess.run(["pkill", "-f", "uvicorn src.backend.main"],
                   capture_output=True)
    cyc["step_2_killed"] = {"at": datetime.now(timezone.utc).isoformat()}

    up0, n = wait(lambda: promql('up{job="apexinspect"}') == "0")
    cyc["step_3_metric_changed"] = {"up": up0, "polls": n,
                                    "note": "Prometheus scrape that báo target down"}

    st, n = wait(lambda: (alert_state("apexinspect")[0] == "firing")
                 or None)
    cyc["step_4_alert_fired"] = {"state": st, "polls": n,
                                 "rule": "ServiceDown (expr: up == 0)"}
    cyc["step_5_alertmanager"] = {"received": am_has("apexinspect"),
                                  "api": AM}

    # --- recovery
    subprocess.Popen([PY, "-m", "uvicorn", "src.backend.main:app",
                      "--host", "127.0.0.1", "--port", "7860"],
                     cwd=APEX, stdout=open("/tmp/apex_api5.log", "w"),
                     stderr=subprocess.STDOUT)
    up1, n = wait(lambda: promql('up{job="apexinspect"}') == "1")
    cyc["step_6_restored"] = {"up": up1, "polls": n}

    # Alert resolve: hai tín hiệu khác nhau đều chứng minh đã resolve —
    # series ALERTS biến mất khỏi TSDB (null), hoặc state=resolved.
    #
    # Dùng cả hai vì "không còn series" và "còn series nhưng state=resolved"
    # là hai trạng thái khác nhau của Prometheus, và chỉ một trong hai có
    # thể xảy ra tuỳ phiên bản/cấu hình. Nếu chỉ chờ một trạng thái thì
    # kết luận sẽ phụ thuộc vào điều ta không kiểm soát.
    def resolved():
        # TÊN BIẾN CỤC BỘ. Dùng lại `st` của step 4 sẽ ghi đè trạng thái
        # "firing" đã ghi nhận, khiến cycle_complete tính sai về False.
        current, _ = alert_state("apexinspect")
        if current == "resolved":
            return "resolved_state"
        v = promql('ALERTS{alertname="ServiceDown",job="apexinspect"}')
        if v is None:
            return "series_gone"          # đã bị xoá khỏi TSDB
        if v == "0":
            return "alert_metric_zero"
        return None

    how, n = wait(resolved)
    cyc["step_7_alert_resolved"] = {
        "resolved": bool(how), "how": how, "polls": n,
        "alerts_metric": promql(
            'ALERTS{alertname="ServiceDown",job="apexinspect"}'),
        "current_api_state": alert_state("apexinspect")[0],
        "note": ("`up=1` + không còn alert firing là đủ để kết luận service "
                 "đã phục hồi. Giá trị null nghĩa là series ALERTS đã bị xoá "
                 "khỏi TSDB — đó là Prometheus đã dọn, tức alert đã hết."),
    }
    cyc["finished_at"] = datetime.now(timezone.utc).isoformat()
    # `wait()` trả về GIÁ TRỊ của hàm kiểm, không phải giá trị metric. Ở
    # step 3 và 6 hàm kiểm trả bool (đã bằng "0"/"1"), nên phải so sánh với
    # True/False — so với "0"/"1" sẽ luôn sai và cycle_complete mãi False
    # dù chu kỳ đã chạy đúng.
    cyc["checks"] = {
        "metric_went_down": up0 is True,
        "alert_fired": st is True,
        "service_restored": up1 is True,
        "alert_resolved": bool(how),
    }
    cyc["cycle_complete"] = all(cyc["checks"].values())

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(cyc, fh, indent=2, ensure_ascii=False)
    print(json.dumps(cyc, indent=2, ensure_ascii=False))
    return 0 if cyc["cycle_complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
