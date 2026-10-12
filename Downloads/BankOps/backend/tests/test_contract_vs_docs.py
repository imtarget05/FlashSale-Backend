"""Đối chiếu hợp đồng: docs/06-api.md + docs/04-database.md + docs/08-security.md vs mã nguồn.

Những test này **không cần app boot** (kiểm tĩnh trên nguồn), nên luôn chạy được
và chỉ ra chính xác phần nào của hợp đồng chưa được triển khai.
"""
from __future__ import annotations

import re

import pytest
from conftest import BACKEND_DIR, REPO_DIR

ROUTES_DIR = BACKEND_DIR / "app" / "api" / "routes"
MODELS_DIR = BACKEND_DIR / "app" / "models"


def _route_table() -> list[tuple[str, str, str]]:
    """[(method, path, file)] từ tất cả @router.<method>("path")."""
    out: list[tuple[str, str, str]] = []
    for f in sorted(ROUTES_DIR.glob("*.py")):
        for m in re.finditer(
            r'@router\.(get|post|put|patch|delete)\(\s*["\']([^"\']+)["\']', f.read_text()
        ):
            out.append((m.group(1).upper(), m.group(2), f.name))
    return out


def _prefix_for(filename: str) -> str:
    return {
        "auth.py": "/auth",
        "inventory.py": "/inventory",
        "cash_sim.py": "/cash-sim",
        "approvals.py": "/approvals",
        "activity.py": "/activity",
        "health.py": "/health",
    }.get(filename, "")


