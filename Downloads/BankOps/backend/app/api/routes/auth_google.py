"""Google Sign-In: verify ID token, find-or-create user, issue BankOps JWTs.

Flow (Flutter `google_sign_in` -> ID token -> this endpoint):
  1. `POST /auth/google {"id_token": "..."}`
  2. Verify signature/expiry/issuer/audience with Google certs (`google-auth`).
  3. Require `email_verified=true`.
  4. Find-or-create user by email (= username): first sight = signup
     (role `staff`), later = login.
  5. Issue BankOps access (30 min) + refresh (7 days) JWTs, same shape as
     password login. `sub` is always `str(user.id)` (UUID is not JSON
     serializable for `jwt.encode`).

Errors follow the docs/06:44 envelope directly (this router does not rely on
the app-wide handler, which does not exist yet).

Secrets: only the OAuth *client IDs* (public identifiers) live here/in config.
The web *client secret* is never needed for the ID-token flow and must never
be committed — it stays in server env for a future code-exchange flow.
"""
from __future__ import annotations

import secrets
import uuid
from datetime import timedelta

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth_utils import (
    create_access_token,
    create_refresh_token,
    get_user_by_username,
    get_user_capabilities,
    get_user_roles,
)
from app.config import settings
from app.database import get_db
from app.models.iam import Role, User, UserRole, UserStatus

router = APIRouter()

GOOGLE_ISSUERS = ("accounts.google.com", "https://accounts.google.com")


class GoogleLoginIn(BaseModel):
    id_token: str = Field(min_length=10, description="Google OpenID Connect ID token")


def error_envelope(code: str, message: str, http_status: int, details: dict | None = None) -> JSONResponse:
    return JSONResponse(
        status_code=http_status,
        content={
            "error": {
                "code": code,
                "message": message,
                "request_id": uuid.uuid4().hex,
                "details": details or {},
            }
        },
    )


def verify_google_id_token(id_token: str) -> dict:
    """Verify signature/expiry/issuer/audience. Raises ValueError when invalid."""
    from google.auth.transport import requests as google_requests
    from google.oauth2 import id_token as google_id_token

    last_error: Exception | None = None
    for audience in settings.google_allowed_audiences:
        try:
            return google_id_token.verify_oauth2_token(
                id_token, google_requests.Request(), audience
            )
        except ValueError as exc:
            last_error = exc
    raise ValueError(f"Google ID token verification failed: {last_error}")


@router.post("/google")
async def login_with_google(
    body: GoogleLoginIn,
    db: AsyncSession = Depends(get_db),
):
    """Login or signup with a Google ID token (upsert by email)."""
    try:
        info = verify_google_id_token(body.id_token)
    except ValueError:
        return error_envelope(
            "GOOGLE_TOKEN_INVALID",
            "Google ID token không hợp lệ hoặc hết hạn.",
            401,
        )
    except Exception:  # noqa: BLE001 - network/cert failure must not 500 raw
        return error_envelope(
            "GOOGLE_VERIFICATION_UNAVAILABLE",
            "Không xác thực được với Google lúc này, thử lại sau.",
            503,
        )

    if info.get("iss") not in GOOGLE_ISSUERS:
        return error_envelope("GOOGLE_TOKEN_INVALID", "Issuer không phải Google.", 401)
    email = (info.get("email") or "").strip().lower()
    if not email:
        return error_envelope("GOOGLE_EMAIL_MISSING", "ID token thiếu email.", 401)
    if not info.get("email_verified"):
        return error_envelope(
            "GOOGLE_EMAIL_UNVERIFIED",
            "Email Google chưa được xác minh.",
            401,
            {"email": email},
        )

    user = await get_user_by_username(db, email)
    is_new_user = user is None
    if is_new_user:
        user = User(
            id=uuid.uuid4(),
            username=email,
            # Unusable placeholder: native password login must never succeed
            # for OAuth users (verify raises -> authenticate_user returns None).
            password_hash="GOOGLE_OAUTH_NO_PASSWORD:" + secrets.token_hex(32),
            status=UserStatus.active,
            display_name_demo=info.get("name") or email,
        )
        db.add(user)
        await db.flush()
        role = (
            await db.execute(select(Role).where(Role.code == "staff"))
        ).scalar_one_or_none()
        if role is not None:
            db.add(UserRole(user_id=user.id, role_code="staff"))
        await db.commit()

    roles = await get_user_roles(db, user.id)
    capabilities = await get_user_capabilities(db, user.id)
    access_expires = timedelta(minutes=30)
    access_token = create_access_token(
        {"sub": str(user.id), "username": user.username, "roles": roles, "caps": capabilities},
        access_expires,
    )
    refresh_token = create_refresh_token(
        {"sub": str(user.id), "username": user.username},
        timedelta(days=7),
    )
    return {
        "access_token": access_token,
        "refresh_token": refresh_token,
        "token_type": "bearer",
        "expires_in": int(access_expires.total_seconds()),
        "is_new_user": is_new_user,
        "user": {
            "id": str(user.id),
            "username": user.username,
            "display_name": user.display_name_demo,
            "roles": roles,
            "capabilities": capabilities,
        },
    }
