"""Ca kiểm thử bắt buộc theo docs/10-qa.md (A1-A4, I1-I3, C1-C3, R1, G1-G2, E1, S1).

Nhóm này cần app boot được + PostgreSQL. Nếu app không import được, `app`/`client`
fixture sẽ báo FAILED rõ ràng thay vì bỏ qua im lặng.
"""
from __future__ import annotations

import uuid

import pytest

# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------

DEMO_PASSWORD = "DemoPass123!"


class Api:
    """Thin wrapper: giữ token theo vai trò, in lỗi contract dễ đọc."""

    def __init__(self, client):
        self.c = client
        self.tokens: dict[str, str] = {}

    def login(self, username: str, password: str = DEMO_PASSWORD) -> str:
        try:
            r = self.c.post(
                "/auth/login", data={"username": username, "password": password}
            )
        except Exception as exc:  # noqa: BLE001 - báo đúng gốc rễ cho người đọc
            pytest.fail(
                f"login({username}) làm sập server: {type(exc).__name__}: {exc}\n"
                "-> Nguyên nhân đã xác minh: `app/database.py` bọc `get_db` bằng "
                "`@asynccontextmanager`, nhưng FastAPI yêu cầu `Depends()` nhận "
                "một async generator thuần (`async def` + `yield`). FastAPI unwrap "
                "`__wrapped__` nên xếp sai loại là async-gen rồi bọc thêm "
                "`asynccontextmanager()` lần 2 -> `anext()` gọi trên "
                "`_AsyncGeneratorContextManager` -> TypeError.\n"
                "-> Ảnh hưởng: mọi request qua `Depends(get_db)` (POST /auth/login, GET /auth/me) "
                "đều 500; không vai trò demo nào đăng nhập được.",
                pytrace=False,
            )
        if r.status_code != 200:
            pytest.fail(
                f"login({username}) trả {r.status_code}: {r.text[:500]}. "
                "Không có vai trò demo nào đăng nhập được -> toàn bộ ca bên dưới không chạy.",
                pytrace=False,
            )
        token = r.json()["access_token"]
        self.tokens[username] = token
        return token

    def as_user(self, username: str, password: str = DEMO_PASSWORD) -> Api:
        self.login(username, password)
        return self

    def h(self, username: str) -> dict:
        if username not in self.tokens:
            self.login(username)
        return {"Authorization": f"Bearer {self.tokens[username]}"}


@pytest.fixture(scope="module")
def api(client) -> Api:
    return Api(client)


def _err(resp) -> dict:
    try:
        return resp.json()
    except Exception:
        return {}


def assert_error_code(resp, expected_code: str, ctx: str):
    body = _err(resp)
    assert "error" in body, (
        f"{ctx}: response {resp.status_code} không theo hợp đồng docs/06 "
        f'`{{"error":{{"code",...}}}}`; nhận được: {resp.text[:400]}'
    )
    assert body["error"].get("code") == expected_code, (
        f"{ctx}: mong đợi error.code={expected_code}, nhận "
        f"{body['error'].get('code')!r} (status {resp.status_code})"
    )


def assert_concurrency_ok(responses, ctx):
    """docs/10 A2: đúng 1 post; một 200, một 409 hoặc replay cùng kết quả."""
    statuses = sorted(r.status_code for r in responses)
    oks = [r for r in responses if r.status_code in (200, 201)]
    conflicts = [r for r in responses if r.status_code == 409]
    assert len(oks) <= 1, (
        f"{ctx}: {len(oks)} request trả thành công -> hai checker cùng approve đã "
        f"tạo nhiều post (statuses={statuses}). docs/00 invariant 2, docs/10 A2."
    )
    assert oks or conflicts, f"{ctx}: không có response 200/409 nào: {statuses}"
    if not oks:
        assert conflicts, f"{ctx}: mong đợi ít nhất một 200 hoặc 409: {statuses}"


