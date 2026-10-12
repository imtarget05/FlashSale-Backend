"""Activity, Audit & SSE stream routes (docs/06, docs/09, E1)."""
import json
import uuid
from collections.abc import AsyncGenerator
from datetime import UTC, datetime, timedelta

from fastapi import APIRouter, Depends, Header, Query
from fastapi.responses import StreamingResponse
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth import get_current_user
from app.api.routes.auth_utils import require_any_capability
from app.config import settings
from app.database import get_db
from app.models.activity import AuditEvent, Job, OutboxEvent

router = APIRouter()


def _sse_frame(event_type: str, payload: dict, event_id: str | None = None) -> str:
    prefix = f"id: {event_id}\n" if event_id else ""
    return f"{prefix}event: {event_type}\ndata: {json.dumps(payload, default=str)}\n\n"


async def _event_generator(
    db: AsyncSession, cursor: str | None = None
) -> AsyncGenerator[str, None]:
    """At-least-once SSE stream: replay outbox/jobs từ cursor hoặc phát resync_required."""
    cutoff = datetime.now(UTC) - timedelta(days=settings.sse_retention_days)
    anchor_time: datetime | None = None

    if cursor:
        raw = cursor.strip()
        found_anchor = False
        try:
            cid = uuid.UUID(raw)
            ev = (
                await db.execute(select(OutboxEvent).where(OutboxEvent.id == cid))
            ).scalar_one_or_none()
            if ev is not None and ev.created_at is not None:
                created = (
                    ev.created_at.replace(tzinfo=UTC)
                    if ev.created_at.tzinfo is None
                    else ev.created_at
                )
                if created >= cutoff:
                    anchor_time = created
                    found_anchor = True
        except ValueError:
            try:
                parsed = datetime.fromisoformat(raw)
                if parsed.tzinfo is None:
                    parsed = parsed.replace(tzinfo=UTC)
                if parsed >= cutoff:
                    anchor_time = parsed
                    found_anchor = True
            except ValueError:
                found_anchor = False

        if not found_anchor:
            yield _sse_frame(
                "resync_required",
                {
                    "event_id": uuid.uuid4().hex,
                    "occurred_at": datetime.now(UTC).isoformat(),
                    "reason": "cursor_expired_or_unknown",
                    "cursor": raw,
                    "message_key": "resync_required",
                },
            )
            yield _sse_frame("heartbeat", {"ts": datetime.now(UTC).isoformat()})
            return

    stmt = select(OutboxEvent).order_by(OutboxEvent.created_at)
    if anchor_time is not None:
        stmt = stmt.where(OutboxEvent.created_at > anchor_time)
    rows = (await db.execute(stmt.limit(100))).scalars().all()
    for r in rows:
        payload = r.payload_json if isinstance(r.payload_json, dict) else {}
        evt_name = "job.updated" if "job" in (r.event_type or "") else "activity.created"
        yield _sse_frame(
            evt_name,
            {
                "event_id": str(r.id),
                "occurred_at": r.created_at.isoformat() if r.created_at else None,
                "subject_id": str(r.aggregate_id),
                "status": payload.get("status", r.event_type),
                "processed": payload.get("processed"),
                "total": payload.get("total"),
                "message_key": r.event_type,
            },
            event_id=str(r.id),
        )

    jobs = (await db.execute(select(Job).order_by(Job.created_at.desc()).limit(10))).scalars().all()
    for j in jobs:
        yield _sse_frame(
            "job.updated",
            {
                "event_id": str(j.id),
                "occurred_at": j.created_at.isoformat() if j.created_at else None,
                "subject_id": str(j.id),
                "status": j.status,
                "processed": 1 if j.status == "succeeded" else 0,
                "total": 1,
                "message_key": f"job.{j.type}.{j.status}",
            },
            event_id=str(j.id),
        )

    yield _sse_frame("heartbeat", {"ts": datetime.now(UTC).isoformat()})


async def _serialize_activity(db: AsyncSession, cursor: str | None = None) -> dict:
    stmt = select(OutboxEvent).order_by(OutboxEvent.created_at.desc())
    rows = (await db.execute(stmt.limit(100))).scalars().all()
    events = []
    for r in rows:
        payload = r.payload_json if isinstance(r.payload_json, dict) else {}
        events.append(
            {
                "id": str(r.id),
                "event_id": str(r.id),
                "event_type": r.event_type,
                "occurred_at": r.created_at.isoformat() if r.created_at else None,
                "subject_id": str(r.aggregate_id),
                "status": payload.get("status", r.event_type),
                "message_key": r.event_type,
                "processed": payload.get("processed"),
                "total": payload.get("total"),
                "payload": payload,
            }
        )
    return {"events": events, "next_cursor": events[-1]["id"] if events else None}


@router.get("/activity")
async def get_activity_root(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
    cursor: str | None = Query(default=None),
):
    """Snapshot activity feed (docs/06, E1)."""
    return await _serialize_activity(db, cursor)


@router.get("/activity/")
async def get_activity_feed(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
    cursor: str | None = Query(default=None),
):
    """Get activity feed for current user (trailing-slash alias)."""
    return await _serialize_activity(db, cursor)


@router.get("/events/stream")
async def events_stream(
    db: AsyncSession = Depends(get_db),
    cursor: str | None = Query(default=None),
    last_event_id: str | None = Header(default=None, alias="Last-Event-ID"),
):
    """SSE stream chuẩn docs/06: GET /events/stream?cursor= + Last-Event-ID."""
    effective_cursor = cursor or last_event_id
    return StreamingResponse(
        _event_generator(db, effective_cursor),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )


@router.get("/activity/stream")
async def event_stream_alias(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
    cursor: str | None = Query(default=None),
    last_event_id: str | None = Header(default=None, alias="Last-Event-ID"),
):
    """SSE alias tại /activity/stream để giữ tương thích OpenAPI snapshot."""
    effective_cursor = cursor or last_event_id
    return StreamingResponse(
        _event_generator(db, effective_cursor),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )


@router.get("/audit-events")
async def list_audit_events(
    current_user=Depends(require_any_capability("audit:view", "admin:user_management")),
    db: AsyncSession = Depends(get_db),
    limit: int = 50,
    cursor: str | None = None,
):
    """Danh sách audit events append-only (docs/06: GET /audit-events)."""
    limit = max(1, min(limit, 100))
    rows = (
        await db.execute(
            select(AuditEvent).order_by(AuditEvent.occurred_at.desc()).limit(limit)
        )
    ).scalars().all()
    return {
        "audit_events": [
            {
                "id": str(a.id),
                "actor_id": str(a.actor_id) if a.actor_id else None,
                "action": a.action,
                "subject": a.subject,
                "before_hash": a.before_hash,
                "after_hash": a.after_hash,
                "occurred_at": a.occurred_at.isoformat() if a.occurred_at else None,
                "metadata": a.metadata_json or {},
            }
            for a in rows
        ],
        "limit": limit,
        "next_cursor": None,
    }