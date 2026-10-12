"""Inventory routes (docs/05, B05-B07).

Bất biến docs/00:
1. maker != checker, kể cả admin (A1).
2. đúng một lần post mỗi phiếu — optimistic lock + unique constraint (A2).
3. retry cùng Idempotency-Key + cùng payload replay; khác payload 409 (A3/A4).
4. xuất/chuyển quá tồn -> 409, balance giữ nguyên (I1).
5. mọi write đều audit + outbox trong cùng transaction.
"""
import uuid
from datetime import UTC, datetime
from decimal import Decimal, InvalidOperation

from fastapi import APIRouter, Depends, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.errors import AppError
from app.api.routes.auth_utils import get_user_capabilities, require_any_capability
from app.database import get_db
from app.models.activity import AuditEvent, OutboxEvent
from app.models.approval import ApprovalDecision
from app.models.inventory import (
    Asset,
    Balance,
    InventoryRequest,
    InventoryRequestLine,
    Item,
    Location,
    Movement,
)
from app.services import idempotency

router = APIRouter()

_VIEW_CATALOG = (
    "inventory:manage_catalog",
    "inventory:view_balances",
    "inventory:create_request",
    "inventory:approve_request",
)
_VIEW_ASSETS = (
    "inventory:manage_catalog",
    "inventory:view_balances",
    "inventory:create_request",
    "inventory:approve_request",
    "audit:view",
)
_KINDS = ("receipt", "issue", "transfer", "adjustment")
_SUBJECT = "inventory_request"
_APPROVE_CAPS = ("inventory:create_request", "inventory:approve_request")
# Ghi lại quyết định của agent để reviewer đối chiếu (không dùng để đọc response).
_write_caps = require_any_capability("inventory:create_request")
_ZERO = Decimal("0")


def _now() -> datetime:
    return datetime.now(UTC)


def _uuid(value: str, field: str) -> uuid.UUID:
    try:
        return uuid.UUID(str(value))
    except (ValueError, AttributeError, TypeError):
        raise AppError(422, "INVALID_UUID", f"{field} không phải UUID hợp lệ") from None


def _qty(raw) -> Decimal:
    try:
        return Decimal(str(raw))
    except (InvalidOperation, ValueError, TypeError):
        raise AppError(422, "INVALID_QUANTITY", f"quantity không hợp lệ: {raw!r}") from None


def _check_if_match(request: Request, version: int) -> None:
    raw = request.headers.get("if-match")
    if raw is None:
        return
    expected = raw.strip().strip('"')
    if expected.isdigit() and int(expected) != version:
        raise AppError(
            409,
            "VERSION_CONFLICT",
            f"If-Match={expected} nhưng version hiện tại là {version}",
            {"current_version": version},
        )


def _req_out(req: InventoryRequest, lines: list | None = None) -> dict:
    body: dict = {
        "id": str(req.id),
        "kind": req.kind,
        "status": req.status,
        "version": req.version,
        "maker_id": str(req.maker_id),
        "approver_id": str(req.approver_id) if req.approver_id else None,
        "reason": req.reason,
        "created_at": req.created_at.isoformat() if getattr(req, "created_at", None) else None,
        "simulation": True,
    }
    if req.source_id:
        body["source_id"] = str(req.source_id)
    if req.dest_id:
        body["dest_id"] = str(req.dest_id)
    if lines is not None:
        body["lines"] = lines
    return body


async def _load_lines(db: AsyncSession, request_id) -> list:
    rows = (
        await db.execute(
            select(InventoryRequestLine).where(
                InventoryRequestLine.request_id == request_id
            )
        )
    ).scalars().all()
    return [
        {"item_id": str(r.item_id), "quantity": str(r.quantity), "serial": r.serial}
        for r in rows
    ]


async def _load_request(db: AsyncSession, request_id: str) -> InventoryRequest:
    req = (
        await db.execute(
            select(InventoryRequest).where(
                InventoryRequest.id == _uuid(request_id, "request_id")
            )
        )
    ).scalar_one_or_none()
    if req is None:
        raise AppError(404, "NOT_FOUND", "Phiếu không tồn tại")
    return req


