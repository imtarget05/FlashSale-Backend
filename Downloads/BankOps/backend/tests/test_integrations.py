"""Tests for /integrations/* and /integration-runs/* endpoints (Track B)."""
from __future__ import annotations

import hashlib
import hmac
import io
import json
import time
import uuid

DEMO_PASSWORD = "DemoPass123!"


def _login(client, username: str) -> dict[str, str]:
    r = client.post("/auth/login", data={"username": username, "password": DEMO_PASSWORD})
    assert r.status_code == 200, r.text
    return {"Authorization": f"Bearer {r.json()['access_token']}"}


def _seed_sku_and_loc(client, headers: dict[str, str]) -> tuple[str, str]:
    items = client.get("/inventory/items", headers=headers).json()["items"]
    locs = client.get("/inventory/locations", headers=headers).json()["locations"]
    sku = next(i["sku"] for i in items if not i.get("tracked_serial"))
    loc_code = locs[0]["code"]
    return sku, loc_code


class TestIntegrationsUploadAndConfirm:
    def test_csv_upload_with_invalid_row_isolates_errors_and_blocks_confirm(self, client):
        """G1: Dòng lỗi không chặn dòng hợp lệ lúc preview, nhưng chặn confirm."""
        h = _login(client, "demo_maker")
        sku, loc_code = _seed_sku_and_loc(client, h)
        unique_tag = uuid.uuid4().hex[:8]
        csv_bytes = (
            "sku,location_code,quantity,source_row_key\n"
            f"{sku},{loc_code},5,valid-{unique_tag}\n"
            f"INVALID_SKU_{unique_tag},{loc_code},-3,bad-{unique_tag}\n"
        ).encode()

        up = client.post(
            "/integrations/uploads",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
            files={"file": ("mixed.csv", io.BytesIO(csv_bytes), "text/csv")},
            data={"type": "csv"},
        )
        assert up.status_code == 200, up.text
        body = up.json()
        assert body["valid_count"] == 1
        assert body["invalid_count"] == 1
        run_id = body["run_id"]

        # Preview via both canonical and alias routes
        prev1 = client.get(f"/integrations/runs/{run_id}/preview", headers=h)
        prev2 = client.get(f"/integration-runs/{run_id}/staging", headers=h)
        assert prev1.status_code == 200 and prev2.status_code == 200
        assert prev1.json()["invalid_count"] == 1
        assert prev2.json()["valid_count"] == 1

        # Confirm must reject because invalid_count > 0
        conf = client.post(
            f"/integrations/runs/{run_id}/confirm",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
        )
        assert conf.status_code == 409
        assert conf.json()["error"]["code"] == "CONFIRM_HAS_ERRORS"

    def test_json_upload_dedup_and_confirm_creates_draft_not_ledger(self, client):
        """Upload hợp lệ -> confirm tạo InventoryRequest draft, dedup theo fingerprint."""
        h = _login(client, "demo_maker")
        sku, loc_code = _seed_sku_and_loc(client, h)
        tag = uuid.uuid4().hex[:8]
        payload_bytes = json.dumps(
            {
                "rows": [
                    {
                        "sku": sku,
                        "location_code": loc_code,
                        "quantity": 4,
                        "source_row_key": f"json-{tag}",
                    }
                ]
            }
        ).encode("utf-8")

        up1 = client.post(
            "/integrations/uploads",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
            files={"file": ("items.json", io.BytesIO(payload_bytes), "application/json")},
            data={"type": "json"},
        )
        assert up1.status_code == 200, up1.text
        b1 = up1.json()
        assert b1["deduplicated"] is False
        assert b1["valid_count"] == 1
        assert b1["invalid_count"] == 0
        run_id = b1["run_id"]

        # Re-uploading identical content deduplicates by fingerprint
        up2 = client.post(
            "/integrations/uploads",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
            files={"file": ("items.json", io.BytesIO(payload_bytes), "application/json")},
            data={"type": "json"},
        )
        assert up2.status_code == 200
        assert up2.json()["deduplicated"] is True
        assert up2.json()["run_id"] == run_id

        # Movements before confirm
        mv_before = len(
            client.get("/inventory/movements", headers=h).json().get("movements", [])
        )

        # Confirm creates draft request(s) and replays on same Idempotency-Key
        idem_key = str(uuid.uuid4())
        c1 = client.post(
            f"/integration-runs/{run_id}/confirm",
            headers={**h, "Idempotency-Key": idem_key},
        )
        assert c1.status_code == 200, c1.text
        cb1 = c1.json()
        assert cb1["status"] == "confirmed"
        assert len(cb1["draft_requests"]) == 1

        c_replay = client.post(
            f"/integration-runs/{run_id}/confirm",
            headers={**h, "Idempotency-Key": idem_key},
        )
        assert c_replay.status_code == 200
        assert c_replay.json()["draft_requests"] == cb1["draft_requests"]

        # Confirm with a different Idempotency-Key returns 409 STATE_CONFLICT
        c_dup = client.post(
            f"/integration-runs/{run_id}/confirm",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
        )
        assert c_dup.status_code == 409
        assert c_dup.json()["error"]["code"] == "STATE_CONFLICT"

        # No movement was written directly to ledger
        mv_after = len(
            client.get("/inventory/movements", headers=h).json().get("movements", [])
        )
        assert mv_after == mv_before


