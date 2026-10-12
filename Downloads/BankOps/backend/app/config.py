"""Application configuration."""
import os
import secrets

from pydantic import Field
from pydantic_settings import BaseSettings

# Secret ephemeral per-process cho môi trường demo/test: không hardcode hằng
# công khai, không ký JWT bằng key rỗng, và không thể forge từ bên ngoài
# (không chia sẻ process). Sản xuất bắt buộc set env (xem `signing_key`).
_ephemeral_secrets: dict[str, str] = {}


def _ephemeral_secret(name: str) -> str:
    if name not in _ephemeral_secrets:
        _ephemeral_secrets[name] = secrets.token_urlsafe(48)
    return _ephemeral_secrets[name]


class Settings(BaseSettings):
    app_env: str = Field(default="demo")
    app_name: str = Field(default="BankOps-API")

    database_url: str = Field(...)

    jwt_signing_key_ref: str = Field(default="signing")
    jwt_algorithm: str = Field(default="HS256")
    access_token_expire_minutes: int = Field(default=30)
    refresh_token_expire_days: int = Field(default=7)

    allowed_origins: list[str] = Field(
        default=[
            "http://localhost:3000",
            "http://localhost:5000",
            "http://localhost:8080",
            "http://127.0.0.1:3000",
            "http://127.0.0.1:5000",
            "http://127.0.0.1:8080",
        ]
    )

    idempotency_key_retention_hours: int = Field(default=24)

    server_host: str = Field(default="0.0.0.0")
    server_port: int = Field(default=8000)
    server_reload: bool = Field(default=True)

    connector_secret_ref: str = Field(default="")
    sse_retention_days: int = Field(default=7)
    staging_ttl_hours: int = Field(default=24)
    sheets_timeout_seconds: int = Field(default=15)

    # Integration Hub (docs/09): secret cho Apps Script webhook + Sheet sandbox.
    google_sheets_api_key: str = Field(default="")
    apps_script_webhook_secret: str = Field(default="")

    rate_limit_login_attempts: int = Field(default=5)
    rate_limit_login_window_minutes: int = Field(default=15)

    # Google Sign-In: OAuth client IDs are public identifiers (safe in code).
    # The web client *secret* must never be committed; it lives only in server
    # env for a future authorization-code exchange flow (not needed for the
    # ID-token flow used by the mobile app).
    google_allowed_audiences: list[str] = Field(
        default=[
            # iOS client (GoogleService-Info.plist CLIENT_ID)
            "1070073470852-apcl0c8sgvt9c4f6qscpjgcrgtje4tjn.apps.googleusercontent.com",
            # Web client (used by some Android configurations as audience)
            "1070073470852-hc12cgovbt9h7micnu072vpkvigee28u.apps.googleusercontent.com",
        ]
    )
    google_client_id: str = Field(
        default="1070073470852-apcl0c8sgvt9c4f6qscpjgcrgtje4tjn.apps.googleusercontent.com"
    )

    @property
    def is_demo(self) -> bool:
        return self.app_env == "demo"

    @property
    def session_secret(self) -> str:
        """Env trước; demo/test fallback secret ephemeral (không hardcode)."""
        env_value = os.environ.get("BANKOPS_SESSION_SECRET", "")
        if env_value:
            return env_value
        if self.app_env in ("demo", "test"):
            return _ephemeral_secret("session")
        raise RuntimeError(
            "BANKOPS_SESSION_SECRET must be set outside demo environments"
        )

    @property
    def signing_key(self) -> str:
        key = os.environ.get("BANKOPS_SIGNING_KEY", "") or os.environ.get(
            f"BANKOPS_{self.jwt_signing_key_ref}", ""
        )
        if key:
            return key
        if self.app_env in ("demo", "test"):
            return _ephemeral_secret("jwt-signing")
        raise RuntimeError(
            "JWT signing key must be set via BANKOPS_SIGNING_KEY environment variable"
        )


settings = Settings(
    database_url=os.environ.get("DATABASE_URL", "postgresql+psycopg2://bankops:bankops@localhost:5432/bankops"),
)