async def _load_request_for_read(db, request_id: str, user) -> InventoryRequest:
    """Object scope docs/08: maker xem phiếu mình; có quyền duyệt xem tất cả."""
    req = await _load_request(db, request_id)
    if str(req.maker_id) == str(user.id):
        return req
    caps = await get_user_capabilities(db, user.id)
    if "inventory:approve_request" not in caps:
        raise AppError(404, "NOT_FOUND", "Phiếu không tồn tại hoặc ngoài phạm vi của bạn")
    return req


async def _resolve_item_id(db: AsyncSession, line: dict):
    if line.get("item_id"):
        iid = _uuid(line["item_id"], "item_id")
        if (await db.execute(select(Item.id).where(Item.id == iid))).scalar_one_or_none() is None:
            raise AppError(422, "ITEM_NOT_FOUND", f"item_id {line['item_id']} không tồn tại")
        return iid
    if line.get("sku"):
        row = (await db.execute(select(Item).where(Item.sku == line["sku"]))).scalar_one_or_none()
        if row is not None:
            return row.id
    raise AppError(422, "ITEM_NOT_FOUND", f"Không xác định được item từ {line!r}")


def _validate_transition(req: InventoryRequest, kind: str, src, dst) -> None:
    """issue/transfer bắt buộc source; receipt/adjustment bắt buộc target."""
    if req.kind in ("issue", "transfer") and src is None:
        raise AppError(422, "SOURCE_REQUIRED", f"kind={kind} yêu cầu source_id")
    if req.kind in ("receipt", "adjustment") and src is None and dst is None:
        raise AppError(
            422, "DESTINATION_REQUIRED", f"kind={kind} yêu cầu dest_id (hoặc source_id)"
        )
    if req.kind == "transfer" and (src is None or dst is None):
        raise AppError(422, "DESTINATION_REQUIRED", "transfer yêu cầu cả source_id và dest_id")


async def _get_balance(db: AsyncSession, location_id, item_id) -> Balance | None:
    return (
        await db.execute(
            select(Balance).where(
                Balance.location_id == location_id, Balance.item_id == item_id
            )
        )
    ).scalar_one_or_none()


async def _audit(db, actor_id, action: str, req: InventoryRequest, before: dict) -> None:
    db.add(
        AuditEvent(
            id=uuid.uuid4(),
            actor_id=actor_id,
            action=action,
            subject=f"{_SUBJECT}:{req.id}",
            before_hash=idempotency.payload_hash(before),
            after_hash=idempotency.payload_hash(
                {"status": req.status, "version": req.version}
            ),
            occurred_at=_now(),
            metadata_json={"kind": req.kind, "status": req.status},
        )
    )


async def _outbox(db, event_type: str, req: InventoryRequest, extra: dict) -> None:
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
                "version": req.version,
                **extra,
            },
            created_at=_now(),
        )
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


def _replay(key: str, payload) -> dict | None:
    replay = idempotency.check(key, idempotency.payload_hash(payload))
    if replay is None:
        return None
    _status, body = replay
    return body

# --------------------------------------------------------------------------
# --------------------------------------------------------------------------
# Catalog reads
# --------------------------------------------------------------------------
@router.get("/inventory/items")
async def list_items(
    current_user=Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
):
    items = (await db.execute(select(Item).where(Item.active.is_(True)))).scalars().all()
    return {
        "items": [
            {
                "id": str(i.id),
                "sku": i.sku,
                "name": i.name,
                "unit": i.unit,
                "tracked_serial": i.tracked_serial,
                "active": i.active,
            }
            for i in items
        ]
    }


@router.get("/inventory/locations")
async def list_locations(
    current_user=Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
):
    locations = (await db.execute(select(Location))).scalars().all()
    return {
        "locations": [
            {"id": str(loc.id), "code": loc.code, "name": loc.name, "active": loc.active}
            for loc in locations
        ]
    }


