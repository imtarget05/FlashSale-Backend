"""Lỗi nghiệp vụ có mã code riêng — render theo envelope docs/06:44.

Mọi route nghiệp vụ (inventory, cash-sim, integration) raise `AppError` thay vì
trả `JSONResponse` tự chắp vá: một chỗ duy nhất quyết định hình dạng body lỗi,
nên `error.code` luôn khớp hợp đồng và test `assert_error_code` trong
tests/test_acceptance_qa.py.

Đăng ký handler trong `app/main.py` qua `register_error_handlers(app)`.
"""
from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm.exc import StaleDataError

from app.services.idempotency import IdempotencyConflict


class AppError(Exception):
    """Lỗi nghiệp vụ: HTTP status + mã code ổn định cho client (docs/06)."""

    def __init__(
        self,
        http_status: int,
        code: str,
        message: str,
        details: dict | None = None,
    ) -> None:
        self.http_status = http_status
        self.code = code
        self.message = message
        self.details = details or {}
        super().__init__(message)


def _envelope(code: str, message: str, request: Request, details: dict) -> dict:
    request_id = getattr(request.state, "request_id", None) or uuid_hex()
    return {
        "error": {
            "code": code,
            "message": message,
            "request_id": request_id,
            "details": details,
        }
    }


def uuid_hex() -> str:
    import uuid

    return uuid.uuid4().hex


async def app_error_handler(request: Request, exc: AppError) -> JSONResponse:
    return JSONResponse(
        status_code=exc.http_status,
        content=_envelope(exc.code, exc.message, request, exc.details),
        headers={"X-Request-ID": getattr(request.state, "request_id", "") or ""},
    )


async def idempotency_conflict_handler(
    request: Request, exc: IdempotencyConflict
) -> JSONResponse:
    """A4 (docs/10): cùng Idempotency-Key nhưng payload khác -> 409, không ghi thêm."""
    return JSONResponse(
        status_code=409,
        content=_envelope(
            "IDEMPOTENCY_KEY_REUSED",
            "Idempotency-Key đã được dùng cho một payload khác",
            request,
            {"previous_status": exc.existing_status},
        ),
        headers={"X-Request-ID": getattr(request.state, "request_id", "") or ""},
    )


def register_error_handlers(app: FastAPI) -> None:
    app.add_exception_handler(AppError, app_error_handler)
    app.add_exception_handler(IdempotencyConflict, idempotency_conflict_handler)


@asynccontextmanager
async def write_tx(db):
    """Một transaction cho cả nghiệp vụ + audit + outbox (docs/00 Invariant 5).

    Rollback ở MỌI lỗi — kể cả `IntegrityError` do unique constraint — nên
    thất bại ở bất kỳ bước nào cũng không để lại nửa vời post/movement (A2, I1).
    `StaleDataError` (version optimistic lock bị ghi đè) trở thành
    409 VERSION_CONFLICT thay vì 500: hai checker cùng duyệt thì người sau phải
    nhận mã rõ ràng (A2).
    """
    try:
        yield db
    except StaleDataError as exc:
        await db.rollback()
        raise AppError(
            409,
            "VERSION_CONFLICT",
            "Bản ghi đã bị thay đổi bởi giao dịch khác (optimistic lock)",
            {"cause": type(exc).__name__},
        ) from exc
    except IntegrityError as exc:
        await db.rollback()
        raise AppError(
            409,
            "CONSTRAINT_VIOLATION",
            "Dữ liệu vi phạm ràng buộc toàn vẹn",
            {"cause": type(exc).__name__},
        ) from exc
    except AppError:
        await db.rollback()
        raise
    except Exception:
        await db.rollback()
        raise
