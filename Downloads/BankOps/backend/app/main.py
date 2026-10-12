"""BankOps API Application.

A modular FastAPI monolith for inventory management and cash simulation.
All data is simulated - no real banking integration.
"""
import uuid
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import APIRouter, FastAPI, Request
from fastapi.exceptions import HTTPException, RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from app.api.errors import register_error_handlers
from app.api.middleware.correlation import CorrelationIdMiddleware
from app.api.openapi import custom_openapi
from app.api.routes import (
    activity,
    approvals,
    auth,
    auth_google,
    cash_sim,
    events,
    health,
    integrations,
    inventory,
    reconciliations,
    reports,
)
from app.config import settings
from app.database import init_db


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    """Initialize database on startup (lifespan thay on_event deprecated)."""
    await init_db()
    yield


app = FastAPI(
    title="BankOps API",
    description="Inventory and Cash Simulation System (Demo)",
    version="0.1.0",
    openapi_url="/api/v1/openapi.json",
    docs_url="/api/v1/docs",
    root_path="/api/v1",
    lifespan=lifespan,
)

app.openapi = lambda: custom_openapi(app)

# Không echo wildcard origin kèm credentials (docs/08: CORS cụ thể).
_allow_credentials = bool(settings.allowed_origins) and settings.allowed_origins != ["*"]
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.allowed_origins,
    allow_credentials=_allow_credentials,
    allow_methods=["*"],
    allow_headers=["*"],
    expose_headers=["X-Request-ID", "X-Correlation-ID"],
)
app.add_middleware(CorrelationIdMiddleware)

# --- Error envelope docs/06:44 -----------------------------------------------
_ERROR_CODES = {
    400: "BAD_REQUEST",
    401: "UNAUTHENTICATED",
    403: "FORBIDDEN",
    404: "NOT_FOUND",
    409: "CONFLICT",
    422: "VALIDATION_ERROR",
    429: "RATE_LIMITED",
    503: "DEPENDENCY_UNAVAILABLE",
}


def _request_id(request: Request) -> str:
    rid = getattr(request.state, "request_id", None)
    if rid:
        return rid
    return request.headers.get("x-request-id") or uuid.uuid4().hex


@app.middleware("http")
async def request_id_mw(request: Request, call_next):
    """docs/06:3 — propagate/generate X-Request-ID, envelope HTTP errors."""
    rid = request.headers.get("x-request-id") or uuid.uuid4().hex
    request.state.request_id = rid
    try:
        resp = await call_next(request)
    except HTTPException as e:
        code = _ERROR_CODES.get(e.status_code, f"HTTP_{e.status_code}")
        message = e.detail if isinstance(e.detail, str) else "Request failed"
        return JSONResponse(
            status_code=e.status_code,
            content={"error": {"code": code, "message": message, "request_id": rid, "details": {}}},
            headers={"X-Request-ID": rid},
        )
    resp.headers["X-Request-ID"] = rid
    return resp


@app.exception_handler(HTTPException)
async def http_exception_envelope(request: Request, exc: HTTPException):
    """Mọi HTTPException trả theo envelope docs/06:44, không phải {"detail": ...}."""
    code = _ERROR_CODES.get(exc.status_code, f"HTTP_{exc.status_code}")
    message = exc.detail if isinstance(exc.detail, str) else "Request failed"
    rid = _request_id(request)
    headers = dict(exc.headers or {})
    headers.setdefault("X-Request-ID", rid)
    return JSONResponse(
        status_code=exc.status_code,
        content={
            "error": {
                "code": code,
                "message": message,
                "request_id": rid,
                "details": {},
            }
        },
        headers=headers,
    )


@app.exception_handler(RequestValidationError)
async def validation_exception_envelope(request: Request, exc: RequestValidationError):
    """422 theo cùng envelope docs/06:44 (không phải {"detail": ...})."""
    rid = _request_id(request)
    return JSONResponse(
        status_code=422,
        content={
            "error": {
                "code": _ERROR_CODES[422],
                "message": "Validation failed",
                "request_id": rid,
                "details": {"errors": exc.errors()},
            }
        },
        headers={"X-Request-ID": rid},
    )

@app.exception_handler(Exception)
async def unhandled_exception_envelope(request: Request, exc: Exception):
    """500 theo cùng envelope docs/06:44."""
    rid = getattr(request.state, "request_id", None) or _request_id(request)
    return JSONResponse(
        status_code=500,
        content={
            "error": {
                "code": "INTERNAL_ERROR",
                "message": "Internal server error",
                "request_id": rid,
                "details": {},
            }
        },
        headers={"X-Request-ID": rid},
    )


# Lỗi nghiệp vụ có code riêng (AppError, IdempotencyConflict) -> envelope docs/06.
register_error_handlers(app)

def _mount_routes(target: FastAPI | APIRouter, *, include_in_schema: bool = True) -> None:
    target.include_router(health.router, prefix="/health", tags=["health"], include_in_schema=include_in_schema)
    target.include_router(auth.router, prefix="/auth", tags=["auth"], include_in_schema=include_in_schema)
    target.include_router(auth_google.router, prefix="/auth", tags=["auth"], include_in_schema=include_in_schema)
    target.include_router(inventory.router, tags=["inventory"], include_in_schema=include_in_schema)
    target.include_router(cash_sim.router, tags=["cash-sim"], include_in_schema=include_in_schema)
    target.include_router(approvals.router, prefix="/approvals", tags=["approvals"], include_in_schema=include_in_schema)
    target.include_router(activity.router, tags=["activity"], include_in_schema=include_in_schema)
    target.include_router(events.router, tags=["events"], include_in_schema=include_in_schema)
    target.include_router(reports.router, prefix="/reports", tags=["reports"], include_in_schema=include_in_schema)
    target.include_router(integrations.router, tags=["integrations"], include_in_schema=include_in_schema)
    target.include_router(reconciliations.router, tags=["reconciliations"], include_in_schema=include_in_schema)
    # docs/06:9 khai báo `GET /me` (không qua /auth) — alias tới cùng handler.
    target.add_api_route("/me", auth.get_current_user_info, methods=["GET"], tags=["auth"], include_in_schema=include_in_schema)


_mount_routes(app, include_in_schema=True)

api_v1_router = APIRouter(prefix="/api/v1")
_mount_routes(api_v1_router, include_in_schema=False)
app.include_router(api_v1_router, include_in_schema=False)