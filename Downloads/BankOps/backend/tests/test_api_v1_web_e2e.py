"""End-to-end verification of the /api/v1/* web workflow:
login (demo_maker) -> create stock & cash requests -> submit ->
login (demo_checker) -> approval inbox -> approve -> balances & reports update.
"""
from __future__ import annotations

import uuid

import pytest

DEMO_PASSWORD = "DemoPass123!"


def test_api_v1_maker_checker_web_flow(client):
    # 1. Maker logs in via JSON POST /api/v1/auth/login
    r = client.post(
        "/api/v1/auth/login",
        json={"username": "demo_maker", "password": DEMO_PASSWORD},
    )
    assert r.status_code == 200, r.text
    maker_token = r.json()["access_token"]
    maker_headers = {"Authorization": f"Bearer {maker_token}"}

    # Verify /api/v1/me
    me_resp = client.get("/api/v1/me", headers=maker_headers)
    assert me_resp.status_code == 200
    assert me_resp.json()["username"] == "demo_maker"

    # Fetch catalog & initial balances via /api/v1/*
    items = client.get("/api/v1/inventory/items", headers=maker_headers).json()["items"]
    locs = client.get("/api/v1/inventory/locations", headers=maker_headers).json()["locations"]
    boxes = client.get("/api/v1/cash-sim/boxes", headers=maker_headers).json()["boxes"]

    item_id = next(i["id"] for i in items if not i.get("tracked_serial"))
    loc_id = locs[0]["id"]
    cashbox_id = boxes[0]["id"]

    inv_bal_before = client.get(
        "/api/v1/inventory/balances", headers=maker_headers
    ).json()["balances"]
    qty_before = sum(
        float(b["quantity"])
        for b in inv_bal_before
        if b["location_id"] == loc_id and b["item_id"] == item_id
    )

    cash_bal_before = client.get(
        "/api/v1/cash-sim/balances", headers=maker_headers
    ).json()["balances"]
    amt_before = sum(
        float(b["amount"])
        for b in cash_bal_before
        if b["cashbox_id"] == cashbox_id
    )

    # 2. Maker creates + submits a Stock Request (receipt +15)
    sr_resp = client.post(
        "/api/v1/inventory/requests",
        headers={**maker_headers, "Idempotency-Key": str(uuid.uuid4())},
        json={
            "kind": "receipt",
            "dest_id": loc_id,
            "reason": "Nhập bổ sung giấy A4 từ web",
            "lines": [
                {
                    "item_id": item_id,
                    "quantity": "15",
                    "serials": [],
                }
            ],
        },
    )
    assert sr_resp.status_code in (200, 201), sr_resp.text
    sr_id = sr_resp.json()["id"]
    assert sr_resp.json()["status"] == "draft"
    assert len(sr_resp.json()["lines"]) == 1

    sub_sr = client.post(
        f"/api/v1/inventory/requests/{sr_id}/submit",
        headers={**maker_headers, "Idempotency-Key": str(uuid.uuid4())},
        json={},
    )
    assert sub_sr.status_code == 200, sub_sr.text
    assert sub_sr.json()["status"] == "submitted"

    # 3. Maker creates + submits a Cash Request (receipt +300000.00)
    cr_resp = client.post(
        "/api/v1/cash-sim/requests",
        headers={**maker_headers, "Idempotency-Key": str(uuid.uuid4())},
        json={
            "kind": "receipt",
            "cashbox_id": cashbox_id,
            "amount": "300000.00",
            "currency": "VND",
            "reason": "Thu quỹ mô phỏng từ web",
        },
    )
    assert cr_resp.status_code in (200, 201), cr_resp.text
    cr_id = cr_resp.json()["id"]
    assert cr_resp.json()["status"] == "draft"

    sub_cr = client.post(
        f"/api/v1/cash-sim/requests/{cr_id}/submit",
        headers={**maker_headers, "Idempotency-Key": str(uuid.uuid4())},
        json={},
    )
    assert sub_cr.status_code == 200, sub_cr.text
    assert sub_cr.json()["status"] == "submitted"

    # 4. Checker logs in via /api/v1/auth/login
    r_chk = client.post(
        "/api/v1/auth/login",
        json={"username": "demo_checker", "password": DEMO_PASSWORD},
    )
    assert r_chk.status_code == 200, r_chk.text
    checker_headers = {"Authorization": f"Bearer {r_chk.json()['access_token']}"}

    # Check approval inbox contains both submitted requests with enriched fields
    inbox = client.get(
        "/api/v1/approvals/inbox", headers=checker_headers
    ).json()["approvals"]
    inbox_by_id = {item["id"]: item for item in inbox}
    assert sr_id in inbox_by_id
    assert inbox_by_id[sr_id]["kind"] == "inventory"
    assert inbox_by_id[sr_id]["maker"]
    assert cr_id in inbox_by_id
    assert inbox_by_id[cr_id]["kind"] == "cash"
    assert "300000.00" in inbox_by_id[cr_id]["impact"]

    # 5. Checker approves both requests
    app_sr = client.post(
        f"/api/v1/inventory/requests/{sr_id}/approve",
        headers={**checker_headers, "Idempotency-Key": str(uuid.uuid4())},
        json={},
    )
    assert app_sr.status_code == 200, app_sr.text
    assert app_sr.json()["status"] == "posted"

    app_cr = client.post(
        f"/api/v1/cash-sim/requests/{cr_id}/approve",
        headers={**checker_headers, "Idempotency-Key": str(uuid.uuid4())},
        json={},
    )
    assert app_cr.status_code == 200, app_cr.text
    assert app_cr.json()["status"] == "posted"

    # 6. Verify balances, journals, and reports updated
    inv_bal_after = client.get(
        "/api/v1/inventory/balances", headers=checker_headers
    ).json()["balances"]
    qty_after = sum(
        float(b["quantity"])
        for b in inv_bal_after
        if b["location_id"] == loc_id and b["item_id"] == item_id
    )
    assert qty_after == pytest.approx(qty_before + 15.0)

    cash_bal_after = client.get(
        "/api/v1/cash-sim/balances", headers=checker_headers
    ).json()["balances"]
    amt_after = sum(
        float(b["amount"])
        for b in cash_bal_after
        if b["cashbox_id"] == cashbox_id
    )
    assert amt_after == pytest.approx(amt_before + 300000.0)

    journals = client.get(
        "/api/v1/cash-sim/journals", headers=checker_headers
    ).json()["journals"]
    assert any(j["request_id"] == cr_id and float(j["amount"]) == 300000.0 for j in journals)

    inv_rep = client.get(
        "/api/v1/reports/inventory-daily", headers=checker_headers
    ).json()["buckets"]
    assert len(inv_rep) >= 1

    cash_rep = client.get(
        "/api/v1/reports/cashflow-daily", headers=checker_headers
    ).json()["buckets"]
    assert len(cash_rep) >= 1
