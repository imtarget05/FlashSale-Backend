"""Approval models."""
from sqlalchemy import Column, ForeignKey, String, Text, UniqueConstraint
from sqlalchemy.dialects.postgresql import UUID

from app.db.base import Base, TimestampMixin


class ApprovalDecision(Base, TimestampMixin):
    __tablename__ = "approval_decisions"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    subject_type: str = Column(String(64), nullable=False)
    subject_id: str = Column(UUID(as_uuid=True), nullable=False)
    actor_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=False)
    decision: str = Column(String(32), nullable=False)  # approved, rejected
    reason: str = Column(Text, nullable=True)
    request_payload_hash: str = Column(String(128), nullable=True)

    __table_args__ = (
        # docs/04: đúng một kết quả final mỗi subject (posted hoặc rejected).
        UniqueConstraint("subject_type", "subject_id", name="uq_approval_final_per_subject"),
    )

    def __repr__(self):
        return f"<ApprovalDecision {self.subject_type}:{self.subject_id} {self.decision}>"