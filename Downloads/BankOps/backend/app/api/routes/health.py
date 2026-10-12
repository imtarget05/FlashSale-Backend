"""Health check endpoints."""
from datetime import UTC, datetime

from fastapi import APIRouter
from fastapi.responses import JSONResponse
from pydantic import BaseModel


class HealthResponse(BaseModel):
    status: str
    timestamp: datetime
    version: str
    services: dict


router = APIRouter()


@router.get("/live")
async def liveness_check():
    """Liveness probe - checks if service is running."""
    return {
        "status": "alive",
        "timestamp": datetime.now(UTC),
        "version": "0.1.0",
    }


@router.get("/ready")
async def readiness_check():
    """Readiness probe - checks if service can handle requests.

    docs/06: DB down -> 503 (không phải 200 degraded) để orchestrator
    loại khỏi rotation.
    """
    from app.database import engine

    db_status = "unknown"
    try:
        from sqlalchemy import text

        async with engine.connect() as conn:
            await conn.execute(text("SELECT 1"))
            db_status = "ready"
    except Exception as e:
        db_status = f"not_ready: {str(e)}"

    body = {
        "status": "ready" if db_status == "ready" else "not_ready",
        "timestamp": datetime.now(UTC),
        "version": "0.1.0",
        "services": {
            "database": db_status,
        },
    }
    if db_status != "ready":
        body["timestamp"] = body["timestamp"].isoformat()
        return JSONResponse(status_code=503, content=body)
    return body