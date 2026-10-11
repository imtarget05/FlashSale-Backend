"""Inventory routes."""
import uuid
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation

from fastapi import APIRouter, Depends, Request
from fastapi.responses import JSONResponse
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth_utils import (
    get_user_capabilities,
    require_any_capability,
    require_capability,
)
from app.database import get_db
from app.models.inventory import (
    Asset,
    Balance,
    InventoryRequest,
    InventoryRequestLine,
    Item,
    Location,
    Movement,
)

router = APIRouter()

# Đọc danh mục: manager (catalog) hoặc ai được cấp xem tồn/đọc báo cáo.
# Staff (maker) cần xem items/locations để tạo phiếu -> thêm create_request.
_VIEW_CATALOG = (
    "inventory:manage_catalog",
    "inventory:view_balances",
    "inventory:create_request",
)
_VIEW_ASSETS = ("inventory:manage_catalog", "inventory:view_balances", "audit:view")


@router.get("/items")
async def list_items(
    current_user = Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
):
    """List inventory items."""
    result = await db.execute(select(Item))
    items = result.scalars().all()
    return {
        "items": [
            {
                "id": str(item.id),
                "sku": item.sku,
                "name": item.name,
                "unit": item.unit,
                "tracked_serial": item.tracked_serial,
                "active": item.active,
            }
            for item in items
        ]
    }


@router.get("/locations")
async def list_locations(
    current_user = Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
):
    """List storage locations."""
    result = await db.execute(select(Location))
    locations = result.scalars().all()
    return {
        "locations": [
            {
                "id": str(loc.id),
                "code": loc.code,
                "name": loc.name,
                "active": loc.active,
            }
            for loc in locations
        ]
    }


@router.get("/balances")
async def list_balances(
    current_user=Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
    location_id: str | None = None,
    item_id: str | None = None,
):
    """List inventory balances."""
    stmt = select(Balance)
    if location_id:
        stmt = stmt.where(Balance.location_id == uuid.UUID(location_id))
    if item_id:
        stmt = stmt.where(Balance.item_id == uuid.UUID(item_id))
    result = await db.execute(stmt)
    balances = result.scalars().all()
    return {
        "balances": [
            {
                "location_id": str(b.location_id),
                "item_id": str(b.item_id),
                "quantity": str(b.quantity),
                "version": b.version,
            }
            for b in balances
        ]
    }


@router.get("/assets")
async def list_assets(
    current_user = Depends(require_any_capability(*_VIEW_ASSETS)),
    db: AsyncSession = Depends(get_db),
):
    """List tracked assets."""
    result = await db.execute(select(Asset))
    assets = result.scalars().all()
    return {
        "assets": [
            {
                "id": str(a.id),
                "item_id": str(a.item_id),
                "serial": a.serial,
                "location_id": str(a.location_id),
                "status": a.status,
            }
            for a in assets
        ]
    }


def _req_id(request: Request) -> str:
    return request.headers.get("x-request-id") or uuid.uuid4().hex


def _err(request: Request, status: int, code: str, message: str) -> JSONResponse:
    return JSONResponse(
        status_code=status,
        content={"error": {"code": code, "message": message, "request_id": _req_id(request), "details": {}}},
    )


def _if_match(request: Request, version: int) -> JSONResponse | None:
    raw = request.headers.get("if-match")
    if raw is None:
        return None
    if raw.strip().strip('"') != str(version):
        return _err(request, 409, "VERSION_CONFLICT", f"version mismatch: expected {version}")
    return None


def _as_uuid(value: str | None) -> uuid.UUID | None:
    if not value:
        return None
    return value if isinstance(value, uuid.UUID) else uuid.UUID(str(value))


async def _resolve_item_id(db: AsyncSession, line: dict) -> uuid.UUID | None:
    if line.get("item_id"):
        try:
            return uuid.UUID(str(line["item_id"]))
        except ValueError:
            pass
    sku = line.get("sku")
    if sku:
        row = (await db.execute(select(Item).where(Item.sku == sku))).scalar_one_or_none()
        if row is not None:
            return row.id
    return None


def _line_qty(line: dict) -> Decimal:
    raw = line.get("quantity", line.get("qty", "0"))
    try:
        return Decimal(str(raw))
    except (InvalidOperation, ValueError):
        return Decimal("0")