# --------------------------------------------------------------- B03: auth --
class TestAuthRBAC:
    def test_auth_utils_module_is_valid_python(self):
        import py_compile

        from conftest import BACKEND_DIR as B

        try:
            py_compile.compile(str(B / "app" / "api" / "routes" / "auth_utils.py"), doraise=True)
        except py_compile.PyCompileError as exc:
            pytest.fail(
                "app/api/routes/auth_utils.py không biên dịch được "
                f"(IndentationError) -> auth, seed và mọi route đều không import được.\n{exc}",
                pytrace=False,
            )

    def test_get_db_must_be_a_bare_async_generator(self):
        """Lỗi đã xác minh: `Depends(get_db)` 500 toàn bộ nếu `get_db` bị bọc
        `@asynccontextmanager`.

        FastAPI (`fastapi/dependencies/models.py::_is_async_gen_callable`) unwrap
        `__wrapped__` nên vẫn xếp `get_db` vào loại async-gen, rồi
        `fastapi/dependencies/utils.py::_solve_generator` bọc thêm
        `asynccontextmanager()` lần thứ hai -> `anext(<_AsyncGeneratorContextManager>)`
        -> `TypeError: '_AsyncGeneratorContextManager' object is not an async iterator`.

        Chứng minh thực nghiệm (không sửa mã nguồn):
          - `Depends(get_db)`               -> TypeError
          - `Depends(get_db.__wrapped__)`   -> 200 + AsyncSession
        """
        import inspect

        from fastapi import Depends, FastAPI
        from fastapi.testclient import TestClient
        from sqlalchemy.ext.asyncio import AsyncSession

        import app.database as db

        if inspect.isasyncgenfunction(db.get_db):
            return  # đúng chuẩn FastAPI

        mini = FastAPI()

        @mini.get("/probe")
        async def probe(s: AsyncSession = Depends(db.get_db)):  # noqa: B008 - idiomatic FastAPI
            return {"ok": True}

        with TestClient(mini, raise_server_exceptions=True) as c:
            try:
                c.get("/probe")
            except TypeError as exc:
                pytest.fail(
                    "app/database.py:get_db đang được bọc `@asynccontextmanager` nhưng "
                    "dùng với `Depends()` -> FastAPI bọc lần 2 -> TypeError.\n"
                    f"  {type(exc).__name__}: {exc}\n"
                    "  Ảnh hưởng: POST /auth/login và GET /auth/me trả 500; không "
                    "đăng nhập được, toàn bộ ca A1-A4, I1-I3, C1-C3, S1, R1, E1 rơi theo.\n"
                    "  Sửa: bỏ decorator `@asynccontextmanager` khỏi `get_db`, giữ "
                    "`async def get_db() -> AsyncSession:` + `yield session`.",
                    pytrace=False,
                )
            except Exception:  # noqa: BLE001 - chạy được thì đã không phải lỗi này
                pass

    def test_login_token_subject_is_json_serializable(self):
        """`create_access_token` phải chịu được `sub` là UUID.

        `auth.py:44` truyền `user.id` (UUID từ DB) vào `sub`, nhưng
        `jwt.encode` json-hóa payload -> `TypeError: Object of type UUID is not
        JSON serializable` (đã xác minh live). Đây là **blocker login thứ hai
        sau bcrypt**: sửa bcrypt xong mà không `str(user.id)` thì login vẫn 500.
        """
        import uuid

        from conftest import _import_or_fail

        auth = _import_or_fail("app.api.routes.auth_utils")
        try:
            token = auth.create_access_token(
                {"sub": uuid.uuid4(), "username": "demo_maker"}
            )
        except TypeError as exc:
            pytest.fail(
                f"create_access_token ném TypeError với sub=UUID: {exc}.\n"
                f"  `auth.py` login truyền `user.id` (UUID) -> login không bao giờ "
                f"trả token được. Sửa: `str(user.id)` khi tạo claim `sub` (cả "
                f"access + refresh), hoặc ép str trong `create_access_token`.",
                pytrace=False,
            )
        if not isinstance(token, str) or not token:
            pytest.fail("create_access_token không trả về JWT string", pytrace=False)

    def test_get_current_user_rejects_refresh_tokens(self):
        """Access-only endpoint phải từ chối refresh token (phân biệt `type`).

        `create_refresh_token` gắn `type=refresh`, nhưng `get_current_user`
        không kiểm `payload.get("type")` -> refresh token dùng được như access
        token ở mọi endpoint (token-type confusion), vòng đời 7 ngày thay vì
        30 phút.
        """
        import re

        src = (BACKEND_DIR / "app" / "api" / "routes" / "auth_utils.py").read_text()
        body = src[src.index("async def get_current_user("):src.index(
            "async def get_current_user_with_capabilities("
        )]
        if not re.search(r'payload\.get\(["\']type["\']\)', body):
            pytest.fail(
                "`get_current_user` không kiểm claim `type` của JWT -> refresh "
                "token (sống 7 ngày) dùng được ở mọi chỗ cần access token "
                "(30 phút). Sửa: từ chối token không có `type == 'access'`.",
                pytrace=False,
            )

    def test_login_with_wrong_password_returns_401_not_500(self, client):
        """Sai credentials phải 401 + envelope, không bao giờ 500.

        Hiện tại: user không tồn tại -> 401 đúng; user có thật + sai mật khẩu
        -> `verify_password` ném ValueError (P0 #6) thoát ra ngoài thành
        `500 text/plain`. Kể cả sau khi sửa bcrypt, invariant này phải giữ:
        lỗi credentials luôn là 401 envelope docs/06:44, không lộ 500.
        """
        cases = [
            ("ghost_user_khong_ton_tai", "sai"),
            ("demo_maker", "SaiMatKhau123!"),  # user do seed tạo (sample-data)
        ]
        for username, password in cases:
            resp = client.post(
                "/auth/login", data={"username": username, "password": password}
            )
            if resp.status_code == 500:
                pytest.fail(
                    f"POST /auth/login ({username}/***) trả 500 thay vì 401 — "
                    f"exception lọt ra ngoài (hôm nay: ValueError từ bcrypt, P0 #6).\n"
                    f"  Sửa: bọc `authenticate_user`/`verify_password`, mọi lỗi "
                    f"credentials -> 401 envelope docs/06:44, không 500.",
                    pytrace=False,
                )
            if resp.status_code != 401:
                pytest.fail(
                    f"POST /auth/login ({username}/***) trả {resp.status_code}, "
                    f"mong đợi 401. Body: {resp.text[:120]!r}",
                    pytrace=False,
                )

    def test_session_middleware_is_used_or_removed(self):
        """`SessionMiddleware` đã mount thì phải được dùng, không thì gỡ.

        `app/main.py:36` mount `SessionMiddleware(secret_key=settings.session_secret)`
        với secret default công khai, nhưng không route nào đọc/ghi
        `request.session` -> middleware chết, chỉ mở thêm bề mặt tấn công
        (cookie có ký, secret ai cũng biết) mà không có chức năng.
        Sửa: hoặc dùng session thật (vd lưu pós-login state theo docs/06 cookie
        web), hoặc gỡ middleware + `session_secret` khỏi config.
        """
        main_src = (BACKEND_DIR / "app" / "main.py").read_text()
        if "SessionMiddleware" not in main_src:
            return  # đã gỡ -> xong
        used = [
            f for f in (BACKEND_DIR / "app").rglob("*.py")
            if "request.session" in f.read_text() or "session[" in f.read_text()
        ]
        if not used:
            pytest.fail(
                "`SessionMiddleware` được mount nhưng không route nào dùng session "
                "-> bề mặt tấn công thừa với secret default công khai. Gỡ middleware "
                "hoặc dùng session thật.",
                pytrace=False,
            )

    def test_auth_secrets_are_not_known_defaults(self):
        """JWT signing key và session secret không được là giá trị công khai đã biết trong mã nguồn.

        Thực tế (`app/config.py`): `signing_key` trả `""` ở demo khi không set
        env (JWT ký bằng key rỗng -> ai biết quy ước cũng forge được token demo
        đang deploy); `session_secret` default hằng publicly-known
        `dev_only_session_secret_change_in_prod` (session cookie forgeable).
        Test cũ `test_no_hardcoded_signing_secret_fallback` grep sai chuỗi
        (tìm `dev_default_signing_key` trong khi code là `dev_signing_key`,
        tìm `dev-session-secret-...` gạch ngang trong khi code là gạch dưới)
        nên xanh giả — test này kiểm giá trị runtime thật.
        """
        from conftest import _import_or_fail

        cfg = _import_or_fail("app.config")
        problems = []
        if not cfg.settings.signing_key:
            problems.append(
                "`settings.signing_key` rỗng ở demo (JWT ký bằng key rỗng) — "
                "token forgeable. Sửa: bắt buộc env `BANKOPS_dev_signing_key` "
                "hoặc sinh key ngẫu nhiên mỗi lần boot (demo).",
            )
        if cfg.settings.session_secret == "dev_only_session_secret_change_in_prod":
            problems.append(
                "`settings.session_secret` đang là default publicly-known trong "
                "mã nguồn — session cookie forgeable. Sửa: bắt buộc "
                "`BANKOPS_SESSION_SECRET` ở mọi môi trường.",
            )
        if problems:
            pytest.fail("\n".join(problems), pytrace=False)

    def test_logout_uses_defined_symbol(self):
        """auth.py tham chiếu `oauth2_scheme` trong Depends() mà không import nó."""
        src = (ROUTES_DIR / "auth.py").read_text()
        if "oauth2_scheme" not in src:
            pytest.skip("auth.py không dùng oauth2_scheme")
        imported = re.search(
            r"from app\.api\.routes\.auth_utils import\s*\(([^)]*)\)", src, re.S
        )
        single = re.search(
            r"from app\.api\.routes\.auth_utils import ([^\n(]+)", src
        )
        names = (imported.group(1) if imported else "") + (
            single.group(1) if single else ""
        )
        if "oauth2_scheme" not in names and not re.search(
            r"^\s*oauth2_scheme\s*=", src, re.M
        ):
            pytest.fail(
                "app/api/routes/auth.py dùng `Depends(oauth2_scheme)` nhưng không import/"
                "định nghĩa `oauth2_scheme` -> NameError khi import router, app không boot.",
                pytrace=False,
            )

    def test_no_hardcoded_signing_secret_fallback(self):
        """docs/08: không đưa secret ra log/URL; token không được ký bằng secret mặc định."""
        cfg = (BACKEND_DIR / "app" / "config.py").read_text()
        # NOTE (vòng 5): secret đã bị đổi tên + đổi quote nên 2 grep dưới từng
        # xanh giả; invariant thật do `test_auth_secrets_are_not_known_defaults`
        # kiểm runtime. Giữ grep mở rộng để bắt mọi biến thể hằng secret.
        if "dev_default_signing_key" in cfg or "dev_signing_key" in cfg:
            pytest.fail(
                "app/config.py `signing_key` fallback về hằng `dev_default_signing_key` "
                "khi env không set -> ai cũng ký được JWT hợp lệ (chữ ký forgeable). "
                "docs/08 yêu cầu access token ngắn hạn và secret ngoài mã nguồn.",
                pytrace=False,
            )
        if '"dev-session-secret-change-in-prod"' in cfg:
            pytest.fail(
                "app/config.py `session_secret` mặc định hằng publicly-known "
                "`dev-session-secret-change-in-prod` -> session cookie forgeable.",
                pytrace=False,
            )

    def test_cors_wildcard_with_credentials_is_rejected(self):
        """docs/08: 'security headers/CORS cụ thể', không wildcard + credentials."""
        compose = (REPO_DIR / "docker-compose.yml").read_text()
        main = (BACKEND_DIR / "app" / "main.py").read_text()
        wildcard = "ALLOWED_ORIGINS=*" in compose or 'allow_origins=["*"]' in main
        if wildcard and "allow_credentials=True" in main:
            pytest.fail(
                "docker-compose set `ALLOWED_ORIGINS=*` trong khi `main.py` bật "
                "`allow_credentials=True` -> CORS sai chuẩn (credentialed request với "
                "Origin `*` bị trình duyệt từ chối) và mở rộng nguồn. docs/08: "
                "'security headers/CORS cụ thể'.",
                pytrace=False,
            )

    def test_rate_limit_login_is_actually_enforced(self):
        cfg = (BACKEND_DIR / "app" / "config.py").read_text()
        auth = (ROUTES_DIR / "auth.py").read_text()
        if "rate_limit_login_attempts" in cfg and "rate_limit" not in auth.lower():
            pytest.fail(
                "config khai báo `rate_limit_login_attempts/window` nhưng "
                "POST /auth/login không áp dụng rate limit -> brute force "
                "không bị chặn (docs/08 yêu cầu rate limit login + khóa tạm).",
                pytrace=False,
            )

    def test_refresh_endpoint_and_rotation_exist(self):
        """docs/06: POST /auth/refresh; docs/08: refresh rotation và revoke."""
        if not any(
            m == "POST" and p.rstrip("/").endswith("/refresh")
            for m, p, _ in _route_table()
        ):
            pytest.fail(
                "Thiếu `POST /auth/refresh` (docs/06) và không có refresh rotation/"
                "revocation (docs/08) -> phiên demo không thể thu hồi sau logout.",
                pytrace=False,
            )

    def test_logout_actually_revokes(self):
        src = (ROUTES_DIR / "auth.py").read_text()
        logout_body = src.split("@router.post(\"/logout\")", 1)[-1].split("@router", 1)[0]
        if "revoke" not in logout_body and "delete" not in logout_body and "denylist" not in logout_body:
            pytest.fail(
                "POST /auth/logout chỉ trả message, không thu hồi token nào "
                "(docs/08: 'refresh rotation và revoke/logout'). Token vẫn hợp lệ sau logout.",
                pytrace=False,
            )

    def test_capability_checks_are_enforced_on_protected_routes(self):
        """`require_capability` được định nghĩa nhưng có route nào dùng không?"""
        # chỉ đếm chỗ gọi, không tính dòng import
        callers = []
        for f in ROUTES_DIR.glob("*.py"):
            for i, line in enumerate(f.read_text().splitlines(), 1):
                if "require_capability(" in line and "import" not in line and "def " not in line:
                    callers.append(f"{f.name}:{i}")
        if not callers:
            pytest.fail(
                "Không route nào gọi `require_capability(...)` -> **không có RBAC nào "
                "được áp dụng**. Mọi endpoint chỉ cần đăng nhập là qua. "
                "docs/08: 'deny by default'; docs/06 gán permission cho từng endpoint; "
                "ca S1 (403 theo policy) không thể đạt.",
                pytrace=False,
            )

    def test_approvals_inbox_requires_approve_capability(self):
        """docs/06: GET /approvals/inbox permission `*.approve`."""
        src = (ROUTES_DIR / "approvals.py").read_text()
        if "require_capability" not in src and "approve" not in src:
            pytest.fail(
                "GET /approvals/inbox không kiểm tra capability `*.approve` "
                "(mọi user đã đăng nhập đều gọi được) -> vi phạm docs/06 và ca S1.",
                pytrace=False,
            )


