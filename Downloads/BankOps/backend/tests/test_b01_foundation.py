"""B01/B02 - Nền tảng: repo skeleton, CI, compose, boot/health, migration, seed.

Chứng cứ hồi quy cho các lỗi hạ tầng đã phát hiện. Mọi test ở đây chạy được
không cần PostgreSQL, Docker daemon hay Flutter.
"""
from __future__ import annotations

import re

import pytest
import tomllib
from conftest import BACKEND_DIR, REPO_DIR, run

try:  # pyyaml không nằm trong dependency của dự án -> không được làm test collection chết
    import yaml
except ImportError:  # pragma: no cover
    yaml = None

PYPROJECT = BACKEND_DIR / "pyproject.toml"
COMPOSE = REPO_DIR / "docker-compose.yml"
DOCKERFILE = REPO_DIR / "Dockerfile"
BACKEND_DOCKERFILE = BACKEND_DIR / "Dockerfile"
ALEMBIC_VERSIONS = BACKEND_DIR / "alembic" / "versions"
TESTS_DIR = BACKEND_DIR / "tests"


# ---------------------------------------------------------------- B01: build --
class TestB01Build:
    def test_pyproject_toml_is_valid(self):
        """pyproject phải parse được; nếu không thì poetry/pytest/ruff/mypy đều chết."""
        try:
            tomllib.loads(PYPROJECT.read_text())
        except tomllib.TOMLDecodeError as exc:
            pytest.fail(f"`backend/pyproject.toml` sai cú pháp TOML: {exc}", pytrace=False)

    def test_declared_runtime_deps_cover_imports(self):
        """Mọi module import ở runtime phải nằm trong dependencies."""
        try:
            data = tomllib.loads(PYPROJECT.read_text())
        except tomllib.TOMLDecodeError as exc:
            # pyproject hỏng là lỗi riêng (test_pyproject_toml_is_valid); vẫn kiểm deps
            # bằng regex để không che mất thông tin.
            text = PYPROJECT.read_text()
            required = ["pydantic-settings", "asyncpg", "opentelemetry"]
            missing = [p for p in required if p not in text]
            pytest.fail(
                f"pyproject.toml không parse được ({exc}) nên kiểm tra deps bằng regex; "
                f"thiếu: {missing}",
                pytrace=False,
            )
        deps = data.get("tool", {}).get("poetry", {}).get("dependencies", {})
        required = {
            "pydantic-settings": "app/config.py import pydantic_settings",
            "asyncpg": "app/database.py tạo engine postgresql+asyncpg://",
            "opentelemetry-api": "app/api/middleware/correlation.py import opentelemetry",
            # app/main.py:8 -> starlette.middleware.sessions -> import itsdangerous
            # (đã xác minh: cài sạch theo pyproject -> `import app.main` TypeError)
            "itsdangerous": "app/main.py import starlette.middleware.sessions (cần itsdangerous)",
        }
        missing = [
            f"{pkg} ({why})"
            for pkg, why in required.items()
            if pkg not in deps and pkg.split("-")[0] not in deps
        ]
        if missing:
            pytest.fail(
                "Thiếu runtime dependency trong pyproject.toml:\n  - "
                + "\n  - ".join(missing)
                + "\n  Hậu quả đã xác minh: cài sạch -> `import app.main` "
                "ModuleNotFoundError, app không boot được.",
                pytrace=False,
            )

    def test_sqlalchemy_async_extra_or_greenlet_declared(self):
        """`sqlalchemy.ext.asyncio` cần greenlet; pip/poetry không tự kéo nếu thiếu extra.

        Đã xác minh với cài sạch Python 3.11 theo đúng pyproject:
        `import sqlalchemy.ext.asyncio` ->
        ImportError: The SQLAlchemy asyncio module requires that the Python
        'greenlet' library is installed.
        """
        data = tomllib.loads(PYPROJECT.read_text())
        deps = data.get("tool", {}).get("poetry", {}).get("dependencies", {})
        sqlalchemy_spec = deps.get("sqlalchemy")
        has_async_extra = isinstance(sqlalchemy_spec, str) and "[asyncio]" in sqlalchemy_spec
        has_greenlet = "greenlet" in deps
        if not (has_async_extra or has_greenlet):
            pytest.fail(
                "pyproject.toml khai báo `sqlalchemy` mà không kèm extra `[asyncio]` "
                "và không khai báo `greenlet` -> cài sạch sẽ ImportError ở "
                "`app/database.py` (`from sqlalchemy.ext.asyncio import ...`) -> "
                "app không boot được.\n"
                f"  sqlalchemy hiện tại: {sqlalchemy_spec!r}\n"
                "  Sửa: `sqlalchemy = {\"version\": \"^2.0.35\", \"extras\": [\"asyncio\"]}` "
                "hoặc thêm `greenlet`.",
                pytrace=False,
            )

    def test_compose_file_is_parseable_by_compose(self):
        """`docker compose up` không được fail ở bước parse YAML."""
        proc = run(["docker", "compose", "config"], cwd=REPO_DIR)
        if proc.returncode != 0:
            pytest.fail(
                "`docker compose config` thất bại (compose file không hợp lệ):\n"
                + (proc.stderr or proc.stdout),
                pytrace=False,
            )

    def test_compose_defines_api_service(self):
        """docs/11 yêu cầu services postgres, api, worker, frontend-web."""
        if yaml is None:
            pytest.skip(
                "pyyaml không được cài và không nằm trong dependency dự án; "
                "`test_compose_file_is_parseable_by_compose` (docker compose config) "
                "vẫn kiểm tính hợp lệ của compose file."
            )
        data = yaml.safe_load(COMPOSE.read_text())
        services = set((data or {}).get("services", {}) or {})
        missing = {"api", "worker"} - services
        if missing:
            pytest.fail(
                f"docker-compose.yml thiếu service: {sorted(missing)}. "
                f"Hiện chỉ có: {sorted(services)}",
                pytrace=False,
            )

    def test_compose_does_not_use_asyncpg_url_without_asyncpg_dep(self):
        """DATABASE_URL asyncpg phải đi kèm dependency asyncpg."""
        text = COMPOSE.read_text()
        pyproject = PYPROJECT.read_text()
        if "postgresql+asyncpg://" in text and "asyncpg" not in pyproject:
            pytest.fail(
                "docker-compose dùng DATABASE_URL asyncpg nhưng pyproject không khai báo "
                "`asyncpg` -> engine tạo là asyncpg driver sẽ ImportError lúc boot.",
                pytrace=False,
            )

    def test_compose_seed_callable_exists(self):
        """Compose gọi `from app.seed import seed_demo` -> hàm phải tồn tại."""
        compose = COMPOSE.read_text()
        seed_src = (BACKEND_DIR / "app" / "seed.py").read_text()
        if "seed_demo" in compose and "def seed_demo" not in seed_src:
            pytest.fail(
                "docker-compose.yml chạy `from app.seed import seed_demo` nhưng "
                f"app/seed.py không định nghĩa `seed_demo` (chỉ có "
                f"{[ln for ln in seed_src.splitlines() if ln.startswith('async def') or ln.startswith('def ')]}). "
                "Container api sẽ chết ngay sau migrate.",
                pytrace=False,
            )

    def test_compose_worker_module_exists(self):
        compose = COMPOSE.read_text()
        if "python -m app.worker" in compose:
            worker = BACKEND_DIR / "app" / "worker.py"
            if not worker.exists():
                pytest.fail(
                    "docker-compose chạy `python -m app.worker` nhưng "
                    "`backend/app/worker.py` không tồn tại -> worker container crash.",
                    pytrace=False,
                )

    def test_compose_operational_basics(self):
        """Compose demo phải đủ tối thiểu vận hành, không config gây hiểu nhầm."""
        import re

        compose = COMPOSE.read_text()
        problems = []

        # (a) mount alembic .py vào initdb.d: Postgres chỉ chạy *.sql/*.sh,
        # .py bị bỏ qua -> mount này vô dụng và gây tưởng migrate chạy ở initdb.
        if "alembic/versions:/docker-entrypoint-initdb.d" in compose:
            problems.append(
                "`postgres.volumes` mount `./backend/alembic/versions:/docker-entrypoint-initdb.d` "
                "nhưng Postgres bỏ qua file `.py` -> migrate không chạy ở đây; "
                "mount gây hiểu nhầm (migrate thật do api/worker chạy "
                "`alembic upgrade head`). Sửa: bỏ mount này."
            )

        # (b) restart policy: không có -> container chết là nằm chết.
        for svc in ("postgres:", "api:", "worker:", "frontend-web:"):
            m = re.search(rf"^  {svc}$(.*?)(?=^  \w|\nvolumes:|\Z)", compose, re.M | re.S)
            block = m.group(1) if m else ""
            if "restart:" not in block:
                problems.append(
                    f"service `{svc[:-1]}` thiếu `restart:` (vd `unless-stopped`) -> "
                    f"container crash không tự dậy."
                )

        # (c) api và worker cùng chạy `alembic upgrade head` lúc start -> race
        # migrate song song (alembic không có distributed lock).
        runners = compose.count("alembic upgrade head")
        if runners > 1:
            problems.append(
                f"`alembic upgrade head` chạy ở {runners} service (api + worker) -> "
                f"race migrate khi start đồng thời. Sửa: chỉ 1 nơi migrate "
                f"(vd init container), worker/api chờ (`depends_on` + retry)."
            )

        if problems:
            pytest.fail(
                "docker-compose.yml thiếu tối thiểu vận hành:\n  - " + "\n  - ".join(problems),
                pytrace=False,
            )

    def test_mypy_strict_passes(self):
        """`pyproject.toml [tool.mypy]` + docs/14 stage 1 yêu cầu mypy strict.

        Hiện tại ~200+ lỗi (thiếu annotation). Test chạy `python -m mypy app`;
        skip khi thiếu mypy (dev dependency, CI có).
        """
        import importlib.util
        import sys

        from conftest import run

        if importlib.util.find_spec("mypy") is None:
            pytest.skip("mypy chưa cài (dev dependency)")
        proc = run([sys.executable, "-m", "mypy", "app"], cwd=BACKEND_DIR, timeout=600)
        if proc.returncode != 0:
            tail = "\n".join((proc.stdout or "").splitlines()[-8:])
            pytest.fail(
                f"mypy strict fail (returncode={proc.returncode}). 8 dòng cuối:\n{tail}\n"
                f"  Sửa: annotate toàn bộ `app/` theo `[tool.mypy]` "
                f"(disallow_untyped_defs...). Đây là gate stage 1 theo docs/14.",
                pytrace=False,
            )

    def test_backend_dockerfile_copy_sources_exist(self):
        """Mọi COPY source phải tồn tại trong build context (backend/)."""
        bad = []
        for line in BACKEND_DOCKERFILE.read_text().splitlines():
            line = line.strip()
            if not line.startswith("COPY ") or line.startswith("COPY --"):
                continue
            parts = line.split()
            if len(parts) < 3 or parts[-1].startswith("--"):
                continue
            srcs = parts[1:-1]
            for src in srcs:
                if "*" in src or any(op in src for op in ("2>", "||", "&&")):
                    continue  # glob / cú pháp shell: test riêng ở test_..._shell_redirection
                if not (BACKEND_DIR / src).exists() and not (REPO_DIR / src).exists():
                    bad.append(src)
        if bad:
            pytest.fail(
                "backend/Dockerfile COPY nguồn không tồn tại trong build context: "
                + ", ".join(bad),
                pytrace=False,
            )

    def test_backend_dockerfile_has_no_shell_redirection_in_copy(self):
        """COPY không hiểu shell redirection -> docker build fail."""
        text = BACKEND_DOCKERFILE.read_text()
        offenders = [
            ln.strip()
            for ln in text.splitlines()
            if ln.strip().startswith("COPY ") and ("2>" in ln or "||" in ln or "<" in ln)
        ]
        if offenders:
            pytest.fail(
                "backend/Dockerfile dùng cú pháp shell trong COPY (docker không hỗ trợ):\n  "
                + "\n  ".join(offenders),
                pytrace=False,
            )

    def test_ci_workflow_exists_and_is_non_empty(self):
        """B01 yêu cầu CI."""
        workflows = sorted((REPO_DIR / ".github" / "workflows").glob("*")) if (
            REPO_DIR / ".github" / "workflows"
        ).exists() else []
        non_empty = [w for w in workflows if w.stat().st_size > 0]
        if not non_empty:
            pytest.fail(
                "Không có GitHub Actions workflow nào (`.github/workflows/` rỗng) "
                "trong khi B01 yêu cầu CI.",
                pytrace=False,
            )