class TestGoogleSheetsAndWebhooks:
    def test_google_sheets_fetch_mocked_and_validation(self, client, monkeypatch):
        from app.config import settings

        h = _login(client, "demo_maker")
        sku, loc_code = _seed_sku_and_loc(client, h)

        # Invalid sheet_id -> 422 INVALID_SHEET_ID
        bad = client.post(
            "/integrations/google-sheets/fetch",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
            json={"sheet_id": "bad!id", "tab": "Sheet1"},
        )
        assert bad.status_code == 422
        assert bad.json()["error"]["code"] == "INVALID_SHEET_ID"

        # Mock API key + httpx response
        monkeypatch.setattr(settings, "google_sheets_api_key", "fake-sheets-key")
        tag = uuid.uuid4().hex[:8]

        class _FakeResp:
            status_code = 200
            text = "OK"

            def json(self):
                return {
                    "values": [
                        ["sku", "location_code", "quantity", "source_row_key"],
                        [sku, loc_code, "9", f"sheet-{tag}"],
                    ]
                }

        class _FakeAsyncClient:
            def __init__(self, *args, **kwargs):
                pass

            async def __aenter__(self):
                return self

            async def __aexit__(self, *args):
                pass

            async def get(self, url, params=None):
                return _FakeResp()

        monkeypatch.setattr("app.api.routes.integrations.httpx.AsyncClient", _FakeAsyncClient)

        ok = client.post(
            "/integrations/google-sheets/fetch",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
            json={"sheet_id": "1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms", "tab": "Sheet1"},
        )
        assert ok.status_code == 200, ok.text
        obody = ok.json()
        assert obody["valid_count"] == 1
        assert obody["invalid_count"] == 0
        assert obody["simulation"] is True

    def test_apps_script_webhook_hmac_replay_and_dedup(self, client, monkeypatch):
        from app.config import settings

        secret = "test-webhook-hmac-secret"
        monkeypatch.setattr(settings, "apps_script_webhook_secret", secret)

        h = _login(client, "demo_maker")
        sku, loc_code = _seed_sku_and_loc(client, h)

        # Create an apps_script connection
        conn_r = client.post(
            "/integrations/connections",
            headers={**h, "Idempotency-Key": str(uuid.uuid4())},
            json={"type": "apps_script", "name": "Test Apps Script"},
        )
        assert conn_r.status_code == 201, conn_r.text
        conn_id = conn_r.json()["id"]

        event_id = f"ev-{uuid.uuid4().hex[:10]}"
        raw_payload = json.dumps(
            {
                "event_id": event_id,
                "rows": [
                    {
                        "sku": sku,
                        "location_code": loc_code,
                        "quantity": 3,
                        "source_row_key": f"wh-{event_id}",
                    }
                ],
            }
        ).encode("utf-8")

        now_ts = str(int(time.time()))
        valid_sig = hmac.new(
            secret.encode(), f"{now_ts}.".encode() + raw_payload, hashlib.sha256
        ).hexdigest()

        # 1. Bad signature -> 401 WEBHOOK_BAD_SIGNATURE
        r_bad_sig = client.post(
            f"/integrations/webhooks/{conn_id}",
            content=raw_payload,
            headers={
                "Content-Type": "application/json",
                "X-BankOps-Timestamp": now_ts,
                "X-BankOps-Signature": "0" * 64,
            },
        )
        assert r_bad_sig.status_code == 401
        assert r_bad_sig.json()["error"]["code"] == "WEBHOOK_BAD_SIGNATURE"

        # 2. Expired timestamp (>300s) -> 401 WEBHOOK_REPLAY
        old_ts = str(int(time.time()) - 600)
        old_sig = hmac.new(
            secret.encode(), f"{old_ts}.".encode() + raw_payload, hashlib.sha256
        ).hexdigest()
        r_replay = client.post(
            f"/integrations/webhooks/{conn_id}",
            content=raw_payload,
            headers={
                "Content-Type": "application/json",
                "X-BankOps-Timestamp": old_ts,
                "X-BankOps-Signature": old_sig,
            },
        )
        assert r_replay.status_code == 401
        assert r_replay.json()["error"]["code"] == "WEBHOOK_REPLAY"

        # 3. Valid webhook -> 200 staged
        r_ok = client.post(
            f"/integrations/webhooks/{conn_id}",
            content=raw_payload,
            headers={
                "Content-Type": "application/json",
                "X-BankOps-Timestamp": now_ts,
                "X-BankOps-Signature": valid_sig,
            },
        )
        assert r_ok.status_code == 200, r_ok.text
        assert r_ok.json()["deduplicated"] is False
        run_id = r_ok.json()["run_id"]

        # 4. Duplicate webhook push -> deduplicated=True
        r_dup = client.post(
            f"/integrations/webhooks/{conn_id}",
            content=raw_payload,
            headers={
                "Content-Type": "application/json",
                "X-BankOps-Timestamp": now_ts,
                "X-BankOps-Signature": valid_sig,
            },
        )
        assert r_dup.status_code == 200
        assert r_dup.json()["deduplicated"] is True
        assert r_dup.json()["run_id"] == run_id