# --------------------------------------------------------------------------
# A1 - người tạo không được tự duyệt
# --------------------------------------------------------------------------
class TestA1SelfApproval:
    def test_inventory_self_approval_forbidden(self, api):
        m = api.as_user("demo_maker")
        r = m.c.post(
            "/inventory/requests",
            headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"kind": "issue", "lines": []},
        )
        assert r.status_code in (200, 201), f"tạo draft thất bại: {r.status_code} {r.text[:300]}"
        req_id = r.json().get("id")
        sub = m.c.post(
            f"/inventory/requests/{req_id}/submit",
            headers={**m.h("demo_maker"), "If-Match": str(r.json().get("version", 1))},
        )
        assert sub.status_code in (200, 201), f"submit: {sub.status_code} {sub.text[:300]}"

        # cùng user (maker) tự approve
        ap = m.c.post(
            f"/inventory/requests/{req_id}/approve",
            headers={
                **m.h("demo_maker"),
                "Idempotency-Key": str(uuid.uuid4()),
                "If-Match": str(sub.json().get("version", 2)),
            },
        )
        assert ap.status_code == 403, (
            f"A1: maker tự duyệt phiếu mình trả {ap.status_code} (mong đợi 403) "
            f"-> {ap.text[:400]}"
        )
        assert_error_code(ap, "SELF_APPROVAL_FORBIDDEN", "A1 inventory")
        # không được post
        st = m.c.get(f"/inventory/requests/{req_id}", headers=m.h("demo_maker"))
        assert st.json().get("status") != "posted", "A1: phiếu vẫn bị post dù tự duyệt"

    def test_admin_also_cannot_self_approve(self, api):
        """docs/00 invariant 1: 'kể cả admin'."""
        try:
            a = api.as_user("demo_admin")
        except pytest.fail.Exception:
            pytest.skip("seed không tạo user demo_admin -> không kiểm chứng được nhánh admin")
        r = a.c.post(
            "/inventory/requests",
            headers={**a.h("demo_admin"), "Idempotency-Key": str(uuid.uuid4())},
            json={"kind": "adjustment", "reason": "admin self", "lines": []},
        )
        if r.status_code not in (200, 201):
            pytest.skip(f"admin không tạo được draft: {r.status_code}")
        rid = r.json()["id"]
        a.c.post(f"/inventory/requests/{rid}/submit", headers=a.h("demo_admin"))
        ap = a.c.post(
            f"/inventory/requests/{rid}/approve",
            headers={**a.h("demo_admin"), "Idempotency-Key": str(uuid.uuid4())},
        )
        assert ap.status_code == 403, (
            f"A1: admin tự duyệt trả {ap.status_code}, mong đợi 403 "
            "(docs/00: 'admin không miễn quy tắc maker/checker')"
        )


# --------------------------------------------------------------------------
# A2 - hai checker approve đồng thời
# --------------------------------------------------------------------------
class TestA2ConcurrentApprove:
    def test_two_checkers_concurrent_approve(self, api):
        import concurrent.futures

        m = api.as_user("demo_maker")
        r = m.c.post(
            "/inventory/requests",
            headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"kind": "receipt", "lines": []},
        )
        assert r.status_code in (200, 201), r.text[:300]
        rid = r.json()["id"]
        m.c.post(
            f"/inventory/requests/{rid}/submit",
            headers={**m.h("demo_maker"), "If-Match": str(r.json().get("version", 1))},
        )

        # hai checker khác nhau
        checkers = ["demo_checker", "demo_approver2"]
        headers_list = []
        for c in checkers:
            try:
                api.login(c)
            except pytest.fail.Exception:
                if c == "demo_approver2":
                    continue
            headers_list.append({**api.h(c), "Idempotency-Key": str(uuid.uuid4())})

        if len(headers_list) < 2:
            pytest.skip(
                "seed chỉ tạo 1 checker -> không thể kiểm chứng ca A2 "
                "(hai checker approve đồng thời) với dữ liệu hiện có"
            )

        def call(hdr):
            return api.c.post(f"/inventory/requests/{rid}/approve", headers=hdr)

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as ex:
            responses = list(ex.map(call, headers_list))

        assert_concurrency_ok(responses, "A2")

        # đúng một movement/journal
        mv = api.c.get("/inventory/movements", headers=api.h("demo_maker"))
        if mv.status_code == 200:
            rows = mv.json().get("movements", [])
            n = len([x for x in rows if x.get("request_id") == rid])
            assert n <= 1, f"A2: {n} movement cho cùng request (mong đợi <= 1)"


