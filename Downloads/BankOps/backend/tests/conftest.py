"""Shared fixtures for BankOps audit tests.

These tests are written by the independent QA reviewer. They must not import
`app.*` at module import time, so that collection always succeeds even when the
application itself is broken.
"""
from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

import pytest

BACKEND_DIR = Path(__file__).resolve().parents[1]
REPO_DIR = BACKEND_DIR.parent
APP_DIR = BACKEND_DIR / "app"

# Make `app` importable the same way uvicorn/alembic would.
sys.path.insert(0, str(BACKEND_DIR))

# A syntactically valid placeholder so `app.config.Settings()` can be built
# without a real database. Tests never open a connection through this URL.
os.environ.setdefault(
    "DATABASE_URL", "postgresql+psycopg2://bankops:bankops@127.0.0.1:5432/bankops"
)


def _import_or_fail(module: str):
    try:
        return __import__(module, fromlist=["*"])
    except BaseException as exc:  # noqa: BLE001 - we want every failure kind
        pytest.fail(
            f"Không import được `{module}`: {type(exc).__name__}: {exc}\n"
            f"-> Ứng dụng/khối đó không khởi động được, mọi ca kiểm thử phụ thuộc "
            f"vào nó đều không chạy được.",
            pytrace=False,
        )


@pytest.fixture(scope="session")
def app():
    """Import the FastAPI application, failing loudly if it cannot boot."""
    return _import_or_fail("app.main").app


@pytest.fixture(scope="session")
def client(app):
    """HTTP client against the real app. Needs a reachable database for /health/ready."""
    from fastapi.testclient import TestClient

    try:
        with TestClient(app) as c:
            yield c
    except BaseException as exc:  # noqa: BLE001
        pytest.fail(
            f"Không khởi động được ASGI app (startup hook): {type(exc).__name__}: {exc}",
            pytrace=False,
        )


@pytest.fixture(scope="session")
def models():
    """Import every SQLAlchemy model module, failing loudly on any broken module."""
    broken = []
    loaded = []
    for name in ("iam", "inventory", "cash_sim", "approval", "activity", "integration", "reconciliation"):
        try:
            loaded.append(__import__(f"app.models.{name}", fromlist=["*"]))
        except BaseException as exc:  # noqa: BLE001
            broken.append(f"  app.models.{name}: {type(exc).__name__}: {exc}")
    if broken:
        pytest.fail(
            "Một hoặc nhiều model module không import được:\n" + "\n".join(broken),
            pytrace=False,
        )
    return loaded


def run(cmd: list[str], cwd: Path | None = None, timeout: int = 300) -> subprocess.CompletedProcess:
    return subprocess.run(
        cmd, cwd=str(cwd or REPO_DIR), capture_output=True, text=True, timeout=timeout
    )