class TestPasswordCrypto:
    """passlib 1.7.4 không đọc được bcrypt >= 4.1 (đã xác minh với bcrypt 5.0.0)."""

    def test_get_password_hash_and_verify_roundtrip(self):
        """Hash + verify phải chạy được với mọi mật khẩu.

        Đã xác minh thực nghiệm (cả Python 3.11 + bcrypt 5.0.0 lẫn 3.14):
        `passlib.handlers.bcrypt` đọc `bcrypt.__about__.__version__` (bị xoá ở
        bcrypt >= 4.1) -> version detection hỏng -> mọi lượt hash/verify ném
        `ValueError: password cannot be longer than 72 bytes` kể cả mật khẩu 3 ký tự.

        Ảnh hưởng: `seed_demo` không tạo được user, `auth_utils.py:67` không đăng
        nhập được -> toàn bộ ca A1-A4/I1-I3/C1-C3/S1/E1 rơi theo (kể cả sau khi
        sửa bug get_db).
        """
        from conftest import _import_or_fail

        auth = _import_or_fail("app.api.routes.auth_utils")

        try:
            hashed = auth.get_password_hash("DemoPass123!")
        except Exception as exc:  # noqa: BLE001 - mọi loại exception đều là lỗi
            pytest.fail(
                "app.api.routes.auth_utils.get_password_hash ném exception "
                f"({type(exc).__name__}: {exc}).\n"
                "  Nguyên nhân đã xác minh: passlib 1.7.4 không tương thích "
                "bcrypt >= 4.1 (đang là 5.0.0) -> version detection qua "
                "`bcrypt.__about__` thất bại.\n"
                "  Sửa: pin `bcrypt = \"<4.1\"` trong pyproject (hoặc bỏ passlib, "
                "dùng thẳng `bcrypt.hashpw`).\n"
                "  Lưu ý: `backend/poetry.lock` đang không tồn tại nên lần cài "
                "sẽ mãi lấy bcrypt mới nhất.",
                pytrace=False,
            )

        if not str(hashed).startswith(("$2b$", "$2a$", "$2y$")):
            pytest.fail(f"hash không đúng định dạng bcrypt: {hashed!r}", pytrace=False)

        try:
            ok = auth.verify_password("DemoPass123!", hashed)
            bad = auth.verify_password("sai_mat_khau", hashed)
        except Exception as exc:  # noqa: BLE001
            pytest.fail(
                f"auth_utils.verify_password ném {type(exc).__name__}: {exc} "
                "-> đăng nhập không bao giờ thành công.",
                pytrace=False,
            )

        assert ok is True, "verify_password trả False với đúng mật khẩu"
        assert bad is False, "verify_password trả True với sai mật khẩu"

