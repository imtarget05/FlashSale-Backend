"""Auth routes - demo authentication and session management."""
from datetime import timedelta

from fastapi import APIRouter, Depends, HTTPException, Request, status
from jose import JWTError, jwt
from pydantic import BaseModel
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth_utils import (
    authenticate_user,
    create_access_token,
    create_refresh_token,
    get_current_user,
    get_user_capabilities,
    get_user_roles,
    is_token_revoked,
    login_rate_limited,
    oauth2_scheme,
    record_login_failure,
    reset_login_failures,
    revoke_token,
)
from app.config import settings
from app.database import get_db
from app.models.iam import User

router = APIRouter()


@router.post("/login")
async def login(
    request: Request,
    db: AsyncSession = Depends(get_db),
):
    """Login with username and password (supports JSON or form-urlencoded)."""
    content_type = (request.headers.get("content-type") or "").lower()
    username: str | None = None
    password: str | None = None
    if "application/json" in content_type:
        try:
            body = await request.json()
            if isinstance(body, dict):
                username = body.get("username")
                password = body.get("password")
        except Exception:
            pass
    else:
        try:
            form = await request.form()
            username = form.get("username")  # type: ignore[assignment]
            password = form.get("password")  # type: ignore[assignment]
        except Exception:
            pass

    if not username or not password:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="username and password are required",
        )

    # Rate limit login (docs/08: chặn brute force, khóa tạm theo username).
    if login_rate_limited(username):
        raise HTTPException(
            status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            detail="Too many failed login attempts, try again later.",
            headers={"Retry-After": str(settings.rate_limit_login_window_minutes * 60)},
        )

    user = await authenticate_user(db, username, password)
    if not user:
        record_login_failure(username)
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Incorrect username or password",
            headers={"WWW-Authenticate": "Bearer"},
        )

    reset_login_failures(username)
    roles = await get_user_roles(db, user.id)
    capabilities = await get_user_capabilities(db, user.id)

    access_token_expires = timedelta(minutes=30)
    refresh_token_expires = timedelta(days=7)

    access_token = create_access_token(
        {"sub": str(user.id), "username": user.username, "roles": roles, "caps": capabilities},
        access_token_expires,
    )
    refresh_token = create_refresh_token(
        {"sub": str(user.id), "username": user.username},
        refresh_token_expires,
    )

    return {
        "access_token": access_token,
        "refresh_token": refresh_token,
        "token_type": "bearer",
        "expires_in": int(access_token_expires.total_seconds()),
        "user": {
            "id": str(user.id),
            "username": user.username,
            "display_name": user.display_name_demo,
            "roles": roles,
            "capabilities": capabilities,
        },
    }


class RefreshIn(BaseModel):
    refresh_token: str


@router.post("/refresh")
async def refresh_tokens(
    body: RefreshIn,
    db: AsyncSession = Depends(get_db),
):
    """Đổi refresh token cũ lấy cặp mới (rotation, docs/08).

    Refresh token đã dùng hoặc đã thu hồi bị từ chối — chống replay.
    """
    invalid = HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Invalid or expired refresh token",
        headers={"WWW-Authenticate": "Bearer"},
    )
    try:
        payload = jwt.decode(
            body.refresh_token, settings.signing_key, algorithms=[settings.jwt_algorithm]
        )
    except JWTError:
        raise invalid from None
    if payload.get("type") != "refresh":
        raise invalid
    if is_token_revoked(body.refresh_token):
        raise invalid

    user = await db.get(User, payload.get("sub", ""))
    if user is None or user.status != "active":
        raise invalid

    # Rotation: refresh token vừa dùng bị revoke, không tái sử dụng.
    revoke_token(body.refresh_token)

    roles = await get_user_roles(db, user.id)
    capabilities = await get_user_capabilities(db, user.id)
    access_token = create_access_token(
        {"sub": str(user.id), "username": user.username, "roles": roles, "caps": capabilities},
        timedelta(minutes=30),
    )
    new_refresh = create_refresh_token(
        {"sub": str(user.id), "username": user.username}, timedelta(days=7)
    )
    return {
        "access_token": access_token,
        "refresh_token": new_refresh,
        "token_type": "bearer",
        "expires_in": 1800,
    }


@router.post("/logout")
async def logout(token: str = Depends(oauth2_scheme)):
    """Logout: revoke access token hiện tại (denylist tới khi token hết hạn)."""
    revoke_token(token)
    return {"message": "Logged out successfully"}


@router.get("/me")
async def get_current_user_info(
    current_user = Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    """Get current user info including roles and capabilities."""
    roles = await get_user_roles(db, current_user.id)
    capabilities = await get_user_capabilities(db, current_user.id)

    return {
        "id": str(current_user.id),
        "username": current_user.username,
        "display_name": current_user.display_name_demo,
        "roles": roles,
        "capabilities": capabilities,
    }