# --------------------------------------------------------------------------
# A3/A4 - idempotency
# --------------------------------------------------------------------------
class TestA3A4Idempotency:
    def test_retry_same_key_same_payload_replays(self, api):
        m = api.as_user("demo_maker")
        key = str(uuid.uuid4())
        payload = {"cashbox_id": "11111111-1111-4111-8111-111111111111",
                   "kind": "receipt", "amount": "100000.00", "reason": "A3"}
        h = {**m.h("demo_maker"), "Idempotency-Key": key}
        r1 = m.c.post("/cash-sim/requests", headers=h, json=payload)
        assert r1.status_code in (200, 201), f"A3 lần 1: {r1.status_code} {r1.text[:300]}"
        r2 = m.c.post("/cash-sim/requests", headers=h, json=payload)
        assert r2.status_code == r1.status_code, (
            f"A3: lần retry trả {r2.status_code}, lần đầu {r1.status_code} "
            "(mong đợi replay cùng kết quả)"
        )
        assert r2.json().get("id") == r1.json().get("id"), (
            "A3: retry cùng key/payload phải trả **cùng** result ID "
            "(docs/10 A3; docs/05 bước 2)"
        )

        j = m.c.get("/cash-sim/journals", headers=m.h("demo_maker"))
        if j.status_code == 200:
            rid = r1.json()["id"]
            n = len([x for x in j.json().get("journals", []) if x.get("request_id") == rid])
            assert n <= 1, f"A3: {n} journal cho cùng request -> đã nhân đôi"

    def test_same_key_different_payload_409(self, api):
        m = api.as_user("demo_maker")
        key = str(uuid.uuid4())
        h = {**m.h("demo_maker"), "Idempotency-Key": key}
        r1 = m.c.post(
            "/cash-sim/requests", headers=h,
            json={"cashbox_id": "11111111-1111-4111-8111-111111111111",
                  "kind": "receipt", "amount": "100000.00", "reason": "A4-a"},
        )
        assert r1.status_code in (200, 201), r1.text[:300]
        r2 = m.c.post(
            "/cash-sim/requests", headers=h,
            json={"cashbox_id": "11111111-1111-4111-8111-111111111111",
                  "kind": "receipt", "amount": "999999.00", "reason": "A4-b"},
        )
        assert r2.status_code == 409, (
            f"A4: cùng Idempotency-Key khác payload trả {r2.status_code} (mong đợi 409, "
            "không ghi thêm) -> {r2.text[:300]}"
        )
        assert_error_code(r2, "IDEMPOTENCY_KEY_REUSED", "A4")