# ------------------------------------------------- B05-B09: API surface ----
class TestApiSurfaceVsDocs:
    """Liệt kê endpoint thực tế so với bảng endpoint trong docs/06-api.md."""

    REQUIRED_WRITES = [
        ("POST", "/inventory/requests"),
        ("POST", "/inventory/requests/{id}/submit"),
        ("POST", "/inventory/requests/{id}/approve"),
        ("POST", "/inventory/requests/{id}/reject"),
        ("POST", "/cash-sim/requests"),
        ("POST", "/cash-sim/requests/{id}/submit"),
        ("POST", "/cash-sim/requests/{id}/approve"),
        ("POST", "/cash-sim/requests/{id}/reject"),
    ]

    def test_required_write_endpoints_exist(self):
        have = {(m, p) for m, p, _ in _route_table()}
        # FastAPI path param tên khác nhau được chấp nhận
        missing = []
        for m, p in self.REQUIRED_WRITES:
            if (m, p) in have:
                continue
            generic = p.replace("{id}", "*")
            if not any(mm == m and pp.replace("{request_id}", "*") == generic for mm, pp in have):
                missing.append(f"{m} {p}")
        if missing:
            pytest.fail(
                "Thiếu endpoint write bắt buộc theo docs/06 (B06/B08 chưa làm):\n  - "
                + "\n  - ".join(missing)
                + f"\n\nEndpoint hiện có: {sorted(have)}",
                pytrace=False,
            )

    def test_no_write_endpoint_is_a_stub(self):
        """Endpoint write phải có logic nghiệp vụ, không trả object rỗng."""
        found = 0
        for f in sorted(ROUTES_DIR.glob("*.py")):
            src = f.read_text()
            for m in re.finditer(
                r'@router\.(post|put|patch|delete)\(\s*["\']([^"\']+)["\']\s*\)(.*?)(?=\n@router|\Z)',
                src,
                re.S,
            ):
                found += 1
                body = m.group(3)
                if re.search(r"return\s*\{\s*\}", body) or re.search(
                    r"return\s*\{\s*\"[a-z_]+\":\s*\[\]\s*\}", body
                ):
                    pytest.fail(f"{f.name} {m.group(1).upper()} {m.group(2)} là stub (trả rỗng).")
        # tránh "xanh giả": không có endpoint write nào thì vòng lặp trên không bao giờ chạy
        if found == 0:
            pytest.fail(
                "Không có endpoint write (POST/PUT/PATCH/DELETE) nào trong app/api/routes/ "
                "-> `test_no_write_endpoint_is_a_stub` sẽ luôn pass một cách rỗng (vacuous).",
                pytrace=False,
            )

    def test_missing_module_endpoints(self):
        """Các module docs/06 khai báo nhưng chưa có route nào."""
        prefixes = {p for _, p, _ in _route_table()}
        expected = ["/integrations", "/integration-runs", "/reconciliations", "/audit-events", "/events"]
        # activity dùng /activity thay vì /events
        present = set(prefixes)
        missing = []
        for e in expected:
            if e == "/events":
                if not any("/stream" in p for p in present):
                    missing.append("GET /events/stream (SSE)")
                continue
            if not any(p.startswith(e) for p in present):
                missing.append(e)
        if missing:
            pytest.fail(
                "Chưa có endpoint cho: " + ", ".join(missing) + " (docs/06; B09/B10/B14).",
                pytrace=False,
            )

    def test_idempotency_key_is_required_on_write_commands(self):
        """docs/06 + docs/05 bước 1-2: mọi write có X-Request-ID, create/submit/approve cần Idempotency-Key."""
        srcs = "\n".join(f.read_text() for f in ROUTES_DIR.glob("*.py"))
        if "idempotency" not in srcs.lower():
            pytest.fail(
                "Không có xử lý `Idempotency-Key` ở bất kỳ route nào -> ca A3/A4 "
                "(retry không nhân đôi, cùng key khác payload = 409) không thể đạt. "
                "docs/06 dòng 3, docs/05 bước 2.",
                pytrace=False,
            )

    def test_if_match_version_is_enforced(self):
        srcs = "\n".join(f.read_text() for f in ROUTES_DIR.glob("*.py"))
        if "if-match" not in srcs.lower() and "if_match" not in srcs.lower():
            pytest.fail(
                "Không có kiểm tra header `If-Match` (optimistic locking) -> "
                "ca A2 (hai checker approve đồng thời) không có cơ chế 409 CONFLICT_VERSION "
                "theo docs/06 + docs/05.",
                pytrace=False,
            )

    def test_maker_checker_is_enforced(self):
        """Invariant 1 (docs/00): created_by != approved_by, kể cả admin."""
        srcs = "\n".join(f.read_text() for f in ROUTES_DIR.glob("*.py"))
        if "approved_by" not in srcs and "approver_id" not in srcs:
            pytest.fail(
                "Không có kiểm tra `created_by != approved_by` trong route nào -> "
                "người tạo tự duyệt được. Invariant 1 (docs/00), ca A1 (403).",
                pytrace=False,
            )

    def test_error_body_follows_contract(self):
        """docs/06: `{"error":{"code","message","request_id","details"}}`."""
        # NOTE (vòng 7): từng xanh giả vì 1 file (`auth_google.py`, do reviewer
        # viết tính năng) có envelope trong khi cả app vẫn trả `detail`.
        # Invariant đúng: envelope phải nằm ở chỗ dùng chung (handler toàn cục
        # hoặc module errors) — không đếm file tính năng đơn lẻ.
        shared = [
            f for f in [BACKEND_DIR / "app" / "api" / "errors.py", BACKEND_DIR / "app" / "main.py"]
            if f.exists() and '"error"' in f.read_text()
        ]
        route_hits = [
            f.name for f in ROUTES_DIR.glob("*.py") if '"error"' in f.read_text()
        ]
        has_envelope = bool(shared) or len(route_hits) >= 3
        if not has_envelope:
            pytest.fail(
                "Mọi lỗi đang trả `detail` kiểu FastAPI, không phải hợp đồng "
                '`{"error":{"code","message","request_id","details"}}` của docs/06. '
                "Không có error code nào trong docs/05 mẫu lỗi "
                "(SELF_APPROVAL_FORBIDDEN, INSUFFICIENT_STOCK...).",
                pytrace=False,
            )

    def test_sse_endpoint_matches_contract(self):
        """docs/06 SSE: GET /events/stream?cursor=, Last-Event-ID, resync_required."""
        src = (ROUTES_DIR / "activity.py").read_text()
        problems = []
        if "/events/stream" not in src:
            problems.append("đường dẫn không phải `GET /events/stream` (đang dùng /activity/stream)")
        if "cursor" not in src:
            problems.append("không nhận query `cursor`")
        if "Last-Event-ID" not in src:
            problems.append("không tôn trọng header `Last-Event-ID` khi reconnect")
        if "resync_required" not in src:
            problems.append("không gửi `resync_required` khi cursor quá cũ")
        if "heartbeat" in src and "job.updated" not in src:
            problems.append("chỉ phát heartbeat, không có event `job.updated`/`activity.created`")
        if problems:
            pytest.fail(
                "SSE không khớp hợp đồng docs/06 (ca E1 không đạt):\n  - "
                + "\n  - ".join(problems),
                pytrace=False,
            )

    def test_pagination_and_cursor_contract(self):
        srcs = "\n".join(f.read_text() for f in ROUTES_DIR.glob("*.py"))
        if "cursor" not in srcs:
            pytest.fail(
                "Không có pagination `limit`/`cursor` trên endpoint list nào "
                "(docs/06: limit 1..100 + opaque cursor, sort ổn định).",
                pytrace=False,
            )


