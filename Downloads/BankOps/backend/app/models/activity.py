"""Activity and audit models."""
from datetime import datetime

from sqlalchemy import (
    JSON,
    Column,
    DateTime,
    ForeignKey,
    Integer,
    String,
    event,
)
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.exc import DBAPIError

from app.db.base import Base, TimestampMixin, utc_now


class AuditEvent(Base):
    __tablename__ = "audit_events"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    actor_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=True)
    action: str = Column(String(128), nullable=False)
    subject: str = Column(String(128), nullable=False)
    before_hash: str = Column(String(128), nullable=True)
    after_hash: str = Column(String(128), nullable=True)
    request_id: str = Column(String(64), nullable=True)
    occurred_at: datetime = Column(DateTime(timezone=True), nullable=False, default=utc_now)
    metadata_json: dict = Column(JSON, nullable=True)

    def __repr__(self):
        return f"<AuditEvent {self.action} on {self.subject}>"


# --- Append-only guard (Invariant 5 docs/00: audit không UPDATE/DELETE) ------
def _reject_audit_mutation(connection, clause, operation):
    raise DBAPIError(
        clause,
        {"message": f"audit_events is append-only: {operation} forbidden"},
        Exception(f"audit_events is append-only: {operation} forbidden"),
    )


@event.listens_for(AuditEvent, "before_update", propagate=True)
def _block_audit_update(mapper, connection, target):  # noqa: ARG001
    _reject_audit_mutation(connection, None, "UPDATE")


@event.listens_for(AuditEvent, "before_delete", propagate=True)
def _block_audit_delete(mapper, connection, target):  # noqa: ARG001
    _reject_audit_mutation(connection, None, "DELETE")


class OutboxEvent(Base):
    __tablename__ = "outbox_events"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    event_type: str = Column(String(128), nullable=False)
    aggregate_id: str = Column(String(128), nullable=False)
    payload_json: dict = Column(JSON, nullable=False)
    created_at: datetime = Column(DateTime(timezone=True), nullable=False, default=utc_now)
    published_at: datetime = Column(DateTime(timezone=True), nullable=True)
    attempts: int = Column(Integer, default=0, nullable=False)

    def __repr__(self):
        return f"<OutboxEvent {self.event_type}>"


class Job(Base, TimestampMixin):
    __tablename__ = "jobs"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    type: str = Column(String(128), nullable=False)
    status: str = Column(String(32), nullable=False, default="queued")
    processed: int = Column(Integer, nullable=True)
    total: int = Column(Integer, nullable=True)
    error_code: str = Column(String(64), nullable=True)
    owner_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=True)
    finished_at: datetime = Column(DateTime(timezone=True), nullable=True)

    def __repr__(self):
        return f"<Job {self.type} {self.status}>"


class ActivityEvent(Base):
    __tablename__ = "activity_events"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    owner_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=True)
    type: str = Column(String(128), nullable=False)
    subject: str = Column(String(128), nullable=False)
    message_key: str = Column(String(256), nullable=False)
    payload: dict = Column(JSON, nullable=False)
    occurred_at: datetime = Column(DateTime(timezone=True), nullable=False, default=utc_now)
    outbox_event_id: str = Column(UUID(as_uuid=True), ForeignKey("outbox_events.id"), nullable=True)

    def __repr__(self):
        return f"<ActivityEvent {self.type}>"