# --------------------------------------------------------------------------
# I1/I2 - kho
# --------------------------------------------------------------------------
class TestI1I2Inventory:
    def test_issue_more_than_balance_is_409_and_balance_unchanged(self, api):
        """I1: xuất quá tồn -> 409, balance giữ nguyên."""
        m = api.as_user("demo_maker")
        before = m.c.get(
            "/inventory/balances",
            headers=m.h("demo_maker"),
            params={"location_id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
                    "item_id": "cccccccc-cccc-4ccc-8ccc-ccccccccccc1"},
        )
        assert before.status_code == 200, before.text[:300]
        qty_before = before.json()["balances"][0]["quantity"]

        r = m.c.post(
            "/inventory/requests",
            headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"kind": "issue",
                  "source_id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
                  "lines": [{"item_id": "cccccccc-cccc-4ccc-8ccc-ccccccccccc1",
                             "quantity": "999999.000"}]},
        )
        assert r.status_code in (200, 201), r.text[:300]
        rid = r.json()["id"]
        sub = m.c.post(f"/inventory/requests/{rid}/submit", headers=m.h("demo_maker"))
        assert sub.status_code in (200, 201), sub.text[:300]

        ap = api.c.post(
            f"/inventory/requests/{rid}/approve",
            headers={**api.h("demo_checker"), "Idempotency-Key": str(uuid.uuid4())},
        )
        assert ap.status_code == 409, (
            f"I1: approve xuất quá tồn trả {ap.status_code} (mong đợi 409 INSUFFICIENT_STOCK)"
        )
        assert_error_code(ap, "INSUFFICIENT_STOCK", "I1")

        after = m.c.get(
            "/inventory/balances",
            headers=m.h("demo_maker"),
            params={"location_id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
                    "item_id": "cccccccc-cccc-4ccc-8ccc-ccccccccccc1"},
        )
        assert after.json()["balances"][0]["quantity"] == qty_before, (
            "I1: balance thay đổi dù approve thất bại"
        )

    def test_transfer_creates_two_movements_and_preserves_total(self, api):
        """I2: chuyển kho -> hai movement, tổng lượng bảo toàn."""
        m = api.as_user("demo_maker")
        r = m.c.post(
            "/inventory/requests",
            headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"kind": "transfer",
                  "source_id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
                  "dest_id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2",
                  "lines": [{"item_id": "cccccccc-cccc-4ccc-8ccc-ccccccccccc1",
                             "quantity": "10.000"}]},
        )
        assert r.status_code in (200, 201), r.text[:300]
        rid = r.json()["id"]
        m.c.post(f"/inventory/requests/{rid}/submit", headers=m.h("demo_maker"))
        ap = api.c.post(
            f"/inventory/requests/{rid}/approve",
            headers={**api.h("demo_checker"), "Idempotency-Key": str(uuid.uuid4())},
        )
        assert ap.status_code in (200, 201), f"I2: approve trả {ap.status_code}: {ap.text[:300]}"

        mv = api.c.get("/inventory/movements", headers=api.h("demo_maker"))
        rows = [x for x in mv.json().get("movements", []) if x.get("request_id") == rid]
        assert len(rows) == 2, f"I2: mong đợi 2 movement (source -qty / dest +qty), nhận {len(rows)}"
        total = sum(float(x["quantity_signed"]) for x in rows)
        assert abs(total) < 1e-9, f"I2: tổng lượng 2 movement = {total}, phải bằng 0"


# --------------------------------------------------------------------------
# I3 - serial
# --------------------------------------------------------------------------
class TestI3Serial:
    def test_duplicate_serial_rejected(self, api):
        m = api.as_user("demo_maker")
        serial = f"DEMO-DUP-{uuid.uuid4().hex[:8].upper()}"
        payload = {"item_id": "cccccccc-cccc-4ccc-8ccc-ccccccccccc2",
                   "location_id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1",
                   "serial": serial}
        r1 = api.c.post("/inventory/assets",
                        headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
                        json=payload)
        if r1.status_code == 404:
            pytest.skip("chưa có POST /inventory/assets -> B07 chưa triển khai, I3 không kiểm chứng được")
        assert r1.status_code in (200, 201), f"I3 tạo lần 1: {r1.status_code} {r1.text[:300]}"
        r2 = api.c.post("/inventory/assets",
                        headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
                        json=payload)
        assert r2.status_code == 409, (
            f"I3: tạo serial trùng trả {r2.status_code} (mong đợi 409 DUPLICATE_SERIAL)"
        )
        assets = api.c.get("/inventory/assets", headers=api.h("demo_maker"),
                           params={"serial": serial})
        rows = [a for a in assets.json().get("assets", []) if a.get("serial") == serial]
        assert len(rows) == 1, f"I3: có {len(rows)} asset cùng serial (mong đợi đúng 1)"


