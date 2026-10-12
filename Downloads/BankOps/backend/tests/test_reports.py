"""Tests for /reports/* endpoints (Track A: daily/monthly charts & stagnation)."""
from __future__ import annotations

import uuid
from decimal import Decimal

DEMO_PASSWORD = "DemoPass123!"


def _login(client, username: str) -> dict[str, str]:
    r = client.post("/auth/login", data={"username": username, "password": DEMO_PASSWORD})
    assert r.status_code == 200, r.text
    return {"Authorization": f"Bearer {r.json()['access_token']}"}


class TestReportsUnit:
    def test_severity_thresholds_cover_all_levels(self):
        from app.api.routes.reports import _severity

        assert _severity(None, 30, 60, 90) == "critical"
        assert _severity(95, 30, 60, 90) == "critical"
        assert _severity(90, 30, 60, 90) == "critical"
        assert _severity(75, 30, 60, 90) == "stagnant"
        assert _severity(60, 30, 60, 90) == "stagnant"
        assert _severity(45, 30, 60, 90) == "watch"
        assert _severity(30, 30, 60, 90) == "watch"
        assert _severity(10, 30, 60, 90) == "normal"
        assert _severity(0, 30, 60, 90) == "normal"


class TestReportsIntegration:
    def test_inventory_daily_and_monthly_aggregation(self, client):
        maker_h = _login(client, "demo_maker")
        checker_h = _login(client, "demo_checker")

        items_r = client.get("/inventory/items", headers=maker_h)
        locs_r = client.get("/inventory/locations", headers=maker_h)
        assert items_r.status_code == 200 and locs_r.status_code == 200
        item_id = next(
            i["id"] for i in items_r.json()["items"] if not i.get("tracked_serial")
        )
        loc_id = locs_r.json()["locations"][0]["id"]

        # Create, submit, approve a receipt of 7 units
        req_r = client.post(
            "/inventory/requests",
            headers={**maker_h, "Idempotency-Key": str(uuid.uuid4())},
            json={
                "kind": "receipt",
                "source_id": loc_id,
                "reason": "reports test receipt",
                "lines": [{"item_id": item_id, "quantity": "7"}],
            },
        )
        assert req_r.status_code in (200, 201), req_r.text
        rid = req_r.json()["id"]
        sub_r = client.post(
            f"/inventory/requests/{rid}/submit",
            headers={**maker_h, "If-Match": "1"},
        )
        assert sub_r.status_code == 200, sub_r.text
        app_r = client.post(
            f"/inventory/requests/{rid}/approve",
            headers={**checker_h, "Idempotency-Key": str(uuid.uuid4())},
        )
        assert app_r.status_code == 200, app_r.text

        daily = client.get("/reports/inventory-daily", headers=maker_h)
        assert daily.status_code == 200, daily.text
        dbody = daily.json()
        assert dbody.get("simulation") is True
        assert dbody.get("unit") == "quantity"
        assert len(dbody.get("buckets", [])) >= 1
        latest = dbody["buckets"][-1]
        assert Decimal(latest["inbound_qty"]) >= Decimal("7")
        assert Decimal(latest["net_qty"]) == (
            Decimal(latest["inbound_qty"]) - Decimal(latest["outbound_qty"])
        )

        monthly = client.get("/reports/inventory-monthly", headers=maker_h)
        assert monthly.status_code == 200, monthly.text
        mbody = monthly.json()
        assert mbody.get("simulation") is True
        assert len(mbody.get("buckets", [])) >= 1
        mb = mbody["buckets"][-1]
        assert len(mb["bucket"]) == 7  # YYYY-MM
        assert Decimal(mb["inbound_qty"]) >= Decimal("7")

    def test_cashflow_daily_and_monthly_single_leg_accounting(self, client):
        maker_h = _login(client, "demo_maker")
        checker_h = _login(client, "demo_checker")

        before_r = client.get("/reports/cashflow-daily", headers=checker_h)
        assert before_r.status_code == 200
        before_in = sum(
            Decimal(b["total_in"]) for b in before_r.json().get("buckets", [])
        )

        # Post a receipt of 250000.00 VND
        cr = client.post(
            "/cash-sim/requests",
            headers={**maker_h, "Idempotency-Key": str(uuid.uuid4())},
            json={
                "cashbox_id": "11111111-1111-4111-8111-111111111111",
                "kind": "receipt",
                "amount": "250000.00",
                "reason": "reports cashflow test",
            },
        )
        assert cr.status_code in (200, 201), cr.text
        cid = cr.json()["id"]
        sub = client.post(f"/cash-sim/requests/{cid}/submit", headers=maker_h)
        assert sub.status_code == 200
        ap = client.post(
            f"/cash-sim/requests/{cid}/approve",
            headers={**checker_h, "Idempotency-Key": str(uuid.uuid4())},
        )
        assert ap.status_code == 200

        after_r = client.get("/reports/cashflow-daily", headers=checker_h)
        assert after_r.status_code == 200
        abody = after_r.json()
        assert abody.get("simulation") is True
        assert abody.get("currency") == "VND"
        after_in = sum(Decimal(b["total_in"]) for b in abody.get("buckets", []))
        # Must increase by exactly 250000.00 (not 500000.00 from double-counting both legs)
        assert after_in - before_in == Decimal("250000.00")

        monthly_r = client.get("/reports/cashflow-monthly", headers=checker_h)
        assert monthly_r.status_code == 200
        mbody = monthly_r.json()
        assert mbody.get("simulation") is True
        assert mbody.get("currency") == "VND"
        assert sum(Decimal(b["total_in"]) for b in mbody.get("buckets", [])) == after_in

    def test_stagnation_report_and_invalid_date_filter(self, client):
        maker_h = _login(client, "demo_maker")
        stag = client.get(
            "/reports/stagnation?watch_days=30&stagnant_days=60&critical_days=90",
            headers=maker_h,
        )
        assert stag.status_code == 200, stag.text
        sbody = stag.json()
        assert sbody.get("simulation") is True
        assert sbody.get("thresholds") == {"watch": 30, "stagnant": 60, "critical": 90}
        assert isinstance(sbody.get("items"), list)
        for item in sbody["items"]:
            assert item["severity"] in ("normal", "watch", "stagnant", "critical")
            assert Decimal(item["quantity"]) > 0

        bad_date = client.get(
            "/reports/inventory-daily?date_from=not-a-date",
            headers=maker_h,
        )
        assert bad_date.status_code == 422
        assert bad_date.json()["error"]["code"] == "INVALID_DATE"