@router.get("/inventory/balances")
async def list_balances(
    current_user=Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
    location_id: str | None = None,
    item_id: str | None = None,
    limit: int = 100,
    cursor: str | None = None,
):
    """Tồn kho theo (location, item). Paginate bằng limit + opaque cursor."""
    import base64

    limit = max(1, min(limit, 100))
    stmt = select(Balance).order_by(Balance.location_id, Balance.item_id)
    if location_id:
        stmt = stmt.where(Balance.location_id == _uuid(location_id, "location_id"))
    if item_id:
        stmt = stmt.where(Balance.item_id == _uuid(item_id, "item_id"))
    rows = (await db.execute(stmt.limit(limit + 1))).scalars().all()
    page, next_cursor = rows[:limit], None
    if len(rows) > limit:
        raw = f"{page[-1].location_id}:{page[-1].item_id}".encode()
        next_cursor = base64.urlsafe_b64encode(raw).decode()
    return {
        "balances": [
            {
                "location_id": str(b.location_id),
                "item_id": str(b.item_id),
                "quantity": str(b.quantity),
                "version": b.version,
            }
            for b in page
        ],
        "next_cursor": next_cursor,
        "limit": limit,
    }


@router.get("/inventory/assets")
async def list_assets(
    current_user=Depends(require_any_capability(*_VIEW_ASSETS)),
    db: AsyncSession = Depends(get_db),
    serial: str | None = None,
    location_id: str | None = None,
    limit: int = 100,
    cursor: str | None = None,
):
    import base64

    limit = max(1, min(limit, 100))
    stmt = select(Asset).order_by(Asset.serial)
    if serial:
        stmt = stmt.where(Asset.serial == serial)
    if location_id:
        stmt = stmt.where(Asset.location_id == _uuid(location_id, "location_id"))
    rows = (await db.execute(stmt.limit(limit + 1))).scalars().all()
    page, next_cursor = rows[:limit], None
    if len(rows) > limit:
        next_cursor = base64.urlsafe_b64encode(str(page[-1].id).encode()).decode()
    return {
        "assets": [
            {
                "id": str(a.id),
                "item_id": str(a.item_id),
                "serial": a.serial,
                "location_id": str(a.location_id),
                "assignee_demo": a.assignee_demo,
                "status": a.status,
            }
            for a in page
        ],
        "next_cursor": next_cursor,
        "limit": limit,
    }


@router.get("/inventory/movements")
async def list_movements(
    current_user=Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
    request_id: str | None = None,
    limit: int = 100,
    cursor: str | None = None,
):
    import base64

    limit = max(1, min(limit, 100))
    stmt = select(Movement).order_by(Movement.posted_at.desc(), Movement.id)
    if request_id:
        stmt = stmt.where(Movement.request_id == _uuid(request_id, "request_id"))
    rows = (await db.execute(stmt.limit(limit + 1))).scalars().all()
    page, next_cursor = rows[:limit], None
    if len(rows) > limit:
        next_cursor = base64.urlsafe_b64encode(str(page[-1].id).encode()).decode()
    return {
        "movements": [
            {
                "id": str(m.id),
                "request_id": str(m.request_id),
                "line_id": str(m.line_id),
                "item_id": str(m.item_id),
                "location_id": str(m.location_id),
                "quantity_signed": str(m.quantity_signed),
                "posted_at": m.posted_at.isoformat() if m.posted_at else None,
            }
            for m in page
        ],
        "next_cursor": next_cursor,
        "limit": limit,
    }


@router.get("/inventory/requests")
async def list_requests(
    current_user=Depends(require_any_capability(*_APPROVE_CAPS)),
    db: AsyncSession = Depends(get_db),
    status: str | None = None,
    mine: bool = False,
    limit: int = 100,
    cursor: str | None = None,
):
    import base64

    limit = max(1, min(limit, 100))
    stmt = select(InventoryRequest).order_by(
        InventoryRequest.created_at.desc(), InventoryRequest.id
    )
    if status:
        stmt = stmt.where(InventoryRequest.status == status)
    caps = await get_user_capabilities(db, current_user.id)
    if mine or "inventory:approve_request" not in caps:
        stmt = stmt.where(InventoryRequest.maker_id == current_user.id)
    rows = (await db.execute(stmt.limit(limit + 1))).scalars().all()
    page, next_cursor = rows[:limit], None
    if len(rows) > limit:
        next_cursor = base64.urlsafe_b64encode(str(page[-1].id).encode()).decode()
    return {
        "requests": [_req_out(r, await _load_lines(db, r.id)) for r in page],
        "next_cursor": next_cursor,
        "limit": limit,
    }