def _req_out(req: InventoryRequest, lines: list | None = None) -> dict:
    body: dict = {
        "id": str(req.id),
        "kind": req.kind,
        "status": req.status,
        "version": req.version,
        "maker_id": str(req.maker_id),
    }
    if req.source_id:
        body["source_id"] = str(req.source_id)
    if req.dest_id:
        body["dest_id"] = str(req.dest_id)
    if lines is not None:
        body["lines"] = lines
    return body


@router.post("/requests", status_code=201)
async def create_request(
    payload: dict,
    request: Request,
    user=Depends(require_capability("inventory:create_request")),
    db: AsyncSession = Depends(get_db),
):
    """Create a draft inventory request (minimal write-path)."""
    kind = str(payload.get("kind", "issue"))
    source = payload.get("source_id") or payload.get("source")
    dest = payload.get("dest_id") or payload.get("dest")
    if not source and payload.get("location_code"):
        loc = (await db.execute(select(Location).where(Location.code == payload["location_code"]))).scalar_one_or_none()
        if loc is not None:
            source = str(loc.id)
    req = InventoryRequest(
        id=uuid.uuid4(),
        kind=kind,
        status="draft",
        maker_id=user.id,
        source_id=_as_uuid(source),
        dest_id=_as_uuid(dest),
        reason=payload.get("reason"),
        version=1,
        created_by=user.id,
    )
    db.add(req)
    await db.flush()
    out_lines: list[dict] = []
    for line in payload.get("lines", []) or []:
        item_id = await _resolve_item_id(db, line)
        if item_id is None:
            continue
        qty = _line_qty(line)
        db.add(InventoryRequestLine(id=uuid.uuid4(), request_id=req.id, item_id=item_id, quantity=qty, serial=line.get("serial")))
        out_lines.append({"item_id": str(item_id), "quantity": str(qty)})
    await db.commit()
    await db.refresh(req)
    return _req_out(req, out_lines)


async def _load_request(db: AsyncSession, request_id: str) -> InventoryRequest | None:
    try:
        rid = uuid.UUID(request_id)
    except ValueError:
        return None
    return (await db.execute(select(InventoryRequest).where(InventoryRequest.id == rid))).scalar_one_or_none()


@router.get("/requests/{request_id}")
async def get_request(
    request_id: str,
    request: Request,
    user=Depends(require_any_capability("inventory:create_request", "inventory:approve_request")),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, request_id)
    if req is None:
        return _err(request, 404, "NOT_FOUND", "request not found")
    rows = (await db.execute(select(InventoryRequestLine).where(InventoryRequestLine.request_id == req.id))).scalars().all()
    lines = [{"item_id": str(r.item_id), "quantity": str(r.quantity)} for r in rows]
    return _req_out(req, lines)


@router.post("/requests/{request_id}/submit")
async def submit_request(
    request_id: str,
    request: Request,
    user=Depends(require_capability("inventory:create_request")),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, request_id)
    if req is None:
        return _err(request, 404, "NOT_FOUND", "request not found")
    if str(req.maker_id) != str(user.id):
        return _err(request, 403, "FORBIDDEN", "only the maker can submit")
    if req.status != "draft":
        return _err(request, 409, "STATE_CONFLICT", f"cannot submit from {req.status}")
    conflict = _if_match(request, req.version)
    if conflict is not None:
        return conflict
    req.status = "submitted"
    req.version += 1
    req.submitted_at = datetime.now(timezone.utc).isoformat()
    await db.commit()
    await db.refresh(req)
    return _req_out(req)


@router.get("/movements")
async def list_movements(
    user=Depends(require_any_capability(*_VIEW_CATALOG)),
    db: AsyncSession = Depends(get_db),
    request_id: str | None = None,
):
    stmt = select(Movement)
    if request_id:
        stmt = stmt.where(Movement.request_id == uuid.UUID(request_id))
    rows = (await db.execute(stmt)).scalars().all()
    return {
        "movements": [
            {
                "id": str(m.id),
                "request_id": str(m.request_id),
                "item_id": str(m.item_id),
                "location_id": str(m.location_id),
                "quantity_signed": str(m.quantity_signed),
            }
            for m in rows
        ]
    }


async def _get_balance(db: AsyncSession, loc_id, item_id) -> Balance | None:
    return (await db.execute(select(Balance).where(Balance.location_id == loc_id, Balance.item_id == item_id))).scalar_one_or_none()


