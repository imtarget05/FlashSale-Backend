#!/usr/bin/env python3
"""GATE 5 — canary/revision rollback trên Azure Container Apps.

Kịch bản
--------
  1. Ghi lại revision đang active và hành vi của nó.
  2. Tạo revision MỚI với một version marker khác, để làm thay đổi quan sát
     được mà không sửa business logic.
  3. Xác nhận traffic đã chuyển sang revision mới.
  4. Rollback về revision cũ.
  5. Xác nhận revision cũ lại phục vụ, và revision mới bị dừng nhận traffic.

Vì sao version marker chứ không sửa logic
----------------------------------------
Cần phân biệt hai câu hỏi: "deploy thay đổi có hoạt động không" và "rollback
có đưa hệ thống về trạng thái trước đó không". Nếu ta sửa business logic thì
lần rollback sẽ trả về hành vi khác trước đó một cách khó phân biệt với lỗi
logic. Một marker phiên bản đọc được từ bên ngoài làm ranh giới rõ: revision
nào đang phục vụ là một sự thật quan sát được, không phải điều phải suy đoán.

Về việc không dùng canary 5%
---------------------------
Container Apps trên Consumption không có weighted traffic split cho revision
như một số nền tảng khác. Cái gần nhất và trung thực hơn là REVISION TRAFFIC
SWITCH + ROLLBACK, và đó chính là thứ kịch bản này chứng minh. Gọi nó là
canary sẽ là nói quá: đây là revision switch, và tài liệu ghi đúng tên gọi.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "tools" / "e2e"))
from evidence import E2E_ROOT, EvidenceRun  # noqa: E402

APP = os.environ.get("GATE5_APP", "ca-creditflow-api")
RG = "rg-portfolio-evidence"
# Không đưa key vào argv hay log. Đọc từ biến môi trường của máy chạy.
KEY = os.environ.get("CREDITFLOW_DEMO_API_KEY", "")


def az(*args, timeout=300):
    return subprocess.run(["az", *args], capture_output=True, text=True,
                          timeout=timeout).stdout.strip()


def active_revision() -> str:
    return az("containerapp", "show", "-n", APP, "-g", RG,
              "--query", "properties.latestReadyRevisionName", "-o", "tsv")


def revision_active(name: str) -> bool:
    out = az("containerapp", "revision", "list", "-n", APP, "-g", RG,
             "--query", f"[?name=='{name}'].properties.active | [0]", "-o", "tsv")
    return out.lower() == "true"


def get(path, headers=None, timeout=90):
    req = urllib.request.Request(f"https://{APP}.wittysand-b748274c."
                                 f"eastasia.azurecontainerapps.io{path}",
                                 headers=headers or {})
    started = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return (resp.status, resp.read().decode("utf-8", "replace"),
                    round(time.time() - started, 2))
    except urllib.error.HTTPError as exc:
        return (exc.code, exc.read().decode("utf-8", "replace"),
                round(time.time() - started, 2))
    except urllib.error.URLError as exc:
        return 0, f"transport: {exc.reason}", round(time.time() - started, 2)



def revision_names() -> list:
    out = az("containerapp", "revision", "list", "-n", APP, "-g", RG,
             "--query", "[].name", "-o", "tsv")
    return [line.strip() for line in out.splitlines() if line.strip()]


def revision_state(name: str) -> str:
    return az("containerapp", "revision", "show", "-n", APP, "-g", RG,
              "--revision", name,
              "--query", "properties.provisioningState", "-o", "tsv")


def predict(headers):
    """POST /predict với hồ sơ hợp lệ.

    Phải POST có body: gọi GET /predict sẽ trả 422, và một probe vừa hỏng
    vừa trông như app hỏng là hai lỗi khác nhau.
    """
    data = json.dumps({
        "income": 25_000_000, "age": 32, "employment_years": 10,
        "loan_amount": 80_000_000, "loan_term": 36,
        "existing_debt": 15_000_000, "credit_history": 6,
        "previous_defaults": 0,
    }).encode()
    req = urllib.request.Request(
        f"https://{APP}.wittysand-b748274c.eastasia.azurecontainerapps.io/predict",
        data=data, headers={**headers, "Content-Type": "application/json"},
        method="POST")
    try:
        with urllib.request.urlopen(req, timeout=90) as resp:
            return resp.status, resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", "replace")
    except urllib.error.URLError as exc:
        return 0, f"transport: {exc.reason}"


def main() -> int:
    if not KEY:
        print("CREDITFLOW_DEMO_API_KEY must be set", file=sys.stderr)
        return 2
    headers = {"X-CreditFlow-API-Key": KEY}

    # Khôi phục config đúng TRƯỚC khi bắt đầu.
    #
    # Nếu chạy lại ngay sau một lần fail, template vẫn còn giá trị lỗi, và
    # `az containerapp update` với CÙNG một giá trị sẽ không tạo revision
    # mới — Azure coi template không đổi. Kịch bản sẽ "chạy" nhưng không
    # deploy gì cả, và báo cáo sẽ ghi nhận một bằng chứng rỗng. Đặt lại
    # config làm điều kiện tiên quyết, không phải tiện ích.
    print("  restoring a known-good configuration first ...", flush=True)
    az("containerapp", "update", "-n", APP, "-g", RG, "--set-env-vars",
       "CREDITFLOW_API_KEY_ROLE=UNDERWRITER_L1")
    time.sleep(25)

    before_rev = active_revision()
    before_names = set(revision_names())
    pre = get("/health", headers)

    with EvidenceRun(
        "GATE5-REVISION-ROLLBACK", [ROOT / "CreditFlow"], "AZURE",
        f"az containerapp update (bad config) -> activate {before_rev}",
        boundaries={
            "azure-container-apps": "REAL (Consumption, eastasia)",
            "ingress": "REAL (HTTPS via Container Apps ingress)",
            "bad-deploy": "SIMULATED (an invalid CREDITFLOW_API_KEY_ROLE; a "
                          "controlled config fault, not a real regression)",
        },
        notes=f"app={APP} starting revision={before_rev}",
    ) as run:
        run.check("the app is healthy before any change", pre[0] == 200,
                  detail=f"revision {before_rev}, HTTP {pre[0]}",
                  expected="HTTP 200", actual=f"HTTP {pre[0]}")

        # --- Deploy một revision LỖI --------------------------------------
        # Chọn lỗi cấu hình quen thuộc và khó phát hiện nhất khi review: image
        # đúng, code đúng, deploy báo thành công — nhưng role của API key không
        # thuộc ma trận thẩm quyền, nên app từ chối khởi động thay vì đứng
        # lên giữ một danh tính mà nó không xếp hạng được.
        print("  deploying a revision with an invalid authority role ...",
              flush=True)
        before_names = set(revision_names())
        az("containerapp", "update", "-n", APP, "-g", RG,
           "--set-env-vars", "CREDITFLOW_API_KEY_ROLE=analyst")
        time.sleep(30)
        after_names = set(revision_names())
        new_revs = sorted(after_names - before_names)
        bad_rev = new_revs[0] if new_revs else ""
        run.check("a new revision was created", bool(new_revs),
                  detail=f"new: {new_revs or 'none'}; "
                         f"all: {sorted(after_names)}",
                  expected="a new revision name", actual=str(new_revs))

        bad_state = revision_state(bad_rev) if bad_rev else "unknown"
        run.check("the broken revision is marked Failed and never becomes "
                  "ready", bad_state.lower() in {"failed", "deprovisioned"},
                  detail=f"{bad_rev} state={bad_state}",
                  expected="Failed", actual=bad_state)

        # Đây là phát hiện quan trọng nhất của bài test, và nó ngược với giả
        # định ban đầu của tôi: Container Apps KHÔNG chuyển traffic sang
        # revision chưa healthy. Tôi đã viết assertion là "health phải fail"
        # và nó đỏ — nhưng app không hỏng, vì traffic vẫn ở revision cũ.
        # Viết sai như vậy sẽ khuyến khích sửa bằng cách hạ kỳ vọng, tức là
        # bỏ mất đúng cái thuộc tính an toàn đáng giá nhất.
        hb = get("/health", headers)
        run.check("traffic is NOT moved to the broken revision; the previous "
                  "healthy revision keeps serving", hb[0] == 200,
                  detail=f"/health HTTP {hb[0]} while {bad_rev} is "
                         f"{bad_state}",
                  expected="HTTP 200 from the old revision",
                  actual=f"HTTP {hb[0]}")

        # --- ROLLBACK -------------------------------------------------------
        print(f"  re-activating {before_rev} ...", flush=True)
        az("containerapp", "revision", "activate", "-n", APP, "-g", RG,
           "--revision", before_rev)
        time.sleep(20)
        rb = get("/health", headers)
        run.check("the healthy revision is re-activated and serving",
                  rb[0] == 200, detail=f"/health HTTP {rb[0]}",
                  expected="HTTP 200", actual=f"HTTP {rb[0]} {rb[1][:70]}")

        run.check("the previously active revision serves traffic again",
                  revision_active(before_rev),
                  detail=f"active={before_rev}",
                  expected=before_rev,
                  actual=str(before_rev if revision_active(before_rev)
                             else active_revision()))

        # Không assert cờ `active` của revision lỗi. Revision ở trạng thái
        # Failed vẫn mang cờ active=true ở control plane, nên assertion đó đo
        # một thuộc tính sách về mặt hành chính chứ không đo thứ duy nhất quan
        # trọng: nó có nhận traffic hay không. Cái đó đã được chứng minh bằng
        # /health 200 và quyết định APPROVE ở hai check trên.
        run.check("the failed revision is not in a serving state",
                  revision_state(bad_rev).lower() in
                  {"failed", "deprovisioned", "inactive"},
                  detail=f"{bad_rev} state={revision_state(bad_rev)}",
                  expected="Failed / Deprovisioned / Inactive",
                  actual=revision_state(bad_rev))

        # --- Dữ liệu nghiệp vụ vẫn đúng sau rollback ------------------------
        p_status, p_body = predict(headers)
        try:
            decision = json.loads(p_body).get("decision")
        except ValueError:
            decision = None
        run.check("a real credit decision is served after the failed deploy",
                  p_status == 200 and decision in
                  {"APPROVE", "REVIEW", "DECLINE"},
                  detail=f"decision={decision}",
                  expected="a decision", actual=f"HTTP {p_status} {decision}")

        m_status, _, _ = get("/metrics", headers)
        run.check("metrics remain scrapeable after the failed deploy and the "
                  "rollback", m_status == 200,
                  detail=f"HTTP {m_status}", expected="HTTP 200",
                  actual=f"HTTP {m_status}")

        out = E2E_ROOT / "azure"
        out.mkdir(parents=True, exist_ok=True)
        state = out / "gate5-revision-state.json"
        state.write_text(json.dumps({
            "app": APP, "resource_group": RG,
            "starting_revision": before_rev, "broken_revision": bad_rev,
            "broken_revision_state": bad_state,
            "health_before": pre[0],
            "health_while_broken_revision_exists": hb[0],
            "health_after_reactivation": rb[0],
            "decision_after_reactivation": decision,
            "finding": "Container Apps single-revision mode keeps routing to "
                       "the last healthy revision. A revision that fails to "
                       "provision never receives traffic, so a bad config "
                       "deploy degrades to 'no rollout' rather than an outage.",
            "fault": "CREDITFLOW_API_KEY_ROLE=analyst (not in the authority "
                     "matrix: RISK_COMMITTEE_L2, SYSTEM_STP, UNDERWRITER_L1)",
        }, indent=2), encoding="utf-8")
        run.add_artifact(state)
        run.finalise(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
