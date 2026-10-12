"""Reports routes — aggregation readonly cho biểu đồ ngày/tháng (Track A).

Mọi endpoint là read-only trên ledger/movement đã post: không tạo write,
không đổi số dư. Decimal giữ dạng string tới client để không mất chính xác.
"""
from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth_utils import require_any_capability
from app.database import get_db
from app.models.cash_sim import CashEntry, CashJournal
from app.models.inventory import Balance, InventoryRequest, Item, Movement

router = APIRouter()

_READ_CAPS = (
    "inventory:view_balances",
    "inventory:manage_catalog",
    "cash:view_balances",
    "cash:reconcile",
    "audit:view",
    "approval:view_inbox",
)

_WATCH_DEFAULT = 30
_STAGNANT_DEFAULT = 60
_CRITICAL_DEFAULT = 90


def _parse_date(raw: str | None, field: str) -> datetime | None:
    if not raw:
        return None
    try:
        dt = datetime.fromisoformat(raw)
    except ValueError:
        from app.api.errors import AppError

        raise AppError(422, "INVALID_DATE", f"{field} phải ISO-8601, nhận {raw!r}") from None
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=UTC)
    return dt


def _severity(days: int | None, watch: int, stagnant: int, critical: int) -> str:
    if days is None:
        return "critical"
    if days >= critical:
        return "critical"
    if days >= stagnant:
        return "stagnant"
    if days >= watch:
        return "watch"
    return "normal"

@router.get("/inventory-daily")
async def inventory_daily(
    current_user=Depends(require_any_capability(*_READ_CAPS)),
    db: AsyncSession = Depends(get_db),
    date_from: str | None = Query(default=None),
    date_to: str | None = Query(default=None),
):
    """Nhập/xuất/tồn theo ngày từ Movement đã post (nguồn sự thật)."""
    from datetime import date
    from decimal import Decimal

    df, dt = _parse_date(date_from, "date_from"), _parse_date(date_to, "date_to")
    stmt = select(Movement)
    if df:
        stmt = stmt.where(Movement.posted_at >= df)
    if dt:
        stmt = stmt.where(Movement.posted_at <= dt)
    moves = (await db.execute(stmt)).scalars().all()
    buckets: dict[str, dict] = {}
    for m in moves:
        day = (m.posted_at.date() if m.posted_at else date.today()).isoformat()
        b = buckets.setdefault(
            day,
            {"inbound": Decimal("0"), "outbound": Decimal("0"), "net": Decimal("0")},
        )
        qty = Decimal(str(m.quantity_signed))
        if qty >= 0:
            b["inbound"] += qty
        else:
            b["outbound"] += -qty
        b["net"] += qty
    # Đếm phiếu theo ngày tạo để đối chiếu với movement.
    req_stmt = select(InventoryRequest)
    if df:
        req_stmt = req_stmt.where(InventoryRequest.created_at >= df)
    if dt:
        req_stmt = req_stmt.where(InventoryRequest.created_at <= dt)
    reqs = (await db.execute(req_stmt)).scalars().all()
    counts: dict[str, dict] = {}
    for r in reqs:
        day = (r.created_at.date() if r.created_at else date.today()).isoformat()
        c = counts.setdefault(day, {"receipt": 0, "issue": 0})
        if r.kind == "receipt":
            c["receipt"] += 1
        elif r.kind == "issue":
            c["issue"] += 1
    out = []
    for day in sorted(set(buckets) | set(counts)):
        b = buckets.get(
            day, {"inbound": Decimal("0"), "outbound": Decimal("0"), "net": Decimal("0")}
        )
        c = counts.get(day, {"receipt": 0, "issue": 0})
        out.append(
            {
                "bucket": day,
                "inbound_qty": str(b["inbound"]),
                "outbound_qty": str(b["outbound"]),
                "net_qty": str(b["net"]),
                "receipt_count": c["receipt"],
                "issue_count": c["issue"],
            }
        )
    return {"buckets": out, "unit": "quantity", "simulation": True}


@router.get("/inventory-monthly")
async def inventory_monthly(
    current_user=Depends(require_any_capability(*_READ_CAPS)),
    db: AsyncSession = Depends(get_db),
    date_from: str | None = Query(default=None),
    date_to: str | None = Query(default=None),
):
    """Gộp buckets ngày thành tháng (YYYY-MM)."""
    daily = await inventory_daily(current_user, db, date_from, date_to)
    months: dict[str, dict] = {}
    from decimal import Decimal

    for b in daily["buckets"]:
        key = b["bucket"][:7]
        m = months.setdefault(
            key,
            {
                "inbound": Decimal("0"),
                "outbound": Decimal("0"),
                "net": Decimal("0"),
                "receipt": 0,
                "issue": 0,
            },
        )
        m["inbound"] += Decimal(b["inbound_qty"])
        m["outbound"] += Decimal(b["outbound_qty"])
        m["net"] += Decimal(b["net_qty"])
        m["receipt"] += b["receipt_count"]
        m["issue"] += b["issue_count"]
    return {
        "buckets": [
            {
                "bucket": k,
                "inbound_qty": str(v["inbound"]),
                "outbound_qty": str(v["outbound"]),
                "net_qty": str(v["net"]),
                "receipt_count": v["receipt"],
                "issue_count": v["issue"],
            }
            for k, v in sorted(months.items())
        ],
        "unit": "quantity",
        "simulation": True,
    }