@router.post("/requests/{request_id}/approve")
async def approve_request(
    request_id: str,
    request: Request,
    user=Depends(require_any_capability("inventory:create_request", "inventory:approve_request")),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, request_id)
    if req is None:
        return _err(request, 404, "NOT_FOUND", "request not found")
    if str(req.maker_id) == str(user.id):
        return _err(request, 403, "SELF_APPROVAL_FORBIDDEN", "maker cannot approve own request")
    caps = await get_user_capabilities(db, user.id)
    if "inventory:approve_request" not in caps:
        return _err(request, 403, "FORBIDDEN", "missing capability: inventory:approve_request")
    if req.status != "submitted":
        return _err(request, 409, "STATE_CONFLICT", f"cannot approve from {req.status}")
    conflict = _if_match(request, req.version)
    if conflict is not None:
        return conflict
    lines = (await db.execute(select(InventoryRequestLine).where(InventoryRequestLine.request_id == req.id))).scalars().all()

    # Stock check for issue/transfer on source balances.
    if req.kind in ("issue", "transfer") and req.source_id is not None:
        need: dict = {}
        for ln in lines:
            need[ln.item_id] = need.get(ln.item_id, Decimal("0")) + Decimal(str(ln.quantity))
        for item_id, qty in need.items():
            bal = await _get_balance(db, req.source_id, item_id)
            have = Decimal(str(bal.quantity)) if bal else Decimal("0")
            if have < qty:
                return _err(request, 409, "INSUFFICIENT_STOCK", "insufficient stock at source")

    now = datetime.now(timezone.utc).isoformat()
    for ln in lines:
        qty = Decimal(str(ln.quantity))
        if req.kind == "transfer" and req.source_id is not None and req.dest_id is not None:
            db.add(Movement(id=uuid.uuid4(), request_id=req.id, line_id=ln.id, item_id=ln.item_id, location_id=req.source_id, quantity_signed=-qty, posted_at=now))
            db.add(Movement(id=uuid.uuid4(), request_id=req.id, line_id=ln.id, item_id=ln.item_id, location_id=req.dest_id, quantity_signed=qty, posted_at=now))
            src = await _get_balance(db, req.source_id, ln.item_id)
            if src:
                src.quantity = Decimal(str(src.quantity)) - qty
            dst = await _get_balance(db, req.dest_id, ln.item_id)
            if dst:
                dst.quantity = Decimal(str(dst.quantity)) + qty
            else:
                db.add(Balance(location_id=req.dest_id, item_id=ln.item_id, quantity=qty, version=1))
        elif req.kind == "issue" and req.source_id is not None:
            db.add(Movement(id=uuid.uuid4(), request_id=req.id, line_id=ln.id, item_id=ln.item_id, location_id=req.source_id, quantity_signed=-qty, posted_at=now))
            src = await _get_balance(db, req.source_id, ln.item_id)
            if src:
                src.quantity = Decimal(str(src.quantity)) - qty
        else:
            target = req.dest_id or req.source_id
            if target is None:
                continue
            db.add(Movement(id=uuid.uuid4(), request_id=req.id, line_id=ln.id, item_id=ln.item_id, location_id=target, quantity_signed=qty, posted_at=now))
            bal = await _get_balance(db, target, ln.item_id)
            if bal:
                bal.quantity = Decimal(str(bal.quantity)) + qty
            else:
                db.add(Balance(location_id=target, item_id=ln.item_id, quantity=qty, version=1))

    req.status = "posted"
    req.version += 1
    req.approver_id = user.id
    await db.commit()
    await db.refresh(req)
    return _req_out(req)


@router.post("/requests/{request_id}/reject")
async def reject_request(
    request_id: str,
    request: Request,
    payload: dict | None = None,
    user=Depends(require_any_capability("inventory:create_request", "inventory:approve_request")),
    db: AsyncSession = Depends(get_db),
):
    req = await _load_request(db, request_id)
    if req is None:
        return _err(request, 404, "NOT_FOUND", "request not found")
    caps = await get_user_capabilities(db, user.id)
    if "inventory:approve_request" not in caps:
        return _err(request, 403, "FORBIDDEN", "missing capability: inventory:approve_request")
    if req.status != "submitted":
        return _err(request, 409, "STATE_CONFLICT", f"cannot reject from {req.status}")
    conflict = _if_match(request, req.version)
    if conflict is not None:
        return conflict
    if payload and payload.get("reason"):
        req.reason = str(payload["reason"])
    req.status = "rejected"
    req.version += 1
    req.approver_id = user.id
    await db.commit()
    await db.refresh(req)
    return _req_out(req)