#!/usr/bin/env python3
"""Phase 19: negative-path security probe against deployed CreditFlow.

Bốn nhóm kiểm, theo đúng bốn loại rủi ro của một API quyết định tín dụng:

  1. Xác thực  — secret thiếu/sai, và việc app fail-closed
  2. Đầu vào   — thiếu trường, sai kiểu, vi phạm ràng buộc nghiệp vụ
  3. Rò rỉ     — không lộ hồ sơ khách hàng, không lộ bí mật
  4. Chính sách — ngưỡng quyết định, và invariant chống rò rỉ thời gian

Điểm khác biệt so với probe của MAIA: ở đây CÓ đường chức năng thật
(/predict trả quyết định), nên "invalid input bị chặn" là kết quả của việc
kiểm tra chạy được chứ không chỉ là app chết sớm. Vì vậy mỗi case đều có một
positive control đi kèm, để chứng minh endpoint thật sự làm việc chứ không
phải lúc nào cũng 500.

Về việc redact: mọi giá trị bí mật đi qua redact() trước khi vào output, và
evidence ghi ra cũng đã redact. File bằng chứng không được chứa secret.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

REDACTED = "<redacted>"
SECRET_PATTERNS = [
    re.compile(r"(eyJ[A-Za-z0-9_\-]{4,}\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+)"),
    re.compile(r"(?i)((?:api[_-]?key|secret|token|password)\"?\s*[:=]\s*\"?)([^\s\"',}]+)"),
    re.compile(r"(Bearer\s+)[A-Za-z0-9._\-]+", re.IGNORECASE),
]

# Hồ sơ synthetic dùng cho positive control. Giá trị này KHÔNG phải dữ liệu
# khách hàng thật; nó chỉ tồn tại để chứng minh /predict trả quyết định.
GOOD_PROFILE = {
    "income": 25_000_000,
    "age": 32,
    "employment_years": 10,
    "loan_amount": 80_000_000,
    "loan_term": 36,
    "existing_debt": 15_000_000,
    "credit_history": 6,
    "previous_defaults": 0,
}


def redact(value) -> str:
    if value is None:
        return value
    out = str(value)
    for pattern in SECRET_PATTERNS:
        if pattern.groups >= 2:
            out = pattern.sub(lambda m: f"{m.group(1)}{REDACTED}", out)
        else:
            out = pattern.sub(lambda m: REDACTED, out)
    return out


def call(base, method, path, *, key=None, body=None, timeout=60):
    data, headers = None, {}
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if key is not None:
        headers["X-CreditFlow-API-Key"] = key
    req = urllib.request.Request(f"{base}{path}", data=data, headers=headers,
                                 method=method)
    started = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return (resp.status, resp.read().decode("utf-8", "replace"),
                    round(time.time() - started, 3))
    except urllib.error.HTTPError as exc:
        return (exc.code, exc.read().decode("utf-8", "replace"),
                round(time.time() - started, 3))
    except urllib.error.URLError as exc:
        return 0, f"transport error: {exc.reason}", round(time.time() - started, 3)


def is_json(raw: str) -> dict:
    try:
        parsed = json.loads(raw)
        return parsed if isinstance(parsed, dict) else {"value": parsed}
    except ValueError:
        return {}



def probe(base: str, key: str, wrong_key: str) -> list[dict]:
    results: list[dict] = []

    def add(case, group, attack, control, status, raw, elapsed, *, expect=None,
            forbidden=(), check=None, extra=None):
        payload = is_json(raw)
        # `forbidden` có thể chứa chính bí mật đang kiểm (ví dụ API key), nên
        # danh sách này KHÔNG được ghi thô vào evidence. Lỗi này đã xảy ra một
        # lần: probe kiểm "/predict không trả lại api key" bằng cách đưa key
        # thật vào forbidden, rồi lưu forbidden vào JSON — bằng chứng lại chứa
        # đúng bí mật mà nó sinh ra để chứng minh không rò rỉ.
        leaked = [f for f in forbidden if f.lower() in raw.lower()]
        problem = check(payload) if check else None
        ok = not leaked and (expect is None or status == expect) and not problem
        row = {
            "case": case,
            "group": group,
            "attack": attack,
            "control": control,
            "expected_status": expect,
            "actual_status": status,
            "forbidden_count": len(forbidden),
            "forbidden_substrings_checked": [
                "<the api key itself>" if f == key else redact(f)
                for f in forbidden
            ],
            "leaked": ["<the api key itself>" if f == key else redact(f)
                       for f in leaked],
            "control_violation": problem,
            "response_excerpt": redact(raw)[:300],
            "elapsed_seconds": elapsed,
            "verdict": "PASS" if ok else "FAIL",
        }
        if extra:
            row.update(extra)
        results.append(row)

    # ---------- 1. Xác thực ----------
    # LƯU Ý VỀ PHẠM VI. Repo tự định nghĩa lớp auth là "money-moving and PII
    # endpoints" (xem tests/test_api_auth.py). /predict và /predict/graph cố ý
    # ĐỂ MỞ: chúng nhận feature rồi trả điểm, không đọc hồ sơ khách hàng nào.
    # Vì vậy kỳ vọng đúng cho chúng là 200, không phải 401 — và chính việc
    # deploy lên internet buộc phải nói rõ mức phơi bày, xem `exposure` ở
    # cuối file. Endpoint thực sự phải khóa là nhóm dưới đây.
    s, r, e = call(base, "POST", "/predict", body=GOOD_PROFILE)
    add("auth:no_key_predict_open_by_design", "authentication",
        "POST /predict with no API key",
        "scoring endpoint is intentionally unauthenticated in this repo",
        s, r, e, expect=200, forbidden=["Traceback"],
        check=lambda p: None if "decision" in p else "no decision returned",
        extra={"exposure": "unauthenticated model oracle on a public URL",
               "data_exposed": "none: the call takes features and returns a "
                               "score, it does not read stored customer rows"})

    s, r, e = call(base, "GET", "/applications")
    add("auth:applications_requires_key", "authentication",
        "GET /applications with no API key",
        "PII endpoint is refused without a credential", s, r, e, expect=401,
        forbidden=["Traceback", "/app/"])

    s, r, e = call(base, "GET", "/disbursements")
    add("auth:disbursements_requires_key", "authentication",
        "GET /disbursements with no API key",
        "money-moving ledger is refused without a credential", s, r, e,
        expect=401, forbidden=["Traceback", "/app/"])

    s, r, e = call(base, "POST", "/predict/graph/any-thread/approve", body={})
    add("auth:approve_requires_key", "authentication",
        "POST /predict/graph/<id>/approve with no API key",
        "the approval that can move money is refused without a credential",
        s, r, e, expect=401, forbidden=["Traceback", "/app/"])

    s, r, e = call(base, "GET", "/applications", key=wrong_key)
    add("auth:wrong_key_rejected", "authentication",
        "GET /applications with a wrong API key",
        "a wrong credential is refused even on a protected route", s, r, e,
        expect=401, forbidden=["Traceback"])

    s, r, e = call(base, "GET", "/applications", key=key)
    add("positive:applications_with_key", "functional",
        "GET /applications with a valid API key",
        "a correct credential is accepted", s, r, e, expect=200,
        forbidden=["Traceback"])

    # ---------- 2. Positive control ----------
    s, r, e = call(base, "POST", "/predict", key=key, body=GOOD_PROFILE)
    add("positive:valid_profile", "functional", "none (positive control)",
        "a valid profile reaches the model and yields a decision", s, r, e,
        expect=200,
        check=lambda p: None if {"decision", "risk_probability"} <= set(p)
        else "no decision in the response",
        extra={"decision": is_json(r).get("decision"),
               "risk_level": is_json(r).get("risk_level"),
               "risk_probability": is_json(r).get("risk_probability")})

    s, r, e = call(base, "GET", "/health", key=key)
    add("positive:model_loaded", "functional", "none (positive control)",
        "the model bundle is loaded and reported", s, r, e, expect=200,
        check=lambda p: None if p.get("model_loaded") is True
        else "model_loaded is not true")

    # ---------- 3. Đầu vào ----------
    missing = {k: v for k, v in list(GOOD_PROFILE.items())[:-3]}
    s, r, e = call(base, "POST", "/predict", key=key, body=missing)
    add("input:missing_features", "input", "profile with three features removed",
        "missing required features are rejected by the schema", s, r, e,
        expect=422, forbidden=["risk_probability", "Traceback"])

    s, r, e = call(base, "POST", "/predict", key=key, body={})
    add("input:empty_body", "input", "empty JSON object",
        "an empty body is rejected", s, r, e, expect=422,
        forbidden=["risk_probability", "Traceback"])

    s, r, e = call(base, "POST", "/predict", key=key,
                   body={**GOOD_PROFILE, "income": "not-a-number"})
    add("input:wrong_type", "input", "income sent as a string",
        "type validation rejects it at the boundary", s, r, e, expect=422,
        forbidden=["risk_probability", "Traceback"])

    s, r, e = call(base, "POST", "/predict", key=key,
                   body={**GOOD_PROFILE, "age": 17})
    add("input:below_minimum_age", "input", "age 17, below the ge=18 floor",
        "out-of-range values are rejected", s, r, e, expect=422,
        forbidden=["risk_probability"])

    s, r, e = call(base, "POST", "/predict", key=key,
                   body={**GOOD_PROFILE, "income": -5_000_000})
    add("input:negative_income", "input", "negative income against gt=0",
        "out-of-range values are rejected", s, r, e, expect=422,
        forbidden=["risk_probability"])

    # Ràng buộc liên trường: employment_years <= age - 18
    s, r, e = call(base, "POST", "/predict", key=key,
                   body={**GOOD_PROFILE, "age": 30, "employment_years": 25})
    add("input:cross_field_violation", "input",
        "employment_years 25 with age 30, violating employment_years <= age-18",
        "the cross-field business rule is enforced, not just per-field ranges",
        s, r, e, expect=422, forbidden=["risk_probability"],
        check=lambda p: None if "employment" in json.dumps(p).lower()
        else "the violated field is not named in the error")

    # ---------- 4. Rò rỉ ----------
    s, r, e = call(base, "GET", "/audit/unknown-application-id", key=key)
    add("leak:unknown_audit_id", "leak",
        "GET /audit/<random id> for an application that does not exist",
        "a missing record discloses nothing about other records", s, r, e,
        expect=None, forbidden=["password", "api_key", "Traceback", "/app/"],
        check=lambda p: None if not p.get("audit_trail")
        else "audit data returned for an unknown id")

    s, r, e = call(base, "GET", "/metrics", key=key)
    add("leak:metrics_no_credentials", "leak", "GET /metrics with a valid key",
        "runtime metrics expose counters, not credentials or customer rows",
        s, r, e, expect=200,
        forbidden=[key, "api_key", "password", "Traceback"],
        check=lambda p: None if "runtime" in p else "runtime metrics missing")

    s, r, e = call(base, "GET", "/metrics/prometheus", key=key)
    add("leak:prometheus_no_credentials", "leak",
        "GET /metrics/prometheus with a valid key",
        "Prometheus exposition carries no secret", s, r, e, expect=200,
        forbidden=[key, "api_key", "password"])

    # ---------- 5. Chính sách ----------
    # Hồ sơ rủi ro cao: nhiều lần trễ nợ, ít lịch sử tín dụng.
    risky = {
        "income": 8_000_000, "age": 23, "employment_years": 1,
        "loan_amount": 200_000_000, "loan_term": 60,
        "existing_debt": 180_000_000, "credit_history": 0,
        "previous_defaults": 3,
    }
    s, r, e = call(base, "POST", "/predict", key=key, body=risky)
    payload = is_json(r)
    add("policy:high_risk_profile", "policy",
        "high-risk profile: 3 prior defaults, DTI far above 1",
        "the decision respects the configured thresholds", s, r, e, expect=200,
        check=_check_threshold_policy,
        extra={"decision": payload.get("decision"),
               "risk_level": payload.get("risk_level"),
               "risk_probability": payload.get("risk_probability")})

    return results


def _check_threshold_policy(payload: dict):
    """Quyết định phải nhất quán với xác suất rủi ro và ngưỡng đã cấu hình.

    Đây là chỗ dễ làm ống: response vẫn trả 200 và vẫn có trường `decision`,
    nhưng quyết định lại không khớp với xác suất và ngưỡng. Kiểm status code
    sẽ bỏ sót đúng lỗi đó — một API quyết định tín dụng vẫn "thành công" về
    mặt HTTP trong khi đang cấp khoản vay cho người không đủ điều kiện.
    """
    prob = payload.get("risk_probability")
    decision = str(payload.get("decision", "")).upper()
    threshold = payload.get("threshold") or {}
    if prob is None or not decision:
        return "response missing risk_probability or decision"
    approve_max = threshold.get("approve_max")
    if approve_max is None:
        return "threshold block absent, cannot verify the policy"
    if prob <= approve_max and decision != "APPROVE":
        return f"prob {prob} <= approve_max {approve_max} but decision {decision}"
    if prob > approve_max and decision == "APPROVE":
        return f"prob {prob} > approve_max {approve_max} but decision APPROVE"
    return None


def exposure_review() -> dict:
    """Mức phơi bày thật của một deployment công khai, viết ra thẳng.

    Probe tìm ra rằng /predict không cần API key. Điều đó KHÔNG phải bug so với
    phạm vi mà repo tự tuyên bố, nhưng nó là một thực tế khi app nằm trên URL
    công khai, và việc chỉ ghi "PASS" cho nó là che mất thông tin quan trọng
    nhất mà lần deploy này tạo ra. Phần này ghi lại điều đó.
    """
    return {
        "finding": "POST /predict and POST /predict/graph are unauthenticated",
        "severity_in_repo_terms": "by design",
        "severity_in_deployment_terms": "HIGH",
        "why_both": (
            "The repo scopes auth to 'money-moving and PII endpoints' "
            "(tests/test_api_auth.py), and /predict reads no stored customer "
            "row: it takes features and returns a score. But on a public URL it "
            "is an unauthenticated model oracle, and /drift additionally "
            "returns training reference statistics such as n_rows=3500."
        ),
        "protected_and_verified": [
            "GET /applications", "GET /disbursements",
            "POST /predict/graph/{thread_id}/approve",
        ],
        "unprotected_on_a_public_url": [
            "POST /predict (model oracle)",
            "POST /predict/graph (starts a decision workflow)",
            "GET /drift (training reference aggregates)",
            "GET /model/info (model metadata)",
        ],
        "customer_data_exposed": False,
        "not_proven_by_this_deployment": (
            "The point-in-time invariant (feature_timestamp > "
            "prediction_timestamp must not participate) lives in "
            "pipeline/feature_store/, which is not on the HTTP serving path. "
            "It is verified by the repository test suite, not by this Azure "
            "run. Claiming otherwise would overstate the evidence."
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--commit-sha", required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--image-digest", required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    key = os.environ.get("CREDITFLOW_DEMO_API_KEY", "")
    if not key:
        print("CREDITFLOW_DEMO_API_KEY must be set", file=sys.stderr)
        return 2
    # Một secret sai-but-valid, sinh từ chính key hợp lệ để chắc chắn khác.
    wrong_key = (key[:-1] + ("A" if key[-1] != "A" else "B"))

    started = time.time()
    results = probe(args.base, key, wrong_key)
    passed = sum(r["verdict"] == "PASS" for r in results)
    groups = sorted({r["group"] for r in results})

    report = {
        "execution_location": "AZURE",
        "service": "Azure Container Apps",
        "sku": "Consumption",
        "resource_group": "rg-portfolio-evidence",
        "environment": "cae-portfolio",
        "container_app": "ca-creditflow-api",
        "target": args.base,
        "phase": 19,
        "suite": "creditflow-negative-security",
        "commit_sha": args.commit_sha,
        "image": args.image,
        "image_digest": args.image_digest,
        "revision": args.revision,
        "min_replicas": 0,
        "max_replicas": 1,
        "groups_covered": groups,
        "started_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(started)),
        "duration_seconds": round(time.time() - started, 2),
        "credentials_redacted": True,
        "data_used": "synthetic credit profiles only; no real financial record",
        "total": len(results),
        "passed": passed,
        "failed": len(results) - passed,
        "results": results,
        "exposure_review": exposure_review(),
        "status": "VERIFIED" if passed == len(results) else "FAILED",
    }

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)

    for row in results:
        note = row.get("control_violation") or (f"leaked={row['leaked']}"
                                                if row.get("leaked") else "")
        print(f"  [{row['verdict']}] {row['case']:32} HTTP "
              f"{row['actual_status']} {note}")
    print(f"\n{passed}/{len(results)} passed -> {args.out}")
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