# ------------------------------------------------------------- B02/B05: DB --
class TestSchemaVsDocs04:
    REQUIRED_TABLES = [
        "iam_users", "iam_roles", "iam_user_roles",
        "inv_locations", "inv_items", "inv_assets", "inv_requests", "inv_request_lines",
        "inv_movements", "inv_balances",
        "cash_boxes", "cash_requests", "cash_journals", "cash_entries", "cash_balances",
        "approval_decisions", "audit_events", "outbox_events", "jobs",
        "integration_connections", "integration_runs", "integration_staging_rows",
        "activity_events", "reconciliation_runs", "reconciliation_exceptions",
    ]

    def test_required_tables_are_modelled(self, models):
        """`models` fixture sẽ fail sớm nếu model module không import được."""
        from app.db.base import Base

        have = set(Base.metadata.tables)
        missing = [t for t in self.REQUIRED_TABLES if t not in have]
        if missing:
            pytest.fail(
                "Thiếu bảng theo docs/04: " + ", ".join(missing),
                pytrace=False,
            )

    def test_timestamps_are_timestamptz_not_string(self):
        """docs/04: `created_at/updated_at timestamptz` UTC; không lưu thời điểm dạng text."""
        bad = []
        for f in MODELS_DIR.glob("*.py"):
            for i, line in enumerate(f.read_text().splitlines(), 1):
                if re.search(
                    r"(submitted_at|posted_at|occurred_at)\s*:.*Column\(String\(", line
                ):
                    bad.append(f"{f.name}:{i} {line.strip()}")
        if bad:
            pytest.fail(
                "Thời điểm nghiệp vụ đang khai báo kiểu String thay vì timestamptz "
                "(docs/04):\n  - " + "\n  - ".join(bad),
                pytrace=False,
            )

    def test_money_and_quantity_check_constraints(self):
        """docs/04: amount > 0, quantity >= 0, debit/credit >= 0, exactly one positive."""
        # docs/04: quantity >= 0 (inv_balances), amount >= 0 (cash_balances)
        src = "\n".join(f.read_text() for f in MODELS_DIR.glob("*.py"))
        problems = []
        if "CheckConstraint" not in src:
            problems.append("không có CheckConstraint nào -> `amount > 0`, `quantity >= 0`, "
                            "`debit/credit >= 0`, `SUM(debit)=SUM(credit)` (docs/04) không được DB enforce")
        if "version_id_col" not in src and "version_id" not in src:
            problems.append("cột `version` tồn tại nhưng không gắn `version_id_col` -> "
                            "SQLAlchemy không tự chặn write mất mát (optimistic locking docs/04/05)")
        if problems:
            pytest.fail("Ràng buộc dữ liệu thiếu:\n  - " + "\n  - ".join(problems), pytrace=False)

    def test_ledger_double_entry_exists(self):
        """Invariant 3 (docs/00) + docs/04 section Ledger quỹ mô phỏng."""
        models_src = "\n".join(f.read_text() for f in MODELS_DIR.glob("*.py"))
        app_src = "\n".join(
            p.read_text()
            for p in (BACKEND_DIR / "app").rglob("*.py")
            if "__pycache__" not in str(p)
        )
        if "SIM_CASH" not in models_src and "SIM_CASH" not in app_src:
            pytest.fail(
                "Không thấy account codes `SIM_CASH:*`/`SIM_OFFSET:DEMO` và không có "
                "posting service nào (docs/04 'Ledger quỹ mô phỏng'). Chỉ có bảng "
                "cash_entries rỗng, không code nào tạo bút toán -> C1/C2/C3 không kiểm chứng được.",
                pytrace=False,
            )

    def test_movement_unique_and_balance_guard(self):
        """docs/04: unique(request_id,line_id,location_id) cho inv_movements."""
        inv = (MODELS_DIR / "inventory.py").read_text()
        movement_block = inv.split("class Movement", 1)[-1].split("class ", 1)[0]
        if "UniqueConstraint" not in movement_block and "unique=True" not in movement_block:
            pytest.fail(
                "inv_movements không có unique(request_id, line_id, location_id) "
                "(docs/04) -> retry/đồng thời có thể ghi movement trùng.",
                pytrace=False,
            )

    def test_approval_unique_final_decision(self):
        """docs/04: approval_decisions ... unique final decision per subject."""
        src = (MODELS_DIR / "approval.py").read_text()
        if "UniqueConstraint" not in src:
            pytest.fail(
                "approval_decisions không có UniqueConstraint(subject_type, subject_id) "
                "-> hai quyết định final cùng subject vẫn ghi được (docs/04).",
                pytrace=False,
            )

    def test_audit_append_only_guard(self):
        """Invariant 5 (docs/00): audit append-only."""
        src = (MODELS_DIR / "activity.py").read_text()
        guard = any(k in src for k in ("append_only", "before_delete", "DML", "event.listen"))
        if not guard:
            pytest.fail(
                "audit_events là bảng thường, không có cơ chế append-only "
                "(không trigger/DDL guard) -> có thể UPDATE/DELETE audit "
                "(Invariant 5 docs/00; docs/04 'không delete').",
                pytrace=False,
            )