@router.get("/cashflow-daily")
async def cashflow_daily(
    current_user=Depends(require_any_capability(*_READ_CAPS)),
    db: AsyncSession = Depends(get_db),
    date_from: str | None = Query(default=None),
    date_to: str | None = Query(default=None),
):
    """Thu/chi theo ngày từ journal đã post + entries (double-entry)."""
    from datetime import date
    from decimal import Decimal

    df, dt = _parse_date(date_from, "date_from"), _parse_date(date_to, "date_to")
    stmt = select(CashJournal)
    if df:
        stmt = stmt.where(CashJournal.posted_at >= df)
    if dt:
        stmt = stmt.where(CashJournal.posted_at <= dt)
    journals = (await db.execute(stmt)).scalars().all()
    buckets: dict[str, dict] = {}
    for j in journals:
        day = (j.posted_at.date() if j.posted_at else date.today()).isoformat()
        entries = (
            await db.execute(select(CashEntry).where(CashEntry.journal_id == j.id))
        ).scalars().all()
        b = buckets.setdefault(
            day, {"in_": Decimal("0"), "out": Decimal("0"), "n": 0, "is_receipt": 0}
        )
        for e in entries:
            # SIM_CASH debit = tiền vào quỹ; SIM_CASH credit = tiền ra quỹ.
            if e.account_code.startswith("SIM_CASH:"):
                b["in_"] += Decimal(str(e.debit))
                b["out"] += Decimal(str(e.credit))
        b["n"] += 1
    out = []
    for day in sorted(buckets):
        b = buckets[day]
        out.append(
            {
                "bucket": day,
                "total_in": str(b["in_"]),
                "total_out": str(b["out"]),
                "net": str(b["in_"] - b["out"]),
                "journal_count": b["n"],
            }
        )
    return {"buckets": out, "currency": "VND", "simulation": True}


@router.get("/cashflow-monthly")
async def cashflow_monthly(
    current_user=Depends(require_any_capability(*_READ_CAPS)),
    db: AsyncSession = Depends(get_db),
    date_from: str | None = Query(default=None),
    date_to: str | None = Query(default=None),
):
    """Gộp cashflow ngày thành tháng."""
    daily = await cashflow_daily(current_user, db, date_from, date_to)
    months: dict[str, dict] = {}
    from decimal import Decimal

    for b in daily["buckets"]:
        key = b["bucket"][:7]
        m = months.setdefault(
            key, {"in_": Decimal("0"), "out": Decimal("0"), "n": 0}
        )
        m["in_"] += Decimal(b["total_in"])
        m["out"] += Decimal(b["total_out"])
        m["n"] += b["journal_count"]
    return {
        "buckets": [
            {
                "bucket": k,
                "total_in": str(v["in_"]),
                "total_out": str(v["out"]),
                "net": str(v["in_"] - v["out"]),
                "journal_count": v["n"],
            }
            for k, v in sorted(months.items())
        ],
        "currency": "VND",
        "simulation": True,
    }


@router.get("/stagnation")
async def stagnation(
    current_user=Depends(require_any_capability(*_READ_CAPS)),
    db: AsyncSession = Depends(get_db),
    watch_days: int = Query(default=_WATCH_DEFAULT),
    stagnant_days: int = Query(default=_STAGNANT_DEFAULT),
    critical_days: int = Query(default=_CRITICAL_DEFAULT),
):
    """Hàng tồn đọng: balance>0 + số ngày từ lần issue gần nhất."""
    from datetime import date
    from decimal import Decimal

    balances = (await db.execute(select(Balance))).scalars().all()
    items = {
        str(i.id): i for i in (await db.execute(select(Item))).scalars().all()
    }
    # Lần issue gần nhất theo (item): max posted_at của movement âm.
    last_issue: dict[str, date] = {}
    moves = (
        await db.execute(
            select(Movement).where(Movement.quantity_signed < 0)
        )
    ).scalars().all()
    for m in moves:
        if not m.posted_at:
            continue
        key = str(m.item_id)
        day = m.posted_at.date()
        if key not in last_issue or day > last_issue[key]:
            last_issue[key] = day
    today = date.today()
    out = []
    for b in balances:
        qty = Decimal(str(b.quantity))
        if qty <= 0:
            continue
        key = str(b.item_id)
        item = items.get(key)
        last = last_issue.get(key)
        days = (today - last).days if last else None
        out.append(
            {
                "item_id": key,
                "sku": item.sku if item else key,
                "name": item.name if item else key,
                "quantity": str(qty),
                "days_since_last_issue": days,
                "severity": _severity(days, watch_days, stagnant_days, critical_days),
            }
        )
    out.sort(
        key=lambda r: (r["days_since_last_issue"] is not None, r["days_since_last_issue"] or 0),
        reverse=True,
    )
    return {
        "items": out,
        "thresholds": {"watch": watch_days, "stagnant": stagnant_days, "critical": critical_days},
        "simulation": True,
    }
