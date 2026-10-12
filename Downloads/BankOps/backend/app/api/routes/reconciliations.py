"""Reconciliation routes (R1, docs/05).

Snapshot expected từ ledger/movements so với read model.
Sai lệch tạo exception để xử lý — KHÔNG tự vá (auto_fixed luôn False/None).
"""
import uuid

from fastapi import APIRouter, Depends, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.errors import AppError
from app.api.routes.auth_utils import require_any_capability
from app.database import get_db
from app.models.cash_sim import CashEntry, CashJournal
from app.models.inventory import Balance, Movement
from app.models.reconciliation import ReconciliationException, ReconciliationRun
from app.services import idempotency

router = APIRouter()

_RECON_CAPS = ("cash:reconcile", "audit:view", "approval:view_inbox")
_SUBJECT_CASH = "cash_ledger"
_SUBJECT_INV = "inventory_ledger"


def _require_key(request: Request) -> str:
    key = request.headers.get("idempotency-key")
    if not key or not key.strip():
        raise AppError(
            400, "IDEMPOTENCY_KEY_REQUIRED",
            "Header Idempotency-Key là bắt buộc với mọi thao tác ghi (docs/06)",
        )
    return key.strip()


@router.post("/reconciliations", status_code=201)
async def run_reconciliation(
    payload: dict,
    request: Request,
    user=Depends(require_any_capability(*_RECON_CAPS)),
    db: AsyncSession = Depends(get_db),
):
    scope = str(payload.get("scope", "cash")).lower()
    if scope not in ("cash", "inventory"):
        raise AppError(422, "INVALID_SCOPE", f"scope phải là cash|inventory, nhận {scope!r}")
    key = _require_key(request)
    cached = idempotency.check(key, idempotency.payload_hash(payload))
    if cached is not None:
        return cached[1]

    run = ReconciliationRun(
        id=uuid.uuid4(), scope=scope, status="running", auto_fixed=False
    )
    db.add(run)
    await db.flush()

    exceptions: list[dict] = []
    if scope == "cash":
        journals = (await db.execute(select(CashJournal))).scalars().all()
        for j in journals:
            entries = (
                await db.execute(select(CashEntry).where(CashEntry.journal_id == j.id))
            ).scalars().all()
            dr = sum(float(e.debit) for e in entries)
            cr = sum(float(e.credit) for e in entries)
            if abs(dr - cr) > 1e-9:
                exceptions.append(
                    {
                        "subject": f"journal:{j.id}",
                        "expected": {"debit": dr, "credit": dr},
                        "actual": {"debit": dr, "credit": cr},
                    }
                )
    else:
        moves = (await db.execute(select(Movement))).scalars().all()
        sums: dict[tuple, float] = {}
        for m in moves:
            k = (str(m.location_id), str(m.item_id))
            sums[k] = sums.get(k, 0.0) + float(m.quantity_signed)
        bals = (await db.execute(select(Balance))).scalars().all()
        bal_map = {(str(b.location_id), str(b.item_id)): float(b.quantity) for b in bals}
        for k, expected in sums.items():
            actual = bal_map.get(k)
            if actual is None or abs(expected - actual) > 1e-6:
                exceptions.append(
                    {
                        "subject": f"balance:{k[0]}:{k[1]}",
                        "expected": {"quantity": expected},
                        "actual": {"quantity": actual},
                    }
                )

    for e in exceptions:
        db.add(
            ReconciliationException(
                id=uuid.uuid4(), run_id=run.id, subject=e["subject"],
                expected_json=e["expected"], actual_json=e["actual"],
            )
        )
    run.status = "exception" if exceptions else "matched"
    await db.commit()
    await db.refresh(run)
    body = {
        "id": str(run.id),
        "scope": run.scope,
        "status": run.status,
        "auto_fixed": False,
        "exceptions": [
            {"subject": e["subject"], "expected": e["expected"], "actual": e["actual"]}
            for e in exceptions
        ],
    }
    idempotency.remember(db, key, idempotency.payload_hash(payload), 201, body)
    return body


@router.get("/reconciliations")
async def list_reconciliations(
    user=Depends(require_any_capability(*_RECON_CAPS)),
    db: AsyncSession = Depends(get_db),
    limit: int = 50,
    cursor: str | None = None,
):
    """Danh sách runs + exceptions (màn Báo cáo đọc ở đây)."""
    limit = max(1, min(limit, 100))
    runs = (
        await db.execute(
            select(ReconciliationRun)
            .order_by(ReconciliationRun.created_at.desc())
            .limit(limit + 1)
        )
    ).scalars().all()
    page, next_cursor = runs[:limit], None
    if len(runs) > limit:
        import base64

        next_cursor = base64.urlsafe_b64encode(str(page[-1].id).encode()).decode()
    out = []
    for r in page:
        excs = (
            await db.execute(
                select(ReconciliationException).where(ReconciliationException.run_id == r.id)
            )
        ).scalars().all()
        out.append(
            {
                "id": str(r.id),
                "scope": r.scope,
                "status": r.status,
                "auto_fixed": r.auto_fixed,
                "exceptions": [
                    {
                        "subject": e.subject,
                        "expected": e.expected_json,
                        "actual": e.actual_json,
                    }
                    for e in excs
                ],
            }
        )
    return {"runs": out, "next_cursor": next_cursor, "limit": limit}