# ------------------------------------------------- cross-cutting: no real $ --
class TestNoRealMoney:
    def test_no_real_payment_or_bank_integration(self):
        """Invariant: chỉ cash_sim, không API ngân hàng/payment rail (docs/00/README)."""
        banned = ["stripe", "paypal", "vnpay", "zalopay", "momo", "solidgate", "adyen"]
        hits = []
        roots = [BACKEND_DIR / "app", REPO_DIR / "docs", REPO_DIR / "sample-data"]
        for root in roots:
            if not root.exists():
                continue
            for f in root.rglob("*"):
                if not f.is_file() or "__pycache__" in str(f):
                    continue
                if f.suffix not in {".py", ".json", ".yml", ".yaml", ".md", ".toml"}:
                    continue
                try:
                    low = f.read_text().lower()
                except Exception:
                    continue
                for b in banned:
                    if b in low:
                        hits.append(f"{f.relative_to(REPO_DIR)}: {b}")
        if hits:
            pytest.fail("Có dấu hiệu tích hợp tiền thật: " + ", ".join(hits), pytrace=False)

    def test_demo_label_present_in_cash_responses(self):
        """Invariant 6 (docs/00): mọi response quỹ phải nói rõ DEMO/SIMULATED."""
        src = (ROUTES_DIR / "cash_sim.py").read_text()
        # phải là key JSON trả về, KHÔNG tính từ docstring "Cash simulation routes."
        has_key = bool(
            re.search(r'["\']simulation["\']\s*:', src)
            or re.search(r'["\']demo["\']\s*:', src)
            or re.search(r'["\']simulated["\']\s*:', src)
        )
        if not has_key:
            pytest.fail(
                "GET /cash-sim/* không trả trường `simulation: true` / nhãn DEMO "
                "(docs/06 response mẫu có `\"simulation\": true`; Invariant 6 docs/00: "
                "'Báo cáo luôn nói rõ DEMO / SIMULATED').",
                pytrace=False,
            )

    def test_openapi_marks_login_public_and_health_public(self):
        """docs/06: auth/login public; docs/openapi không được bắt Bearer cho public routes."""
        src = (BACKEND_DIR / "app" / "api" / "openapi.py").read_text()
        if src.count("if spec.get") and "login" not in src:
            pytest.fail(
                "custom_openapi gắn `security: BearerAuth` cho **mọi** operation kể cả "
                "`POST /auth/login` và `/health/*` (docs/06: public/session, infra). "
                "OpenAPI sai hợp đồng, Swagger 'Authorize' bắt buộc với login.",
                pytrace=False,
            )