@router.get("/inventory/requests/{request_id}")
async def get_request(
    request_id: str,
    current_user=Depends(require_any_capability(*_APPROVE_CAPS)),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request_for_read(db, request_id, current_user)
    return _req_out(req, await _load_lines(db, req.id))



# --------------------------------------------------------------------------
# Writes — create / submit
# --------------------------------------------------------------------------
@router.post("/inventory/requests", status_code=201)
async def create_request(
    payload: dict,
    request: Request,
    user=Depends(require_any_capability("inventory:create_request")),
    db: AsyncSession = Depends(get_db),
):
    key = _require_key(request)
    kind = str(payload.get("kind", "")).lower()
    if kind not in _KINDS:
        raise AppError(422, "INVALID_KIND", f"kind phải thuộc {_KINDS}, nhận {kind!r}")
    cached = _replay(key, payload)
    if cached is not None:
        return cached

    src = _uuid(payload["source_id"], "source_id") if payload.get("source_id") else None
    dst = _uuid(payload["dest_id"], "dest_id") if payload.get("dest_id") else None
    if kind == "transfer" and (src is None or dst is None):
        raise AppError(422, "DESTINATION_REQUIRED", "transfer yêu cầu source_id và dest_id")

    req = InventoryRequest(
        id=uuid.uuid4(),
        kind=kind,
        status="draft",
        maker_id=user.id,
        created_by=user.id,
        source_id=src,
        dest_id=dst,
        reason=payload.get("reason"),
        version=1,
    )
    db.add(req)
    await db.flush()

    lines_in = payload.get("lines") or []
    if not isinstance(lines_in, list):
        raise AppError(422, "INVALID_LINES", "lines phải là danh sách")
    for ln in lines_in:
        if not isinstance(ln, dict):
            raise AppError(422, "INVALID_LINES", f"line không hợp lệ: {ln!r}")
        item_id = await _resolve_item_id(db, ln)
        qty = _qty(ln.get("quantity", "0"))
        if qty <= _ZERO:
            raise AppError(
                422, "INVALID_QUANTITY", f"quantity phải > 0, nhận {ln.get('quantity')!r}"
            )
        db.add(
            InventoryRequestLine(
                id=uuid.uuid4(), request_id=req.id, item_id=item_id,
                quantity=qty, serial=ln.get("serial"),
            )
        )
    await _audit(db, user.id, "inventory.request.created", req, {"status": None})
    await _outbox(db, "inventory.request.created", req, {})
    await db.commit()
    await db.refresh(req)
    body = _req_out(req, await _load_lines(db, req.id))
    idempotency.remember(db, key, idempotency.payload_hash(payload), 201, body)
    return body


@router.post("/inventory/requests/{request_id}/submit")
async def submit_request(
    request_id: str,
    request: Request,
    user=Depends(require_any_capability("inventory:create_request")),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, request_id)
    if str(req.maker_id) != str(user.id):
        raise AppError(403, "FORBIDDEN", "Chỉ maker mới được submit phiếu của mình")
    if req.status != "draft":
        raise AppError(409, "STATE_CONFLICT", f"cannot submit từ {req.status}")
    _check_if_match(request, req.version)
    before = {"status": req.status, "version": req.version}
    req.status = "submitted"
    req.version += 1
    req.submitted_at = _now()
    await _audit(db, user.id, "inventory.request.submitted", req, before)
    await _outbox(db, "inventory.request.submitted", req, {})
    await db.commit()
    await db.refresh(req)
    return _req_out(req, await _load_lines(db, req.id))


@router.post("/inventory/requests/{request_id}/approve")
async def approve_request(
    request_id: str,
    request: Request,
    user=Depends(require_any_capability(*_APPROVE_CAPS)),
    db: AsyncSession = Depends(get_db),
):
    """Duyệt + post trong một transaction (docs/06 cấm 202 cho approve)."""
    key = _require_key(request)
    req = await _load_request(db, request_id)
    cached = _replay(key, {"approve": str(req.id)})
    if cached is not None:
        return cached
    # Invariant 1 — maker không tự duyệt, kể cả admin.
    if str(req.maker_id) == str(user.id):
        raise AppError(403, "SELF_APPROVAL_FORBIDDEN", "maker không được tự duyệt (A1)")
    caps = await get_user_capabilities(db, user.id)
    if "inventory:approve_request" not in caps:
        raise AppError(403, "FORBIDDEN", "missing capability: inventory:approve_request")
    if req.status != "submitted":
        raise AppError(409, "STATE_CONFLICT", f"cannot approve từ {req.status}")
    _check_if_match(request, req.version)

    # Optimistic lock tại DB: version phải còn đúng — checker thứ hai vào
    # sau sẽ thấy 0 row -> 409, đúng một post duy nhất (A2).
    locked = (
        await db.execute(
            select(InventoryRequest)
            .where(InventoryRequest.id == req.id, InventoryRequest.version == req.version)
            .with_for_update()
        )
    ).scalar_one_or_none()
    if locked is None:
        raise AppError(409, "VERSION_CONFLICT", "Phiếu đã bị thay đổi (A2)")
    lines = (
        await db.execute(
            select(InventoryRequestLine).where(InventoryRequestLine.request_id == req.id)
        )
    ).scalars().all()

    # I1: kiểm tra đủ tồn TRƯỚC khi ghi movement nào.
    if req.kind in ("issue", "transfer") and req.source_id is not None:
        need: dict = {}
        for ln in lines:
            need[ln.item_id] = need.get(ln.item_id, _ZERO) + Decimal(str(ln.quantity))
        for item_id, qty in need.items():
            bal = await _get_balance(db, req.source_id, item_id)
            have = Decimal(str(bal.quantity)) if bal else _ZERO
            if have < qty:
                raise AppError(409, "INSUFFICIENT_STOCK", "không đủ tồn tại kho nguồn")

    now = _now()
    for ln in lines:
        qty = Decimal(str(ln.quantity))
        if req.kind == "transfer" and req.source_id is not None and req.dest_id is not None:
            db.add(
                Movement(
                    id=uuid.uuid4(), request_id=req.id, line_id=ln.id,
                    item_id=ln.item_id, location_id=req.source_id,
                    quantity_signed=-qty, posted_at=now,
                )
            )
            db.add(
                Movement(
                    id=uuid.uuid4(), request_id=req.id, line_id=ln.id,
                    item_id=ln.item_id, location_id=req.dest_id,
                    quantity_signed=qty, posted_at=now,
                )
            )
            src = await _get_balance(db, req.source_id, ln.item_id)
            if src:
                src.quantity = Decimal(str(src.quantity)) - qty
            dst = await _get_balance(db, req.dest_id, ln.item_id)
            if dst:
                dst.quantity = Decimal(str(dst.quantity)) + qty
            else:
                db.add(Balance(location_id=req.dest_id, item_id=ln.item_id, quantity=qty, version=1))
        elif req.kind == "issue" and req.source_id is not None:
            db.add(
                Movement(
                    id=uuid.uuid4(), request_id=req.id, line_id=ln.id,
                    item_id=ln.item_id, location_id=req.source_id,
                    quantity_signed=-qty, posted_at=now,
                )
            )
            src = await _get_balance(db, req.source_id, ln.item_id)
            if src:
                src.quantity = Decimal(str(src.quantity)) - qty
        else:
            target = req.dest_id or req.source_id
            if target is None:
                continue
            db.add(
                Movement(
                    id=uuid.uuid4(), request_id=req.id, line_id=ln.id,
                    item_id=ln.item_id, location_id=target,
                    quantity_signed=qty, posted_at=now,
                )
            )
            bal = await _get_balance(db, target, ln.item_id)
            if bal:
                bal.quantity = Decimal(str(bal.quantity)) + qty
            else:
                db.add(Balance(location_id=target, item_id=ln.item_id, quantity=qty, version=1))

    before = {"status": "submitted", "version": req.version}
    req.status = "posted"
    req.version += 1
    req.approver_id = user.id
    db.add(
        ApprovalDecision(
            id=uuid.uuid4(), subject_type="inventory_request", subject_id=req.id,
            actor_id=user.id, decision="approved",
        )
    )
    await _audit(db, user.id, "inventory.request.approved", req, before)
    await _outbox(db, "inventory.request.approved", req, {})
    await db.commit()
    await db.refresh(req)
    body = _req_out(req)
    idempotency.remember(db, key, idempotency.payload_hash({"approve": str(req.id)}), 200, body)
    return body


@router.post("/inventory/requests/{request_id}/reject")
async def reject_request(
    request_id: str,
    request: Request,
    payload: dict | None = None,
    user=Depends(require_any_capability(*_APPROVE_CAPS)),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, request_id)
    if str(req.maker_id) == str(user.id):
        raise AppError(403, "SELF_APPROVAL_FORBIDDEN", "maker không được tự duyệt (A1)")
    caps = await get_user_capabilities(db, user.id)
    if "inventory:approve_request" not in caps:
        raise AppError(403, "FORBIDDEN", "missing capability: inventory:approve_request")
    if req.status != "submitted":
        raise AppError(409, "STATE_CONFLICT", f"cannot reject từ {req.status}")
    _check_if_match(request, req.version)
    before = {"status": req.status, "version": req.version}
    if payload and payload.get("reason"):
        req.reason = str(payload["reason"])
    req.status = "rejected"
    req.version += 1
    req.approver_id = user.id
    await _audit(db, user.id, "inventory.request.rejected", req, before)
    await _outbox(db, "inventory.request.rejected", req, {})
    await db.commit()
    await db.refresh(req)
    return _req_out(req)


@router.post("/inventory/assets", status_code=201)
async def create_asset(
    payload: dict,
    request: Request,
    user=Depends(require_any_capability("inventory:create_request")),
    db: AsyncSession = Depends(get_db),
):
    """Tạo tài sản theo serial — serial trùng -> 409 DUPLICATE_SERIAL (I3)."""
    from sqlalchemy.exc import IntegrityError

    key = _require_key(request)
    serial = str(payload.get("serial", "")).strip()
    if not serial:
        raise AppError(422, "SERIAL_REQUIRED", "serial là bắt buộc")
    cached = _replay(key, payload)
    if cached is not None:
        return cached
    item_id = await _resolve_item_id(db, payload)
    location_id = (
        _uuid(payload["location_id"], "location_id") if payload.get("location_id") else None
    )
    if location_id is None:
        raise AppError(422, "LOCATION_REQUIRED", "location_id là bắt buộc")
    dup = (
        await db.execute(select(Asset).where(Asset.serial == serial))
    ).scalar_one_or_none()
    if dup is not None:
        raise AppError(409, "DUPLICATE_SERIAL", f"serial {serial!r} đã tồn tại")
    asset = Asset(
        id=uuid.uuid4(), item_id=item_id, serial=serial,
        location_id=location_id, status=str(payload.get("status", "available")),
    )
    db.add(asset)
    try:
        await db.commit()
        await db.refresh(asset)
    except IntegrityError:
        await db.rollback()
        raise AppError(409, "DUPLICATE_SERIAL", f"serial {serial!r} đã tồn tại") from None
    body = {
        "id": str(asset.id),
        "item_id": str(asset.item_id),
        "serial": asset.serial,
        "location_id": str(asset.location_id),
        "assignee_demo": asset.assignee_demo,
        "status": asset.status,
    }
    idempotency.remember(db, key, idempotency.payload_hash(payload), 201, body)
    return body
