"""Inventory models."""
from sqlalchemy import (
    Boolean,
    CheckConstraint,
    Column,
    DateTime,
    ForeignKey,
    Integer,
    Numeric,
    String,
    UniqueConstraint,
)
from sqlalchemy.dialects.postgresql import UUID

from app.db.base import Base, TimestampMixin


class Location(Base, TimestampMixin):
    __tablename__ = "inv_locations"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    code: str = Column(String(64), unique=True, nullable=False, index=True)
    name: str = Column(String(256), nullable=False)
    active: bool = Column(Boolean, default=True, nullable=False)

    def __repr__(self):
        return f"<Location {self.code}>"


class Item(Base, TimestampMixin):
    __tablename__ = "inv_items"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    sku: str = Column(String(128), unique=True, nullable=False, index=True)
    name: str = Column(String(256), nullable=False)
    unit: str = Column(String(32), nullable=False)
    tracked_serial: bool = Column(Boolean, default=False, nullable=False)
    active: bool = Column(Boolean, default=True, nullable=False)

    def __repr__(self):
        return f"<Item {self.sku}>"


class Asset(Base, TimestampMixin):
    __tablename__ = "inv_assets"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    item_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_items.id", ondelete="RESTRICT"), nullable=False)
    serial: str = Column(String(128), unique=True, nullable=False, index=True)
    location_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_locations.id", ondelete="RESTRICT"), nullable=False)
    assignee_demo: str = Column(String(128), nullable=True)
    status: str = Column(String(32), nullable=False, default="available")

    def __repr__(self):
        return f"<Asset {self.serial}>"


class InventoryRequest(Base, TimestampMixin):
    __tablename__ = "inv_requests"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    kind: str = Column(String(32), nullable=False)  # receipt, issue, transfer, adjustment
    status: str = Column(String(32), nullable=False, default="draft")
    maker_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=False)
    approver_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=True)
    source_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_locations.id"), nullable=True)
    dest_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_locations.id"), nullable=True)
    reason: str = Column(String(512), nullable=True)
    version: int = Column(Integer, default=1, nullable=False)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    created_by: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=False)

    __mapper_args__ = {
        "version_id_col": version
    }

    def __repr__(self):
        return f"<InventoryRequest {self.kind} {self.status}>"


class InventoryRequestLine(Base):
    __tablename__ = "inv_request_lines"
    __table_args__ = (
        CheckConstraint("quantity > 0", name="qty_positive"),
    )

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    request_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_requests.id", ondelete="CASCADE"), nullable=False)
    item_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_items.id"), nullable=False)
    quantity = Column(Numeric(20, 3), nullable=False)
    serial: str = Column(String(128), nullable=True)

    def __repr__(self):
        return f"<InventoryRequestLine {self.request_id} qty={self.quantity}>"


class Movement(Base, TimestampMixin):
    __tablename__ = "inv_movements"
    __table_args__ = (
        UniqueConstraint("request_id", "line_id", "location_id", name="uq_movement_request_line_location"),
    )

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    request_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_requests.id"), nullable=False)
    line_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_request_lines.id"), nullable=False)
    item_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_items.id"), nullable=False)
    location_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_locations.id"), nullable=False)
    quantity_signed = Column(Numeric(20, 3), nullable=False)
    posted_at = Column(DateTime(timezone=True), nullable=True)

    def __repr__(self):
        return f"<Movement {self.quantity_signed}>"


class Balance(Base):
    __tablename__ = "inv_balances"
    __table_args__ = (
        CheckConstraint("quantity >= 0", name="qty_non_negative"),
    )

    location_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_locations.id"), primary_key=True)
    item_id: str = Column(UUID(as_uuid=True), ForeignKey("inv_items.id"), primary_key=True)
    quantity = Column(Numeric(20, 3), nullable=False, default=0)
    version: int = Column(Integer, default=1, nullable=False)

    def __repr__(self):
        return f"<Balance loc={self.location_id} item={self.item_id} qty={self.quantity}>"