class TestLiveContracts:
    """Hợp đồng kiểm được bằng HTTP thật mà không cần login.

    Các test này chạy được ngay (app boot + PostgreSQL có) và sẽ tự xanh
    khi agent sửa mã — không cần chờ login.
    """

    def test_responses_carry_request_id_header(self, client):
        """Mọi response phải có `X-Request-ID`.

        docs/06:3 yêu cầu mọi write mang `X-Request-ID`, envelope lỗi
        (docs/06:44) chứa `request_id`, và `app/main.py:33` đã
        `expose_headers=["X-Request-ID", "X-Correlation-ID"]` cho CORS —
        nhưng `CorrelationIdMiddleware` chỉ ghi ID vào request scope,
        **không bao giờ set response header**.
        """
        for path in ("/health/live", "/inventory/items"):
            resp = client.get(path)
            rid = resp.headers.get("x-request-id")
            if not rid:
                pytest.fail(
                    f"GET {path} -> {resp.status_code} nhưng thiếu response header "
                    f"`X-Request-ID` (headers trả về: {dict(resp.headers)}).\n"
                    "  `CorrelationIdMiddleware.__call__` không bọc `send` để gắn "
                    "header vào response -> client không bao giờ nhận được ID để "
                    "đối chiếu log/trace, envelope lỗi cũng không có `request_id` "
                    "thật.\n"
                    "  Sửa: bọc `send` trong middleware, gắn `X-Request-ID` + "
                    "`X-Correlation-ID` vào mọi response.",
                    pytrace=False,
                )

    def test_deployed_cors_config_has_no_credentialed_wildcard_echo(self):
        """Cấu hình CORS khi deploy không được echo origin lạ kèm credentials.

        `docker-compose.yml:32` đặt `ALLOWED_ORIGINS=*` trong khi
        `app/main.py:30` đặt `allow_credentials=True`. Với đúng 2 giá trị
        này, Starlette sẽ `Access-Control-Allow-Origin: <origin-kẻ-tấn-công>`
        + `Access-Control-Allow-Credentials: true` -> mọi website đều đọc
        được response mang cookie/session của user.

        Test dựng đúng middleware với đúng giá trị deploy (đọc từ 2 file),
        nên khi agent sửa compose/main thì test tự xanh mà không cần sửa test.
        """
        import re

        from fastapi import FastAPI
        from fastapi.middleware.cors import CORSMiddleware
        from fastapi.testclient import TestClient

        compose = (REPO_DIR / "docker-compose.yml").read_text()
        m = re.search(r"ALLOWED_ORIGINS=(\S+)", compose)
        raw = m.group(1).strip().strip("'\"") if m else ""
        origins = ["*"] if raw == "*" else [o for o in raw.split(",") if o]

        main_src = (BACKEND_DIR / "app" / "main.py").read_text()
        creds = "allow_credentials=True" in main_src

        mini = FastAPI()

        @mini.get("/probe")
        async def probe():  # noqa: RUF029 - endpoint giả cho CORS check
            return {"ok": True}

        mini.add_middleware(
            CORSMiddleware,
            allow_origins=origins,
            allow_credentials=creds,
            allow_methods=["*"],
            allow_headers=["*"],
        )

        evil = "https://evil.example"
        with TestClient(mini, raise_server_exceptions=False) as c:
            pre = c.options(
                "/probe",
                headers={"Origin": evil, "Access-Control-Request-Method": "GET"},
            )
            simple = c.get("/probe", headers={"Origin": evil})

        echoed = [
            f"preflight ACAO={pre.headers.get('access-control-allow-origin')!r}"
            for _ in [0]
            if pre.headers.get("access-control-allow-origin") == evil
        ] + [
            f"simple ACAO={simple.headers.get('access-control-allow-origin')!r}"
            for _ in [0]
            if simple.headers.get("access-control-allow-origin") == evil
        ]
        if creds and echoed:
            pytest.fail(
                f"CORS khi deploy echo origin lạ kèm credentials ({'; '.join(echoed)}, "
                f"ACAC=true) với allow_origins={origins} từ compose + "
                f"allow_credentials=True từ main.py.\n"
                "  Sửa: liệt kê origin cụ thể trong compose (bỏ `*`), hoặc "
                "`allow_credentials=False` nếu không dùng cookie.",
                pytrace=False,
            )

    def test_unauthorized_errors_use_docs06_envelope(self, client):
        """Lỗi 401 phải theo envelope docs/06:44, không phải `{"detail": ...}`.

        docs/06:44: `{"error":{"code":"...","message":"...","request_id":"...","details":{}}}`.
        Thực tế: `{"detail":"Not authenticated"}` — client không có `code`
        ổn định để rẽ nhánh, không có `request_id` để đối chiếu log.
        """
        resp = client.get("/inventory/items")
        assert resp.status_code == 401, f"mong đợi 401, được {resp.status_code}"
        try:
            body = resp.json()
        except Exception:  # noqa: BLE001
            body = None
        err = (body or {}).get("error") if isinstance(body, dict) else None
        if not isinstance(err, dict) or not {"code", "message", "request_id"} <= set(err):
            pytest.fail(
                f"GET /inventory/items không token trả body {body!r} thay vì envelope "
                f"docs/06:44 (`error.code/message/request_id`).\n"
                "  Sửa: custom exception handler trả envelope chuẩn cho mọi "
                "HTTPException (401/403/404/422/500).",
                pytrace=False,
            )

    def test_openapi_security_marks_only_protected_paths(self, app):
        """Schema OpenAPI: public path không `security`, protected path có BearerAuth.

        docs/06:9 xếp `POST /auth/login` + `/health/*` vào nhóm public/session,
        nhưng `custom_openapi` gắn `security: BearerAuth` cho **mọi** operation.

        Lưu ý phương pháp: GET HTTP `/api/v1/openapi.json` qua TestClient trả 404
        **kể cả trên app FastAPI trắng** (quirk của combo FastAPI 0.143 +
        Starlette 1.7 với `root_path`, đã cô lập bằng app mini) -> test đọc
        `app.openapi()` in-process, không phải bug của dự án.
        """
        schema = app.openapi()
        paths = schema.get("paths", {})
        if "/auth/login" not in paths:
            pytest.fail("schema thiếu POST /auth/login", pytrace=False)

        wrongly_secured = [
            f"{method.upper()} {path}"
            for path in ("/auth/login", "/health/live", "/health/ready")
            for method, op in (paths.get(path, {}) or {}).items()
            if isinstance(op, dict) and op.get("security")
        ]
        if wrongly_secured:
            pytest.fail(
                "OpenAPI gắn `security: BearerAuth` cho public path: "
                + ", ".join(wrongly_secured)
                + " (docs/06:9 = public/session). Swagger UI sẽ đòi Authorize "
                "để login.\n"
                "  Sửa: trong `custom_openapi`, bỏ qua các path public khi gắn "
                "`spec['security']`.",
                pytrace=False,
            )

        public = {"/auth/login", "/auth/refresh", "/auth/logout", "/me",
                  "/health/live", "/health/ready"}
        protected_with_auth = [
            f"{method.upper()} {path}"
            for path, ops in paths.items()
            if path not in public
            for method, op in (ops or {}).items()
            if isinstance(op, dict) and op.get("security") == [{"BearerAuth": []}]
        ]
        if not protected_with_auth:
            pytest.fail(
                "Không path bảo vệ nào trong schema còn `security: BearerAuth` -> "
                "sửa lỗi trên không được gỡ toàn bộ security (mất khai báo auth "
                "cho client codegen).",
                pytrace=False,
            )


class TestWorkerOutboxJobs:
    """docs/09: worker claim job bằng DB lock/lease + dispatch outbox sau commit."""

    def test_worker_claims_jobs_with_lease_and_dispatches_outbox(self):
        """`app/worker.py` hiện chỉ là `while True: sleep(30)` — placeholder.

        docs/09:26-28 yêu cầu: statuses queued/running/succeeded/failed/cancelled,
        claim bằng DB lock/lease, heartbeat, retry count + next_attempt, job
        idempotent theo job key, outbox dispatcher phát activity sau commit.
        Worker hiện tại không chạm DB nên không job nào chạy, outbox không phát,
        SSE (E1) không có event.
        """
        import re

        src = (BACKEND_DIR / "app" / "worker.py").read_text()
        code = re.sub(r'"""[\s\S]*?"""', "", src)  # bỏ docstring
        code = "\n".join(
            ln for ln in code.splitlines() if not ln.strip().startswith("#")
        )
        low = code.lower()
        # NOTE: "heartbeat" cố ý không nằm trong danh sách — log heartbeat một
        # mình (như `logger.debug("Worker heartbeat")` hiện tại) không chứng minh
        # worker claim job. Cần ít nhất 1 tín hiệu chạm DB/outbox thật.
        signals = [k for k in ("outbox", "lease", "claim", "next_attempt",
                               "asyncsession", "select(", "dispatch", "retry")
                   if k in low]
        if not signals:
            pytest.fail(
                "app/worker.py không có bất kỳ logic claim-job/dispatch-outbox nào "
                "(chỉ `while True: sleep(30)`), trong khi DB đã có bảng `jobs` + "
                "`outbox_events` và docs/09 yêu cầu claim bằng lock/lease, heartbeat, "
                "retry, outbox dispatch sau commit.\n"
                "  Sửa: worker poll `jobs` (claim + lease + heartbeat), chạy job "
                "idempotent theo job key, phát `outbox_events` chưa gửi.",
                pytrace=False,
            )


