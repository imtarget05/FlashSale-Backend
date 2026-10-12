"""Auth utilities and token management."""

import hashlib
import time
import uuid
from datetime import UTC, datetime, timedelta

from fastapi import Depends, HTTPException, status
from fastapi.security import OAuth2PasswordBearer
from jose import JWTError, jwt
from passlib.context import CryptContext
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.config import settings
from app.database import get_db
from app.models.iam import Capability, RoleCapability, User, UserRole

pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")
oauth2_scheme = OAuth2PasswordBearer(tokenUrl="/api/v1/auth/login")

# --- Token revocation (in-memory demo scope; thay Redis/DB khi scale) ---
_revoked_token_hashes: dict[str, float] = {}  # sha256(token) -> exp epoch


def _token_hash(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def revoke_token(token: str) -> None:
    """Đưa token vào denylist tới khi nó tự hết hạn (logout/refresh rotation)."""
    try:
        payload = jwt.decode(
            token, settings.signing_key, algorithms=[settings.jwt_algorithm],
            options={"verify_exp": False},
        )
        exp = float(payload.get("exp", 0))
    except JWTError:
        return
    _revoked_token_hashes[_token_hash(token)] = exp
    purge_expired_revocations()


def is_token_revoked(token: str) -> bool:
    return _token_hash(token) in _revoked_token_hashes


def purge_expired_revocations() -> None:
    now = time.time()
    for key in [k for k, exp in _revoked_token_hashes.items() if exp < now]:
        del _revoked_token_hashes[key]


# --- Login rate limiting (docs/08: 5 lần / 15 phút, khóa tạm khi brute force) ---
_login_failures: dict[str, list[float]] = {}


def _rate_key(username: str) -> str:
    return username.strip().lower()


def login_rate_limited(username: str) -> bool:
    window = settings.rate_limit_login_window_minutes * 60
    now = time.time()
    recent = [t for t in _login_failures.get(_rate_key(username), []) if now - t < window]
    _login_failures[_rate_key(username)] = recent
    return len(recent) >= settings.rate_limit_login_attempts


def record_login_failure(username: str) -> None:
    _login_failures.setdefault(_rate_key(username), []).append(time.time())


def reset_login_failures(username: str) -> None:
    _login_failures.pop(_rate_key(username), None)


def verify_password(plain: str, hashed: str) -> bool:
    return pwd_context.verify(plain, hashed)


def get_password_hash(password: str) -> str:
    return pwd_context.hash(password)


def create_access_token(data: dict, expires_delta: timedelta | None = None) -> str:
    to_encode = data.copy()
    # jwt.encode json-hóa payload; `sub` từ DB là UUID -> phải ép str trước.
    if isinstance(to_encode.get("sub"), uuid.UUID):
        to_encode["sub"] = str(to_encode["sub"])
    expire = datetime.now(UTC) + (expires_delta or timedelta(minutes=settings.access_token_expire_minutes))
    to_encode.update({"exp": expire, "type": "access"})
    return jwt.encode(to_encode, settings.signing_key, algorithm=settings.jwt_algorithm)


def create_refresh_token(data: dict, expires_delta: timedelta | None = None) -> str:
    to_encode = data.copy()
    if isinstance(to_encode.get("sub"), uuid.UUID):
        to_encode["sub"] = str(to_encode["sub"])
    expire = datetime.now(UTC) + (expires_delta or timedelta(days=settings.refresh_token_expire_days))
    to_encode.update({"exp": expire, "type": "refresh"})
    return jwt.encode(to_encode, settings.signing_key, algorithm=settings.jwt_algorithm)


async def get_user_by_username(db: AsyncSession, username: str) -> User | None:
    result = await db.execute(select(User).where(User.username == username))
    return result.scalar_one_or_none()


async def get_user_roles(db: AsyncSession, user_id: str) -> list[str]:
    result = await db.execute(
        select(UserRole.role_code)
        .where(UserRole.user_id == user_id)
    )
    return [row[0] for row in result.all()]


async def get_user_capabilities(db: AsyncSession, user_id: str) -> list[str]:
    result = await db.execute(
        select(Capability.code)
        .join(RoleCapability, RoleCapability.capability_code == Capability.code)
        .join(UserRole, UserRole.role_code == RoleCapability.role_code)
        .where(UserRole.user_id == user_id)
    )
    return [row[0] for row in result.all()]


async def authenticate_user(db: AsyncSession, username: str, password: str) -> User | None:
    user = await get_user_by_username(db, username)
    if not user:
        return None
    try:
        password_ok = verify_password(password, user.password_hash)
    except Exception:  # noqa: BLE001 - placeholder/corrupt hashes (e.g. OAuth users) must fail closed, never 500
        return None
    if not password_ok:
        return None
    if user.status != "active":
        return None
    return user


async def get_current_user(
    token: str = Depends(oauth2_scheme),
    db: AsyncSession = Depends(get_db),
    ) -> User:
    credentials_exception = HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Could not validate credentials",
        headers={"WWW-Authenticate": "Bearer"},
    )
    try:
        payload = jwt.decode(token, settings.signing_key, algorithms=[settings.jwt_algorithm])
        user_id: str = payload.get("sub")
        if user_id is None:
            raise credentials_exception
        if payload.get("type") != "access":
            # Refresh token (7 ngày) không được dùng như access token (30 phút).
            raise credentials_exception
    except JWTError:
        raise credentials_exception from None

    if is_token_revoked(token):
        raise credentials_exception

    result = await db.execute(select(User).where(User.id == user_id))
    user = result.scalar_one_or_none()
    if user is None:
        raise credentials_exception
    return user


async def get_current_user_with_capabilities(
    token: str = Depends(oauth2_scheme),
        db: AsyncSession = Depends(get_db),
) -> tuple[User, list[str]]:
    user = await get_current_user(token, db)
    capabilities = await get_user_capabilities(db, user.id)
    return user, capabilities


def require_capability(*required_caps: str):
    async def check(
        user_caps: tuple[User, list[str]] = Depends(get_current_user_with_capabilities),
    ):
        user, caps = user_caps
        for cap in required_caps:
            if cap not in caps:
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail=f"Missing capability: {cap}",
                )
        return user
    return check


def require_any_capability(*accepted_caps: str):
    """User cần ít nhất một capability trong danh sách (deny by default)."""
    async def check(
        user_caps: tuple[User, list[str]] = Depends(get_current_user_with_capabilities),
    ):
        user, caps = user_caps
        if not any(cap in caps for cap in accepted_caps):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Missing one of capabilities: {', '.join(accepted_caps)}",
            )
        return user
    return check