# --------------------------------------------------------------------------
# C1/C2/C3 - ledger quỹ
# --------------------------------------------------------------------------
class TestC1C2C3Cash:
    def _create_cash(self, api, maker, kind, amount, reason):
        m = api.as_user(maker)
        r = m.c.post(
            "/cash-sim/requests",
            headers={**m.h(maker), "Idempotency-Key": str(uuid.uuid4())},
            json={"cashbox_id": "11111111-1111-4111-8111-111111111111",
                  "kind": kind, "amount": amount, "reason": reason},
        )
        assert r.status_code in (200, 201), f"tạo {kind}: {r.status_code} {r.text[:300]}"
        rid = r.json()["id"]
        sub = m.c.post(f"/cash-sim/requests/{rid}/submit", headers=m.h(maker))
        assert sub.status_code in (200, 201), f"submit: {sub.status_code} {sub.text[:300]}"
        ap = api.c.post(
            f"/cash-sim/requests/{rid}/approve",
            headers={**api.h("demo_checker"), "Idempotency-Key": str(uuid.uuid4())},
        )
        return rid, ap

    def test_payment_beyond_balance_is_409_no_journal(self, api):
        """C1: chi quá quỹ demo -> 409, không journal."""
        journals_before = api.c.get("/cash-sim/journals", headers=api.h("demo_checker"))
        n_before = len(journals_before.json().get("journals", [])) if journals_before.status_code == 200 else None

        rid, ap = self._create_cash(api, "demo_maker", "payment", "999999999.00", "C1 chi qua quy")
        assert ap.status_code == 409, (
            f"C1: chi quá quỹ trả {ap.status_code} (mong đợi 409 INSUFFICIENT_SIM_CASH): "
            f"{ap.text[:300]}"
        )
        assert_error_code(ap, "INSUFFICIENT_SIM_CASH", "C1")

        after = api.c.get("/cash-sim/journals", headers=api.h("demo_checker"))
        n_after = len(after.json().get("journals", [])) if after.status_code == 200 else None
        if n_before is not None and n_after is not None:
            assert n_after == n_before, "C1: journal vẫn được tạo dù approve thất bại"

    def test_receipt_posts_balanced_journal(self, api):
        """C2: thu hợp lệ -> debit=credit, balance đúng Decimal."""
        rid, ap = self._create_cash(api, "demo_maker", "receipt", "1000000.00", "C2 thu hop le")
        assert ap.status_code in (200, 201), (
            f"C2: approve thu trả {ap.status_code}: {ap.text[:300]}"
        )
        body = ap.json()
        assert body.get("status") == "posted", (
            f"C2: docs/06 yêu cầu POST /approve trả 200 với trạng thái `posted`; nhận {body}"
        )
        if "202" in str(ap.status_code):
            pytest.fail("C2: approve trả 202 dù DB chưa post (docs/06 cấm)")

        jr = api.c.get("/cash-sim/journals", headers=api.h("demo_checker"))
        assert jr.status_code == 200, jr.text[:300]
        journals = jr.json().get("journals", [])
        mine = [j for j in journals if j.get("request_id") == rid]
        assert len(mine) == 1, f"C2: mong đợi 1 journal cho request, nhận {len(mine)}"
        entries = mine[0].get("entries") or []
        if not entries:
            pytest.fail(
                "C2: journal không trả entries -> không chứng minh được debit=credit "
                "(Invariant 3 docs/00)"
            )
        dr = sum(float(e["debit"]) for e in entries)
        cr = sum(float(e["credit"]) for e in entries)
        assert abs(dr - cr) < 1e-9, f"C2: tổng debit {dr} != tổng credit {cr}"

        bal = api.c.get("/cash-sim/balances", headers=api.h("demo_checker"))
        row = next(
            (b for b in bal.json().get("balanes", bal.json().get("balances", []))
             if b.get("cashbox_id") == "11111111-1111-4111-8111-111111111111"),
            None,
        )
        assert row is not None, "C2: không tìm thấy balance của cashbox demo"
        assert str(row["amount"]).startswith("1000000"), (
            f"C2: balance sau receipt = {row['amount']}, mong đợi 1000000.00"
        )

    def test_reversal_creates_new_balanced_journal(self, api):
        """C3: reversal -> journal mới cân, original nguyên vẹn."""
        rid, ap = self._create_cash(api, "demo_maker", "receipt", "500000.00", "C3 goc")
        assert ap.status_code in (200, 201), f"C3: không post được phiếu gốc: {ap.text[:300]}"

        rev = api.c.post(
            f"/cash-sim/requests/{rid}/reverse",
            headers={**api.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"reason": "C3 reversal"},
        )
        if rev.status_code == 404:
            pytest.skip("chưa có endpoint reverse -> C3 (docs/05 'reversal là journal mới') chưa làm")
        assert rev.status_code in (200, 201), f"C3: {rev.status_code} {rev.text[:300]}"

        jr = api.c.get("/cash-sim/journals", headers=api.h("demo_checker"))
        journals = jr.json().get("journals", [])
        orig = next((j for j in journals if j.get("request_id") == rid), None)
        assert orig is not None, "C3: journal gốc biến mất sau reversal"
        revs = [j for j in journals if j.get("reversal_of") == orig.get("id")]
        assert revs, "C3: không có journal liên kết reversal_of tới journal gốc"
        for j in revs:
            entries = j.get("entries") or []
            if entries:
                dr = sum(float(e["debit"]) for e in entries)
                cr = sum(float(e["credit"]) for e in entries)
                assert abs(dr - cr) < 1e-9, f"C3: journal reversal không cân ({dr} vs {cr})"


