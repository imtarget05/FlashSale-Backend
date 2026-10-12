"""Cash simulation routes - real double-entry ledger (docs/04, B08)."""
import uuid
from datetime import UTC, datetime
from decimal import Decimal, InvalidOperation
from typing import Any

from fastapi import APIRouter, Depends, Request
from sqlalchemy import func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.errors import AppError
from app.api.routes.auth import get_current_user
from app.database import get_db
from app.models.activity import AuditEvent, OutboxEvent
from app.models.cash_sim import (
    CashBalance,
    CashBox,
    CashEntry,
    CashJournal,
    CashRequest,
)
from app.services import idempotency

router = APIRouter()

_ZERO = Decimal("0")
_SIM_CASH_PREFIX = "SIM_CASH:"
_SIM_OFFSET = "SIM_OFFSET:DEMO"
_SUBJECT = "cash_request"


def _now() -> datetime:
    return datetime.now(UTC)


def _cashbox_id(payload: dict[str, Any]) -> uuid.UUID | None:
    if payload.get("cashbox_id"):
        try:
            return uuid.UUID(str(payload["cashbox_id"]))
        except (ValueError, AttributeError, TypeError):
            pass
    return None


async def _balance_for_box(db: AsyncSession, cashbox_id: uuid.UUID):
    rows = (
        await db.execute(
            select(CashBalance).where(CashBalance.cashbox_id == cashbox_id)
        )
    ).scalars().all()
    return rows[0] if rows else None


async def _load_request(db: AsyncSession, request_id: uuid.UUID):
    return (await db.execute(select(CashRequest).where(CashRequest.id == request_id))).scalar_one_or_none()


async def _user_capabilities(db: AsyncSession, user_id: str) -> set[str]:
    from app.models.iam import RoleCapability, UserRole

    rows = (
        await db.execute(
            select(RoleCapability.capability_code)
            .join(UserRole, UserRole.role_code == RoleCapability.role_code)
            .where(UserRole.user_id == user_id)
        )
    ).scalars().all()
    return set(rows) if rows else set()


def _check_if_match(request: Request, expected_version: int) -> None:
    raw = request.headers.get("if-match")
    if raw is None:
        return
    expected = raw.strip().strip('"')
    if expected.isdigit() and int(expected) != expected_version:
        raise AppError(
            409,
            "VERSION_CONFLICT",
            f"If-Match={expected} but version is {expected_version}",
            {"current_version": expected_version},
        )


def _require_key(request: Request) -> str:
    key = request.headers.get("idempotency-key")
    if not key or not key.strip():
        raise AppError(
            400,
            "IDEMPOTENCY_KEY_REQUIRED",
            "Header Idempotency-Key là bắt buộc với mọi thao tác ghi (docs/06)",
        )
    return key.strip()


def _cash_entry(
    journal_id: uuid.UUID, account: str, debit: Decimal, credit: Decimal
) -> CashEntry:
    return CashEntry(
        id=uuid.uuid4(),
        journal_id=journal_id,
        account_code=account,
        debit=debit,
        credit=credit,
    )


# --------------------------------------------------------------------------
# Reads
# --------------------------------------------------------------------------

