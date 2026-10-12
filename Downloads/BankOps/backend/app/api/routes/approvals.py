"""Approvals inbox routes."""
from fastapi import APIRouter, Depends
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth_utils import require_capability
from app.database import get_db
from app.models.cash_sim import CashRequest
from app.models.iam import User
from app.models.inventory import InventoryRequest, InventoryRequestLine

router = APIRouter()


@router.get("/inbox")
async def get_approval_inbox(
    current_user=Depends(require_capability("approval:view_inbox")),
    db: AsyncSession = Depends(get_db),
):
    """Get pending approvals for current user (cần capability approve — docs/06)."""
    users = {
        str(u.id): (u.display_name_demo or u.username)
        for u in (await db.execute(select(User))).scalars().all()
    }
    inv_rows = (
        await db.execute(
            select(InventoryRequest)
            .where(InventoryRequest.status == "submitted")
            .order_by(InventoryRequest.submitted_at.asc())
        )
    ).scalars().all()
    cash_rows = (
        await db.execute(
            select(CashRequest)
            .where(CashRequest.status == "submitted")
            .order_by(CashRequest.submitted_at.asc())
        )
    ).scalars().all()

    approvals = []
    for r in inv_rows:
        line_count = (
            await db.execute(
                select(func.count()).select_from(InventoryRequestLine).where(
                    InventoryRequestLine.request_id == r.id
                )
            )
        ).scalar_one()
        maker_id = str(r.maker_id)
        approvals.append(
            {
                "id": str(r.id),
                "kind": "inventory",
                "sub_kind": r.kind,
                "status": r.status,
                "maker_id": maker_id,
                "maker": users.get(maker_id, maker_id),
                "impact": f"Kho ({r.kind}) · {line_count} dòng vật tư",
                "detail": r.reason or f"Phiếu kho {r.kind}",
            }
        )

    for r in cash_rows:
        maker_id = str(r.maker_id)
        approvals.append(
            {
                "id": str(r.id),
                "kind": "cash",
                "sub_kind": r.kind,
                "status": r.status,
                "maker_id": maker_id,
                "maker": users.get(maker_id, maker_id),
                "impact": f"Quỹ ({r.kind}) · {r.amount} VND",
                "detail": r.reason or f"Phiếu quỹ {r.kind}",
            }
        )
    return {"approvals": approvals}