# ------------------------------------------------------------- B02: migrate --
class TestB02MigrationSeed:
    def test_at_least_one_non_empty_alembic_revision(self):
        files = sorted(ALEMBIC_VERSIONS.glob("*.py"))
        if not files:
            pytest.fail("Không có revision nào trong backend/alembic/versions/ (B02).")
        empty = [f.name for f in files if f.stat().st_size == 0]
        if empty:
            pytest.fail(
                "Alembic revision rỗng (0 byte) -> `alembic upgrade head` không chạy được: "
                + ", ".join(empty)
            )

    def test_alembic_revision_defines_upgrade(self):
        files = [f for f in sorted(ALEMBIC_VERSIONS.glob("*.py")) if f.stat().st_size > 0]
        if not files:
            pytest.skip("Chưa có revision hợp lệ")
        for f in files:
            text = f.read_text()
            for needle in ("revision =", "down_revision =", "def upgrade("):
                if needle not in text:
                    pytest.fail(f"{f.name} thiếu `{needle}`.")

    def test_seed_is_not_destructive(self):
        """docs/04: seed idempotent, ledger/audit không delete; không drop_all."""
        seed = (BACKEND_DIR / "app" / "seed.py").read_text()
        if "drop_all" in seed:
            pytest.fail(
                "app/seed.py gọi `Base.metadata.drop_all` -> seed **xoá toàn bộ dữ liệu** "
                "mỗi lần chạy. docs/04 yêu cầu seed idempotent và ledger/audit không delete; "
                "docs/11 yêu cầu `migrate -> seed demo idempotent`.",
                pytrace=False,
            )

    def test_seed_is_blocked_in_production(self):
        """docs/04: seed ... bị chặn ở production. Phải là guard thật, không phải comment."""
        seed = (BACKEND_DIR / "app" / "seed.py").read_text()
        # guard thật = có nhánh điều khiển dựa trên cấu hình môi trường
        guard = bool(
            re.search(r"if\s+.*(is_demo|APP_ENV|app_env|environment)", seed)
            or re.search(r"raise\s+\w*(Error|Exception)", seed)
        )
        if not guard:
            pytest.fail(
                "app/seed.py không có guard chặn seed ở production (chỉ có comment nhắc "
                "từ 'production') -> seed có thể chạy trên môi trường production "
                "(vi phạm docs/04: 'Seed demo là lệnh riêng idempotent và bị chặn ở "
                "production').",
                pytrace=False,
            )

    def test_seed_consumes_sample_data(self):
        """README: `sample-data/*.json` là seed dữ liệu hoàn toàn giả."""
        seed = (BACKEND_DIR / "app" / "seed.py").read_text()
        if "sample-data" not in seed and "sample_data" not in seed:
            pytest.fail(
                "app/seed.py không đọc `sample-data/*.json` (README định nghĩa đây là "
                "nguồn seed) mà tự hardcode dữ liệu -> hai nguồn dữ liệu giả dễ lệch nhau; "
                "asset trong sample-data/inventory.json bị bỏ sót (xem "
                "test_seed_covers_sample_assets).",
                pytrace=False,
            )

    def test_seed_covers_sample_assets(self):
        """sample-data/inventory.json khai báo asset serial -> seed phải tạo."""
        import json

        inv = json.loads((REPO_DIR / "sample-data" / "inventory.json").read_text())
        assets = inv.get("assets") or []
        seed = (BACKEND_DIR / "app" / "seed.py").read_text()
        if "sample-data" in seed:
            pytest.skip("seed đọc sample-data -> kiểm tra riêng khi có dữ liệu")
        missing = [a["serial"] for a in assets if a.get("serial") not in seed]
        if missing:
            pytest.fail(
                f"seed.py không tạo asset serial {missing} theo sample-data/inventory.json "
                "-> B07 (I3) không có dữ liệu để kiểm thử.",
                pytrace=False,
            )

    def test_demo_password_is_not_printed_to_stdout(self):
        """docs/08: không đưa secret ra log; password (kể cả demo) không nên in ra console."""
        seed = (BACKEND_DIR / "app" / "seed.py").read_text()
        if "DEMO_PASSWORD" in seed and "print" in seed:
            lines = [
                ln.strip()
                for ln in seed.splitlines()
                if "print" in ln and "DEMO_PASSWORD" in ln
            ]
            if lines:
                pytest.fail(
                    "app/seed.py in mật khẩu demo ra stdout: " + "; ".join(lines)
                    + " -> rò rỉ secret vào log build/CI (docs/08: "
                    "'Không đưa token vào log'; nguyên tắc chung: không log secret).",
                    pytrace=False,
                )


    def test_sample_data_json_is_valid_and_shaped(self):
        """`sample-data/*.json` là input của seed -> phải parse được và đủ trường.

        Test xanh khi seed đã ăn được dữ liệu (hồi quy: ai sửa JSON sai shape
        thì seed vỡ theo).
        """
        import json
        import uuid

        data_dir = REPO_DIR / "sample-data"
        try:
            users = json.loads((data_dir / "users.json").read_text(encoding="utf-8"))
            inv = json.loads((data_dir / "inventory.json").read_text(encoding="utf-8"))
            cash = json.loads((data_dir / "cash-sim.json").read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            pytest.fail(f"sample-data JSON hỏng: {type(exc).__name__}: {exc}", pytrace=False)

        for u in users:
            for key in ("id", "username", "roles"):
                if key not in u:
                    pytest.fail(f"users.json thiếu trường `{key}` ở {u!r}", pytrace=False)
            try:
                uuid.UUID(str(u["id"]))
            except ValueError:
                pytest.fail(f"users.json id không phải UUID: {u['id']!r}", pytrace=False)
            if not u.get("roles"):
                pytest.fail(f"users.json user không có role: {u['username']!r}", pytrace=False)
        for key in ("locations", "items"):
            if key not in inv:
                pytest.fail(f"inventory.json thiếu `{key}`", pytrace=False)
        if "cashboxes" not in cash:
            pytest.fail("cash-sim.json thiếu `cashboxes`", pytrace=False)


# ------------------------------------------------------- boot / health (B01) --
class TestBoot:
    def test_app_module_imports(self):
        """App phải import được. Đây là cổng của mọi ca API còn lại."""
        from conftest import _import_or_fail

        _import_or_fail("app.main")

    def test_all_model_modules_import(self, models):
        assert models, "không model nào import được"

    def test_health_live_ok(self, client):
        r = client.get("/health/live")
        assert r.status_code == 200, r.text

    def test_health_ready_returns_5xx_when_db_down(self, client):
        """Readiness phải fail khi DB không sẵn sàng; 200 'degraded' sẽ qua healthcheck ảo."""
        r = client.get("/health/ready")
        assert r.status_code in (200, 503), r.text
        if r.status_code == 200:
            body = r.json()
            assert body.get("status") == "ready", (
                "GET /health/ready trả 200 với status "
                f"{body.get('status')!r} -> orchestrator thấy 'healthy' dù DB chết. "
                "docs/11: 'ready có kiểm tra DB'; docs/06: 503 dependency unavailable."
            )

    def test_ready_does_not_leak_internal_error_detail(self, client):
        r = client.get("/health/ready")
        if r.status_code != 200:
            pytest.skip("DB đang sẵn sàng nên không thấy nhánh lỗi")
        body = r.json()
        db = (body.get("services") or {}).get("database", "")
        assert not str(db).lower().startswith("not_ready:"), (
            f"/health/ready leak chi tiết lỗi nội bộ: {db!r} "
            "(docs/06: không trả raw exception)."
        )