@router.get("/cash-sim/boxes")
async def list_cashboxes(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    """Danh sách cash boxes (read-only)."""
    boxes = (await db.execute(select(CashBox))).scalars().all()
    return {
        "simulation": True,
        "currency": "VND",
        "boxes": [
            {
                "id": str(b.id),
                "code": b.code,
                "name": b.name,
                "currency": b.currency,
                "active": b.active,
            }
            for b in boxes
        ],
    }


@router.get("/cash-sim/balances")
async def list_cash_balances(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    """Tồn quỹ của từng cash box - read model, nguồn sự thật là journals."""
    rows = (await db.execute(select(CashBalance))).scalars().all()
    return {
        "simulation": True,
        "currency": "VND",
        "balances": [
            {
                "cashbox_id": str(b.cashbox_id),
                "amount": str(b.amount),
                "version": b.version,
                "currency": "VND",
                "simulation": True,
            }
            for b in rows
        ],
    }


@router.get("/cash-sim/journals")
async def list_journals(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
    request_id: str | None = None,
    limit: int = 100,
    offset: int = 0,
):
    stmt = select(CashJournal).order_by(CashJournal.posted_at.desc())
    if request_id:
        stmt = stmt.where(CashJournal.request_id == uuid.UUID(request_id))
    total = (
        await db.execute(select(func.count()).select_from(stmt.subquery()))
    ).scalar_one()
    rows = (await db.execute(stmt.limit(limit).offset(offset))).scalars().all()
    req_map = {
        r.id: r
        for r in (await db.execute(select(CashRequest))).scalars().all()
    }
    return {
        "simulation": True,
        "currency": "VND",
        "journals": [
            {
                "id": str(j.id),
                "request_id": str(j.request_id),
                "cashbox_id": str(req_map[j.request_id].cashbox_id)
                if j.request_id in req_map
                else "",
                "kind": req_map[j.request_id].kind
                if j.request_id in req_map
                else "receipt",
                "amount": str(req_map[j.request_id].amount)
                if j.request_id in req_map
                else "0",
                "currency": "VND",
                "reason": (req_map[j.request_id].reason or j.correlation_id)
                if j.request_id in req_map
                else j.correlation_id,
                "posted_at": j.posted_at.isoformat() if j.posted_at else None,
                "reversal_of": str(j.reversal_of) if j.reversal_of else None,
                "correlation_id": j.correlation_id,
                "entries": [
                    {
                        "id": str(e.id),
                        "account_code": e.account_code,
                        "debit": str(e.debit),
                        "credit": str(e.credit),
                    }
                    for e in (
                        await db.execute(
                            select(CashEntry).where(CashEntry.journal_id == j.id)
                        )
                    )
                    .scalars()
                    .all()
                ],
            }
            for j in rows
        ],
        "total": total,
        "limit": limit,
        "offset": offset,
    }


def _cash_req_out(r: CashRequest, extra: dict | None = None) -> dict:
    body = {
        "id": str(r.id),
        "cashbox_id": str(r.cashbox_id),
        "kind": r.kind,
        "amount": str(r.amount),
        "status": r.status,
        "version": r.version,
        "maker_id": str(r.maker_id),
        "approver_id": str(r.approver_id) if r.approver_id else None,
        "reason": r.reason,
        "created_at": r.created_at.isoformat() if r.created_at else None,
        "simulation": True,
        "currency": "VND",
    }
    if extra:
        body.update(extra)
    return body


@router.get("/cash-sim/requests")
async def list_cash_requests(
    current_user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
    limit: int = 100,
):
    rows = (
        await db.execute(
            select(CashRequest)
            .order_by(CashRequest.created_at.desc())
            .limit(max(1, min(limit, 100)))
        )
    ).scalars().all()
    return {
        "simulation": True,
        "currency": "VND",
        "requests": [_cash_req_out(r) for r in rows],
    }


# --------------------------------------------------------------------------
# Creates (draft)
# --------------------------------------------------------------------------

@router.post("/cash-sim/requests", status_code=201)
async def create_cash_request(
    payload: dict[str, Any],
    request: Request,
    user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    """Tạo phiếu cash draft - maker phải có capability cash:create_request (docs/06)."""
    key = _require_key(request)
    caps = await _user_capabilities(db, user.id)
    if "cash:create_request" not in caps:
        raise AppError(403, "FORBIDDEN", "missing capability: cash:create_request")

    try:
        amount = Decimal(str(payload.get("amount", "0")))
    except (InvalidOperation, ValueError):
        raise AppError(422, "INVALID_AMOUNT", "amount không hợp lệ") from None

    cashbox_id = _cashbox_id(payload)
    if amount <= _ZERO:
        raise AppError(422, "INVALID_AMOUNT", f"amount phải > 0, nhận {amount}")
    if cashbox_id is None:
        raise AppError(422, "CASHBOX_NOT_FOUND", "cashbox_id không tồn tại")

    replay = idempotency.check(key, idempotency.payload_hash(payload))
    if replay is not None:
        return replay[1]

    req = CashRequest(
        id=uuid.uuid4(),
        cashbox_id=cashbox_id,
        kind=str(payload.get("kind", "payment")),
        amount=amount,
        status="draft",
        maker_id=user.id,
        reason=payload.get("reason"),
        version=1,
        created_by=user.id,
    )
    db.add(req)
    await db.commit()
    await db.refresh(req)
    body = _cash_req_out(req)
    idempotency.remember(db, key, idempotency.payload_hash(payload), 201, body)
    return body


# --------------------------------------------------------------------------
# Submit
# --------------------------------------------------------------------------

@router.post("/cash-sim/requests/{request_id}/submit")
async def submit_cash_request(
    request_id: str,
    request: Request,
    user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, uuid.UUID(request_id))
    if req is None:
        raise AppError(404, "NOT_FOUND", "Phiếu không tồn tại")
    if str(req.maker_id) != str(user.id):
        raise AppError(403, "FORBIDDEN", "only the maker can submit")
    if req.status != "draft":
        raise AppError(409, "STATE_CONFLICT", f"cannot submit từ {req.status}")
    _check_if_match(request, req.version)

    req.status = "submitted"
    req.version += 1
    req.submitted_at = _now()
    await db.commit()
    await db.refresh(req)
    return _cash_req_out(req)


# --------------------------------------------------------------------------
# Approve
# --------------------------------------------------------------------------

@router.post("/cash-sim/requests/{request_id}/approve")
async def approve_cash_request(
    request_id: str,
    request: Request,
    user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    """Force post: check quỹ thực, post journal cân, audit + outbox trong tx (docs/06, B08)."""
    req = await _load_request(db, uuid.UUID(request_id))
    if req is None:
        raise AppError(404, "NOT_FOUND", "Phiếu không tồn tại")
    if str(req.maker_id) == str(user.id):
        raise AppError(403, "SELF_APPROVAL_FORBIDDEN", "maker không được tự duyệt (A1)")
    caps = await _user_capabilities(db, user.id)
    if "cash:approve_request" not in caps:
        raise AppError(403, "FORBIDDEN", "missing capability: cash:approve_request")
    if req.status != "submitted":
        raise AppError(409, "STATE_CONFLICT", f"cannot approve từ {req.status}")
    _check_if_match(request, req.version)

    box = await _balance_for_box(db, req.cashbox_id)
    if box is None:
        raise AppError(404, "CASHBOX_NOT_FOUND", "cashbox không tồn tại")
    amount = Decimal(str(req.amount))
    is_receipt = req.kind == "receipt"
    # Chi (payment/adjustment) mới cần quỹ đủ; thu (receipt) là tiền VÀO quỹ.
    if not is_receipt and Decimal(str(box.amount)) < amount:
        raise AppError(
            409, "INSUFFICIENT_SIM_CASH", "quỹ trong box không đủ để hoàn tất",
            {"current_amount": str(box.amount), "requested": str(amount)},
        )

    cid = uuid.uuid4().hex[:12]
    now = _now()
    journal = CashJournal(
        id=uuid.uuid4(),
        request_id=req.id,
        posted_at=now,
        correlation_id=cid,
        reversal_of=None,
    )
    db.add(journal)
    await db.flush()

    if is_receipt:
        # Thu: debit SIM_CASH (quỹ tăng), credit SIM_OFFSET (nguồn đối ứng).
        debit = _cash_entry(journal.id, f"{_SIM_CASH_PREFIX}{req.cashbox_id}", amount, _ZERO)
        credit = _cash_entry(journal.id, _SIM_OFFSET, _ZERO, amount)
    else:
        # Chi: debit SIM_OFFSET, credit SIM_CASH (quỹ giảm).
        debit = _cash_entry(journal.id, _SIM_OFFSET, amount, _ZERO)
        credit = _cash_entry(journal.id, f"{_SIM_CASH_PREFIX}{req.cashbox_id}", _ZERO, amount)

    db.add(debit)
    db.add(credit)

    box.amount = Decimal(str(box.amount)) + amount if is_receipt else Decimal(str(box.amount)) - amount
    box.version += 1
    req.status = "posted"
    req.version += 1
    req.approver_id = user.id
    await _audit(db, user.id, "approve", req)
    await _outbox(db, "cash_request.approved", req, {"correlation_id": cid})

    try:
        await db.commit()
    except IntegrityError:
        await db.rollback()
        raise AppError(409, "VERSION_CONFLICT", "Phiếu đã bị thay đổi giữa các concurrent approve (A2)") from None

    await db.refresh(req)
    return _cash_req_out(req, {"journal_id": str(journal.id)})


# --------------------------------------------------------------------------
# Reject
# --------------------------------------------------------------------------

@router.post("/cash-sim/requests/{request_id}/reject")
async def reject_cash_request(
    request_id: str,
    request: Request,
    payload: dict[str, Any] | None = None,
    user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, uuid.UUID(request_id))
    if req is None:
        raise AppError(404, "NOT_FOUND", "Phiếu không tồn tại")
    if str(req.maker_id) == str(user.id):
        raise AppError(403, "SELF_APPROVAL_FORBIDDEN", "maker không được tự từ chối (A1)")
    caps = await _user_capabilities(db, user.id)
    if "cash:approve_request" not in caps:
        raise AppError(403, "FORBIDDEN", "missing capability: cash:approve_request")
    if req.status != "submitted":
        raise AppError(409, "STATE_CONFLICT", f"cannot reject từ {req.status}")
    _check_if_match(request, req.version)

    if payload and payload.get("reason"):
        req.reason = str(payload["reason"])
    req.status = "rejected"
    req.version += 1
    req.approver_id = user.id
    await db.commit()
    await db.refresh(req)
    return _cash_req_out(req)


# --------------------------------------------------------------------------
# Reverse
# --------------------------------------------------------------------------

@router.post("/cash-sim/requests/{request_id}/reverse")
async def reverse_cash_request(
    request_id: str,
    request: Request,
    payload: dict[str, Any] | None = None,
    user=Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
):
    """Reversal = journal mới, debit/credit ngược lại, original nguyên vẹn (C3, docs/05)."""
    key = _require_key(request)
    req = await _load_request(db, uuid.UUID(request_id))
    if req is None:
        raise AppError(404, "NOT_FOUND", "Phiếu không tồn tại")
    caps = await _user_capabilities(db, user.id)
    if not ({"cash:create_request", "cash:approve_request", "cash:reconcile"} & caps):
        raise AppError(403, "FORBIDDEN", "missing capability to reverse cash request")
    if req.status != "posted":
        raise AppError(409, "STATE_CONFLICT", f"cannot reverse từ {req.status}")
    _check_if_match(request, req.version)

    orig_j = (
        await db.execute(select(CashJournal).where(CashJournal.request_id == req.id))
    ).scalar_one_or_none()
    if orig_j is None:
        raise AppError(404, "JOURNAL_NOT_FOUND", "Không tìm thấy journal gốc")

    box = await _balance_for_box(db, req.cashbox_id)
    if box is None:
        raise AppError(404, "CASHBOX_NOT_FOUND", "cashbox không tồn tại")
    amount = Decimal(str(req.amount))
    is_receipt = req.kind == "receipt"
    if is_receipt and Decimal(str(box.amount)) < amount:
        raise AppError(409, "INSUFFICIENT_SIM_CASH", "quỹ không đủ để đảo bút toán thu.")

    now = _now()
    base = req.id.hex[:8]
    rev_req = CashRequest(
        id=uuid.uuid4(),
        cashbox_id=req.cashbox_id,
        kind="reversal",
        amount=amount,
        status="posted",
        maker_id=user.id,
        approver_id=req.approver_id or user.id,
        reason=(payload or {}).get("reason") or f"Reversal of {req.id}",
        version=1,
        created_by=user.id,
    )
    db.add(rev_req)
    await db.flush()

    rev_j = CashJournal(
        id=uuid.uuid4(),
        request_id=rev_req.id,
        posted_at=now,
        correlation_id=base,
        reversal_of=orig_j.id,
    )
    db.add(rev_j)
    await db.flush()

    if is_receipt:
        # Đảo phiếu thu: debit SIM_OFFSET, credit SIM_CASH (quỹ giảm lại).
        debit = _cash_entry(rev_j.id, _SIM_OFFSET, amount, _ZERO)
        credit = _cash_entry(rev_j.id, f"{_SIM_CASH_PREFIX}{req.cashbox_id}", _ZERO, amount)
        box.amount = Decimal(str(box.amount)) - amount
    else:
        # Đảo phiếu chi: debit SIM_CASH, credit SIM_OFFSET (quỹ hoàn lại).
        debit = _cash_entry(rev_j.id, f"{_SIM_CASH_PREFIX}{req.cashbox_id}", amount, _ZERO)
        credit = _cash_entry(rev_j.id, _SIM_OFFSET, _ZERO, amount)
        box.amount = Decimal(str(box.amount)) + amount

    db.add_all([debit, credit])
    box.version += 1
    await _audit(db, user.id, "reverse", rev_req)
    await _outbox(db, "cash_request.reversed", rev_req, {"reversal_of": str(orig_j.id)})

    try:
        await db.commit()
        await db.refresh(rev_j)
    except IntegrityError:
        await db.rollback()
        raise AppError(409, "VERSION_CONFLICT", "Phiếu đã bị thay đổi giữa giao dịch đảo (A2)") from None

    body = {
        "id": str(rev_j.id),
        "request_id": str(req.id),
        "reversal_of": str(orig_j.id),
        "status": "posted",
        "posted_at": rev_j.posted_at.isoformat() if rev_j.posted_at else None,
        "simulation": True,
        "currency": "VND",
    }
    idempotency.remember(db, key, idempotency.payload_hash(payload or {}), 200, body)
    return body


# --------------------------------------------------------------------------
# Audit + Outbox helpers (giữ tính nhất quán với inventory routes)
# --------------------------------------------------------------------------

async def _audit(db: AsyncSession, actor_id, action: str, req: CashRequest) -> None:
    db.add(
        AuditEvent(
            id=uuid.uuid4(),
            actor_id=actor_id,
            action=action,
            subject=f"{_SUBJECT}:{req.id}",
            before_hash=idempotency.payload_hash({"status": req.status}),
            after_hash=idempotency.payload_hash({"status": req.status}),
            occurred_at=datetime.now(UTC),
            metadata_json={"kind": req.kind, "status": req.status},
        )
    )


async def _outbox(db: AsyncSession, event_type: str, req: CashRequest, extra: dict) -> None:
    """Outbox cùng transaction -> worker publish at-least-once (docs/04, B09)."""
    db.add(
        OutboxEvent(
            id=uuid.uuid4(),
            event_type=event_type,
            aggregate_id=str(req.id),
            payload_json={
                "subject_type": _SUBJECT,
                "subject_id": str(req.id),
                "kind": req.kind,
                "status": req.status,
                "amount": str(req.amount),
                **extra,
            },
            created_at=datetime.now(UTC),
        )
    )
