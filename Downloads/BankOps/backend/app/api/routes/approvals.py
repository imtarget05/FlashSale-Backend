"""Approvals inbox routes."""
from fastapi import APIRouter, Depends
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.routes.auth_utils import require_capability
from app.database import get_db
from app.models.cash_sim import CashRequest
from app.models.inventory import InventoryRequest

router = APIRouter()


@router.get("/inbox")
async def get_approval_inbox(
    current_user = Depends(require_capability("approval:view_inbox")),
    db: AsyncSession = Depends(get_db),
):
    """Get pending approvals for current user (cần capability approve — docs/06)."""
    inv_rows = (
        await db.execute(
            select(InventoryRequest).where(InventoryRequest.status == "submitted")
        )
    ).scalars().all()
    cash_rows = (
        await db.execute(
            select(CashRequest).where(CashRequest.status == "submitted")
        )
    ).scalars().all()
    approvals = [
        {"id": str(r.id), "kind": "inventory", "status": r.status} for r in inv_rows
    ]
    approvals += [
        {"id": str(r.id), "kind": "cash", "status": r.status} for r in cash_rows
    ]
    return {"approvals": approvals}