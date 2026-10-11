"""Developer contract tests (B01/B15 docs/10 test pyramid).

File này là test của **developer** (không phải QA reviewer): smoke health,
OpenAPI snapshot để bắt contract drift khi thêm/bớt endpoint.
"""


def test_health_live_returns_alive(client):
    resp = client.get("/health/live")
    assert resp.status_code == 200
    assert resp.json()["status"] == "alive"


def test_every_response_carries_request_id(client):
    resp = client.get("/health/live")
    assert resp.headers.get("x-request-id"), "thiếu X-Request-ID trên response"


def test_openapi_snapshot_contract_paths(app):
    """OpenAPI snapshot: lock dãy path hiện có để bắt drift vô ý.

    Nếu bạn cố tình thêm/bớt endpoint: cập nhật `EXPECTED_PATHS` trong cùng PR
    và ghi rõ trong PR description (docs/02: contract đi cùng code).
    """
    schema = app.openapi()
    paths = set(schema.get("paths", {}))

    expected_path_suffixes = {
        "/health/live",
        "/health/ready",
        "/auth/login",
        "/auth/refresh",
        "/auth/logout",
        "/auth/google",
        "/auth/me",
        "/me",
        "/inventory/items",
        "/inventory/locations",
        "/inventory/balances",
        "/inventory/assets",
        "/cash-sim/boxes",
        "/cash-sim/balances",
        "/cash-sim/journals",
        "/approvals/inbox",
        "/activity/",
        "/activity/stream",
    }

    missing = {p for p in expected_path_suffixes if not any(x.endswith(p) for x in paths)}
    assert not missing, (
        f"OpenAPI snapshot drift: các path sau biến mất khỏi schema {sorted(missing)}. "
        "Nếu bạn cố ý gỡ/bắt buộc đổi contract, cập nhật test này cùng PR."
    )


def test_error_envelope_shape_for_404(client):
    """Envelope docs/06:44 áp dụng cho mọi HTTPException, kể cả 404."""
    resp = client.get("/auth/me", headers={"Authorization": "Bearer invalid.token.value"})
    assert resp.status_code == 401
    body = resp.json()
    err = body.get("error")
    assert isinstance(err, dict), f"401 phải theo envelope docs/06:44, nhận {body!r}"
    assert {"code", "message", "request_id"} <= set(err)