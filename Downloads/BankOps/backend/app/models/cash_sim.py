"""Cash simulation models (ledger quỹ mô phỏng — docs/04)."""
from sqlalchemy import (
    Boolean,
    CheckConstraint,
    Column,
    DateTime,
    ForeignKey,
    Integer,
    Numeric,
    String,
)
from sqlalchemy.dialects.postgresql import UUID

from app.db.base import Base, TimestampMixin

# Account codes docs/04: SIM_CASH:{cashbox_id} và SIM_OFFSET:DEMO.
SIM_OFFSET_ACCOUNT = "SIM_OFFSET:DEMO"


def sim_cash_account(cashbox_id) -> str:
    return f"SIM_CASH:{cashbox_id}"


class CashBox(Base, TimestampMixin):
    __tablename__ = "cash_boxes"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    code: str = Column(String(64), unique=True, nullable=False, index=True)
    name: str = Column(String(256), nullable=False)
    currency: str = Column(String(8), default="VND", nullable=False)
    active: bool = Column(Boolean, default=True, nullable=False)

    def __repr__(self):
        return f"<CashBox {self.code}>"


class CashRequest(Base, TimestampMixin):
    __tablename__ = "cash_requests"
    __table_args__ = (
        CheckConstraint("amount > 0", name="amount_positive"),
    )

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    cashbox_id: str = Column(UUID(as_uuid=True), ForeignKey("cash_boxes.id"), nullable=False)
    kind: str = Column(String(32), nullable=False)  # receipt, payment, adjustment
    amount = Column(Numeric(20, 2), nullable=False)
    status: str = Column(String(32), nullable=False, default="draft")
    maker_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=False)
    approver_id: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=True)
    reason: str = Column(String(512), nullable=True)
    version: int = Column(Integer, default=1, nullable=False)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    created_by: str = Column(UUID(as_uuid=True), ForeignKey("iam_users.id"), nullable=False)

    __mapper_args__ = {
        "version_id_col": version
    }

    def __repr__(self):
        return f"<CashRequest {self.kind} {self.amount} {self.status}>"


class CashJournal(Base, TimestampMixin):
    __tablename__ = "cash_journals"

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    request_id: str = Column(UUID(as_uuid=True), ForeignKey("cash_requests.id"), unique=True, nullable=False)
    posted_at = Column(DateTime(timezone=True), nullable=True)
    reversal_of: str = Column(UUID(as_uuid=True), ForeignKey("cash_journals.id"), nullable=True)
    correlation_id: str = Column(String(64), nullable=False)

    def __repr__(self):
        return f"<CashJournal for request {self.request_id}>"


class CashEntry(Base):
    __tablename__ = "cash_entries"
    __table_args__ = (
        CheckConstraint("debit >= 0 AND credit >= 0", name="debit_credit_non_negative"),
        CheckConstraint(
            "(debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0)",
            name="exactly_one_positive",
        ),
    )

    id: str = Column(UUID(as_uuid=True), primary_key=True)
    journal_id: str = Column(UUID(as_uuid=True), ForeignKey("cash_journals.id"), nullable=False)
    account_code: str = Column(String(128), nullable=False)
    debit = Column(Numeric(20, 2), nullable=False, default=0)
    credit = Column(Numeric(20, 2), nullable=False, default=0)

    def __repr__(self):
        return f"<CashEntry {self.account_code} dr={self.debit} cr={self.credit}>"


class CashBalance(Base):
    __tablename__ = "cash_balances"
    __table_args__ = (
        CheckConstraint("amount >= 0", name="amount_non_negative"),
    )

    cashbox_id: str = Column(UUID(as_uuid=True), ForeignKey("cash_boxes.id"), primary_key=True)
    amount = Column(Numeric(20, 2), nullable=False, default=0)
    version: int = Column(Integer, default=1, nullable=False)

    def __repr__(self):
        return f"<CashBalance box={self.cashbox_id} amount={self.amount}>"