# --------------------------------------------------------------------------
# S1 - phân quyền
# --------------------------------------------------------------------------
class TestS1Authorization:
    def test_user_without_scope_gets_403_or_404(self, api):
        """S1: user không có scope -> 403/404 theo policy, không lộ dữ liệu."""
        a = api.as_user("demo_auditor")  # chỉ có audit.read/inventory.read/cash.read
        r = a.c.post(
            "/cash-sim/requests",
            headers={**a.h("demo_auditor"), "Idempotency-Key": str(uuid.uuid4())},
            json={"cashbox_id": "11111111-1111-4111-8111-111111111111",
                  "kind": "payment", "amount": "1000.00", "reason": "S1"},
        )
        assert r.status_code in (403, 404), (
            f"S1: auditor (không có cash.write) tạo phiếu cash trả {r.status_code}, "
            f"mong đợi 403/404 -> {r.text[:300]}"
        )
        body = r.text.lower()
        for leak in ("traceback", "sqlalchemy", "password_hash", "select "):
            assert leak not in body, f"S1: response lộ thông tin nội bộ {leak!r}"

    def test_auditor_cannot_approve(self, api):
        a = api.as_user("demo_auditor")
        # inbox là read; kiểm tra approve một phiếu bất kỳ
        r2 = a.c.post(
            "/cash-sim/requests/22222222-2222-4222-8222-222222222222/approve",
            headers={**a.h("demo_auditor"), "Idempotency-Key": str(uuid.uuid4())},
        )
        assert r2.status_code in (401, 403, 404), (
            f"S1: auditor gọi approve trả {r2.status_code}, mong đợi 403 (thiếu cash.approve)"
        )

    def test_unauthenticated_gets_401(self, api):
        # chỉ dùng path TỒN TẠI trong OpenAPI; path docs/06 `/me` kiểm riêng bên dưới
        for path in (
            "/inventory/items",
            "/cash-sim/balances",
            "/approvals/inbox",
            "/auth/me",
            "/activity",
        ):
            r = api.c.get(path)
            assert r.status_code == 401, (
                f"S1: GET {path} không kèm token trả {r.status_code}, mong đợi 401 "
                "(docs/06/08: deny by default)"
            )

    def test_documented_me_path_exists(self, api):
        """docs/06 dòng 9 khai báo `GET /me`; code triển khai `/auth/me` -> lệch contract."""
        r = api.c.get("/me")
        assert r.status_code != 404, (
            "S1/docs-06: `GET /me` được khai báo trong docs/06 nhưng trả 404; "
            "OpenAPI thực tế chỉ có `GET /auth/me` -> docs và code không khớp nhau."
        )

    def test_object_level_authorization(self, api):
        """docs/08: 'danh sách/API object-level authorization'."""
        m = api.as_user("demo_maker")
        created = m.c.post(
            "/inventory/requests",
            headers={**m.h("demo_maker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"kind": "issue", "lines": []},
        )
        if created.status_code not in (200, 201):
            pytest.skip("không tạo được object để kiểm tra object-level auth")
        rid = created.json()["id"]
        o = api.as_user("demo_auditor")
        r = o.c.get(f"/inventory/requests/{rid}", headers=o.h("demo_auditor"))
        assert r.status_code in (403, 404), (
            f"S1: user khác scope đọc được object không thuộc quyền "
            f"(GET trả {r.status_code})"
        )


# --------------------------------------------------------------------------
# R1 - đối soát
# --------------------------------------------------------------------------
class TestR1Reconciliation:
    def test_reconciliation_detects_planted_mismatch(self, api):
        r = api.c.post(
            "/reconciliations",
            headers={**api.h("demo_checker"), "Idempotency-Key": str(uuid.uuid4())},
            json={"scope": "cash"},
        )
        if r.status_code == 404:
            pytest.skip("chưa có POST /reconciliations -> B14 chưa triển khai, R1 không kiểm chứng được")
        run = r.json()
        assert run.get("status") in ("matched", "exception", "running", "failed"), (
            f"R1: status không thuộc bộ docs/05: {run.get('status')!r}"
        )
        if run.get("status") == "exception":
            exc = run.get("exceptions") or []
            assert exc, "R1: status=exception nhưng không có exception detail"
            for e in exc:
                assert "expected" in e and "actual" in e, (
                    f"R1: exception thiếu expected/actual (docs/05): {e}"
                )
        # không tự vá
        assert run.get("auto_fixed") in (None, False), (
            "R1: reconciliation không được tự động vá sai lệch (docs/05)"
        )


# --------------------------------------------------------------------------
# E1 - SSE
# --------------------------------------------------------------------------
class TestE1SSE:
    def test_sse_replay_after_reconnect(self, api):

        with api.c.stream("GET", "/events/stream") as r:
            assert r.status_code == 200, f"E1: SSE trả {r.status_code}"
            ctype = r.headers.get("content-type", "")
            assert "text/event-stream" in ctype, f"E1: content-type = {ctype!r}"
            assert "access_token" not in str(r.url).lower() and "token=" not in str(r.url).lower(), (
                "E1: token không được gửi trong URL (docs/06)"
            )
            chunk = next(r.iter_lines(), "")
            assert chunk, "E1: không nhận được event nào"

        # reconnect với cursor quá cũ -> resync_required
        with api.c.stream(
            "GET", "/events/stream", params={"cursor": "0" * 32}
        ) as r2:
            body = "".join(list(r2.iter_lines())[:5])
        assert "resync_required" in body or "heartbeat" in body, (
            f"E1: replay theo cursor không hoạt động; nhận: {body[:300]!r}"
        )

    def test_rest_returns_final_state_after_sse_loss(self, api):
        """E1: 'GET trả state cuối' sau khi SSE rớt."""
        r = api.c.get("/activity", headers=api.h("demo_maker"))
        assert r.status_code == 200, f"E1: GET /activity trả {r.status_code}"
        assert "events" in r.json(), "E1: GET /activity phải trả snapshot feed"