# ---------------------------------------------------------- B04/B15: client --
class TestFlutterAndQa:
    def test_flutter_google_signin_wiring_exists(self):
        """Google Sign-In Flutter: pubspec + plist + URL scheme + auth UI.

        Kiểm tĩnh (không có Flutter SDK để analyze): pubspec có
        `google_sign_in`/`http`/`flutter_secure_storage`;
        `ios/Runner/GoogleService-Info.plist` tồn tại (chỉ chứa client ID
        công khai, không secret); `Info.plist` có URL scheme
        `com.googleusercontent.apps...` (REVERSED_CLIENT_ID);
        `auth_service.dart` + `login_screen.dart` tồn tại và main.dart gate
        AppShell sau login.
        """
        pubspec = (REPO_DIR / "frontend" / "pubspec.yaml").read_text()
        for dep in ("google_sign_in:", "http:", "flutter_secure_storage:"):
            if dep not in pubspec:
                pytest.fail(f"frontend/pubspec.yaml thiếu dependency `{dep}`", pytrace=False)
        plist = REPO_DIR / "frontend" / "ios" / "Runner" / "GoogleService-Info.plist"
        if not plist.exists():
            pytest.fail("thiếu frontend/ios/Runner/GoogleService-Info.plist", pytrace=False)
        if "GOCSPX" in plist.read_text():
            pytest.fail("GoogleService-Info.plist chứa client SECRET — không được commit!", pytrace=False)
        info = (REPO_DIR / "frontend" / "ios" / "Runner" / "Info.plist").read_text()
        if "com.googleusercontent.apps." not in info:
            pytest.fail("Info.plist thiếu URL scheme REVERSED_CLIENT_ID cho Google callback", pytrace=False)
        for rel in ("lib/src/features/auth/auth_service.dart", "lib/src/features/auth/login_screen.dart"):
            if not (REPO_DIR / "frontend" / rel).exists():
                pytest.fail(f"thiếu frontend/{rel}", pytrace=False)
        main = (REPO_DIR / "frontend" / "lib" / "main.dart").read_text()
        if "AuthService" not in main or "LoginScreen" not in main:
            pytest.fail("main.dart chưa gate AppShell sau AuthService/LoginScreen", pytrace=False)
        if "24A434" in plist.read_text():
            pytest.skip(
                "WIRING ĐỦ nhưng BUNDLE_ID trong plist (`24A434`) khác app "
                "(`com.example.bankops`) — iOS Google Sign-In có thể từ chối; "
                "cần đồng bộ bundle ID trên Google Console hoặc Xcode."
            )

    def test_flutter_shell_has_three_breakpoint_tiers(self):
        """docs/07: 3 tier <600 / 600-1199 / >=1200 với nav khác nhau.

        Hiện tại: `Breakpoints.medium=600` + `wide=1200` đã khai, shell đổi
        NavigationBar (compact) / NavigationRail (medium+) — nhưng tier wide
        (>=1200: sidebar + topbar theo docs/07:17) chưa tách riêng, rail dùng
        chung cho cả medium lẫn wide.
        """
        lib = REPO_DIR / "frontend" / "lib"
        tokens = (lib / "src" / "core" / "theme" / "app_tokens.dart").read_text()
        shell = (lib / "src" / "app" / "app_shell.dart").read_text()
        problems = []
        if "600" not in tokens or "1200" not in tokens:
            problems.append("thiếu hằng breakpoint 600/1200 theo docs/07")
        if "wide" not in shell.lower() and "sidebar" not in shell.lower():
            problems.append(
                "app_shell.dart không phân biệt tier wide (>=1200 sidebar) — "
                "rail đang dùng chung cho medium + wide",
            )
        if problems:
            pytest.fail("Flutter shell thiếu tier responsive: " + "; ".join(problems), pytrace=False)

    def test_flutter_app_exists(self):
        """B04/B05/B15: Flutter shell + 3 breakpoint + E2E."""
        candidates = [
            REPO_DIR / "frontend",
            REPO_DIR / "app",
            REPO_DIR / "lib",
            REPO_DIR / "mobile",
        ]
        pubspecs = [p for p in candidates if (p / "pubspec.yaml").exists()]
        pubspecs += list(REPO_DIR.glob("**/pubspec.yaml"))
        if not pubspecs:
            pytest.fail(
                "Không có Flutter project nào trong repo (không có pubspec.yaml) -> "
                "B04, B05 'screen', B08 'DEMO label', B15 E2E chưa bắt đầu. "
                "docs/00 yêu cầu một Flutter monorepo app.",
                pytrace=False,
            )

    def test_backend_has_tests(self):
        """B01/B15 + docs/10 test pyramid: phải có test chạy được."""
        # NOTE (vòng 7): `test_google_auth.py` cũng do QA reviewer viết cho
        # tính năng Google Sign-In — không được tính là test của developer.
        tests = [
            f
            for f in (BACKEND_DIR / "tests").glob("test_*.py")
            if f.name not in {"test_b01_foundation.py", "test_contract_vs_docs.py", "test_acceptance_qa.py", "test_google_auth.py"}
        ]
        if not tests:
            pytest.fail(
                "backend/tests không có test nào của developer (chỉ có test do QA reviewer "
                "viết) -> mọi ca A1-E1, S1 trong docs/10 chưa từng được thực thi.",
                pytrace=False,
            )

    def test_openapi_snapshot_test_exists(self):
        """docs/10: 'OpenAPI snapshot' trong API contract tier."""
        # Không được đếm file do QA reviewer viết (tránh "xanh giả" tự khớp).
        reviewer = {
            "test_b01_foundation.py",
            "test_contract_vs_docs.py",
            "test_acceptance_qa.py",
            "test_google_auth.py",
            "conftest.py",
        }
        hits = []
        for f in BACKEND_DIR.rglob("test_*.py"):
            if f.name in reviewer:
                continue
            if f.stat().st_size == 0:
                continue
            text = f.read_text()
            if "openapi" in text.lower() and "snapshot" in text.lower():
                hits.append(f.name)
        if not hits:
            pytest.fail(
                "Chưa có test OpenAPI snapshot của developer (docs/10 Test pyramid). "
                "3 file test hiện có đều do QA reviewer viết.",
                pytrace=False,
            )
