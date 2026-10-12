"""Integrations routes — upload CSV/JSON, Sheets pull, webhook (Track B).

Nguyên tắc docs/09: import không ghi thẳng ledger. Dữ liệu dừng ở staging,
preview từng dòng, human confirm mới tạo draft đi qua approval như mọi phiếu.
"""
import csv
import hashlib
import io
import json
import re
import time
import uuid
from datetime import UTC, datetime

import httpx
from fastapi import APIRouter, Depends, File, Request, UploadFile
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.errors import AppError
from app.api.routes.auth_utils import require_any_capability
from app.config import settings
from app.database import get_db
from app.models.activity import AuditEvent, OutboxEvent
from app.models.integration import (
    IntegrationConnection,
    IntegrationRun,
    IntegrationStagingRow,
)
from app.models.inventory import InventoryRequest, InventoryRequestLine, Item, Location
from app.services import idempotency

router = APIRouter()

_MANAGE = ("integration:manage", "admin:user_management")
_READ = ("integration:manage", "admin:user_management", "activity:view", "audit:view")
_MAX_BYTES = 5 * 1024 * 1024
_MAX_ROWS = 10_000
_SHEET_ID_RE = re.compile(r"^[A-Za-z0-9-_]{10,128}$")


def _now():
    return datetime.now(UTC)


def _require_key(request: Request) -> str:
    key = request.headers.get("idempotency-key")
    if not key or not key.strip():
        raise AppError(
            400, "IDEMPOTENCY_KEY_REQUIRED",
            "Header Idempotency-Key là bắt buộc với mọi thao tác ghi (docs/06)",
        )
    return key.strip()


def _conn_out(c: IntegrationConnection) -> dict:
    return {
        "id": str(c.id),
        "type": c.type,
        "name": c.name,
        "status": c.status,
        "last_sync_at": None,
        "simulation": True,
    }


def _fingerprint(rows: list[dict]) -> str:
    canonical = json.dumps(rows, sort_keys=True, default=str)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _parse_csv(content: bytes) -> list[dict]:
    if len(content) > _MAX_BYTES:
        raise AppError(422, "IMPORT_TOO_LARGE", f"file vượt quá {_MAX_BYTES} bytes")
    text = content.decode("utf-8-sig")
    try:
        reader = csv.DictReader(io.StringIO(text))
        if not reader.fieldnames:
            raise AppError(422, "IMPORT_PARSE_ERROR", "CSV thiếu header")
        rows = [dict(r) for r in reader]
    except AppError:
        raise
    except Exception as e:
        raise AppError(422, "IMPORT_PARSE_ERROR", f"CSV không parse được: {e}") from None
    if len(rows) > _MAX_ROWS:
        raise AppError(422, "IMPORT_TOO_LARGE", f"vượt quá {_MAX_ROWS} dòng")
    return rows


def _parse_json_payload(data) -> list[dict]:
    rows = data.get("rows", data) if isinstance(data, dict) else data
    if not isinstance(rows, list) or not all(isinstance(r, dict) for r in rows):
        raise AppError(
            422, "IMPORT_PARSE_ERROR",
            "JSON phải là list objects hoặc {\"rows\": [...]}",
        )
    if len(rows) > _MAX_ROWS:
        raise AppError(422, "IMPORT_TOO_LARGE", f"vượt quá {_MAX_ROWS} dòng")
    return rows


async def _validate_item_rows(db: AsyncSession, rows: list[dict]) -> list[dict]:
    """Validate từng dòng theo sku/quantity/location — row lỗi không chặn row khác."""
    items = {i.sku: i for i in (await db.execute(select(Item))).scalars().all()}
    locs = {loc.code: loc for loc in (await db.execute(select(Location))).scalars().all()}
    out = []
    for idx, r in enumerate(rows):
        errors: list[str] = []
        sku = str(r.get("sku") or r.get("item_sku") or "").strip()
        loc_code = str(r.get("location_code") or r.get("location") or "").strip()
        try:
            qty = float(r.get("quantity", ""))
            if qty <= 0:
                errors.append("quantity phải > 0")
        except (ValueError, TypeError):
            errors.append(f"quantity không hợp lệ: {r.get('quantity')!r}")
        if not sku:
            errors.append("thiếu sku")
        elif sku not in items:
            errors.append(f"sku {sku!r} không tồn tại")
        if not loc_code:
            errors.append("thiếu location_code")
        elif loc_code not in locs:
            errors.append(f"location {loc_code!r} không tồn tại")
        key = r.get("source_row_key") or f"{sku}:{loc_code}:{idx}"
        out.append(
            {
                "row_index": idx,
                "source_row_key": str(key),
                "data": r,
                "valid": not errors,
                "errors": errors,
            }
        )
    return out


async def _stage_run(
    db: AsyncSession, connection_id, source: str, rows: list[dict],
    external_event_id: str | None = None,
) -> dict:
    """Dedup theo fingerprint; lưu staging rows; trả preview."""
    fp = _fingerprint(rows)
    dup = (
        await db.execute(
            select(IntegrationRun).where(
                IntegrationRun.connection_id == connection_id,
                IntegrationRun.source_fingerprint == fp,
            )
        )
    ).scalars().first()
    if dup is not None:
        return {"run_id": str(dup.id), "deduplicated": True, "fingerprint": fp}
    if external_event_id:
        dup_ev = (
            await db.execute(
                select(IntegrationRun).where(
                    IntegrationRun.external_event_id == external_event_id
                )
            )
        ).scalars().first()
        if dup_ev is not None:
            return {"run_id": str(dup_ev.id), "deduplicated": True}
    validated = await _validate_item_rows(db, rows)
    run = IntegrationRun(
        id=uuid.uuid4(), connection_id=connection_id, status="staged",
        source_fingerprint=fp, mapping_version=1,
        valid_count=sum(1 for v in validated if v["valid"]),
        invalid_count=sum(1 for v in validated if not v["valid"]),
        external_event_id=external_event_id,
    )
    db.add(run)
    await db.flush()
    for v in validated:
        db.add(
            IntegrationStagingRow(
                id=uuid.uuid4(), run_id=run.id,
                source_row_key=v["source_row_key"], data_json=v["data"],
                valid=v["valid"], errors_json=v["errors"],
            )
        )
    await db.commit()
    await db.refresh(run)
    return {
        "run_id": str(run.id),
        "deduplicated": False,
        "valid_count": run.valid_count,
        "invalid_count": run.invalid_count,
        "rows": validated[:200],
        "mapping_version": 1,
    }


# --------------------------------------------------------------------------
# Connections (read + tạo)
# --------------------------------------------------------------------------
@router.get("/integrations/connections")
async def list_connections(
    user=Depends(require_any_capability(*_READ)),
    db: AsyncSession = Depends(get_db),
):
    conns = (await db.execute(select(IntegrationConnection))).scalars().all()
    return {"connections": [_conn_out(c) for c in conns]}


@router.post("/integrations/connections", status_code=201)
async def create_connection(
    payload: dict,
    request: Request,
    user=Depends(require_any_capability(*_MANAGE)),
    db: AsyncSession = Depends(get_db),
):
    key = _require_key(request)
    cached = idempotency.check(key, idempotency.payload_hash(payload))
    if cached is not None:
        return cached[1]
    ctype = str(payload.get("type", "")).lower()
    if ctype not in ("csv_upload", "json_upload", "google_sheets", "apps_script"):
        raise AppError(422, "INVALID_TYPE", f"type không hỗ trợ: {ctype!r}")
    conn = IntegrationConnection(
        id=uuid.uuid4(), type=ctype,
        name=str(payload.get("name", "")) or ctype,
        status="connected",
        config_json=payload.get("config") or {},
    )
    db.add(conn)
    await db.commit()
    await db.refresh(conn)
    body = _conn_out(conn)
    idempotency.remember(db, key, idempotency.payload_hash(payload), 201, body)
    return body


# --------------------------------------------------------------------------
# Upload CSV/JSON -> staging preview
# --------------------------------------------------------------------------
@router.post("/integrations/uploads")
async def upload_file(
    request: Request,
    file: UploadFile = File(...),
    type: str = File(default="csv"),
    user=Depends(require_any_capability("integration:manage")),
    db: AsyncSession = Depends(get_db),
):
    """Nhận file CSV/JSON, validate từng dòng, trả staging preview (chưa ghi ledger)."""
    _require_key(request)
    content = await file.read()
    filename = file.filename or "upload"
    if type == "json" or filename.lower().endswith(".json"):
        try:
            data = json.loads(content.decode("utf-8-sig"))
        except (json.JSONDecodeError, UnicodeDecodeError) as e:
            raise AppError(422, "IMPORT_PARSE_ERROR", f"JSON không hợp lệ: {e}") from None
        rows = _parse_json_payload(data)
        ctype = "json_upload"
    else:
        rows = _parse_csv(content)
        ctype = "csv_upload"
    # Dùng connection mặc định theo type (tạo nếu chưa có để không chặn người dùng).
    conn = (
        await db.execute(
            select(IntegrationConnection).where(IntegrationConnection.type == ctype)
        )
    ).scalars().first()
    if conn is None:
        conn = IntegrationConnection(
            id=uuid.uuid4(), type=ctype, name=filename, status="connected"
        )
        db.add(conn)
        await db.flush()
    preview = await _stage_run(db, conn.id, filename, rows)
    preview["simulation"] = True
    return preview


@router.get("/integrations/runs/{run_id}/preview")
async def preview_run(
    run_id: str,
    user=Depends(require_any_capability(*_READ)),
    db: AsyncSession = Depends(get_db),
):
    try:
        rid = uuid.UUID(run_id)
    except ValueError:
        raise AppError(422, "INVALID_UUID", "run_id không hợp lệ") from None
    run = (
        await db.execute(select(IntegrationRun).where(IntegrationRun.id == rid))
    ).scalar_one_or_none()
    if run is None:
        raise AppError(404, "NOT_FOUND", "run không tồn tại")
    rows = (
        await db.execute(
            select(IntegrationStagingRow).where(IntegrationStagingRow.run_id == rid)
        )
    ).scalars().all()
    return {
        "run_id": str(run.id),
        "status": run.status,
        "valid_count": run.valid_count,
        "invalid_count": run.invalid_count,
        "mapping_version": run.mapping_version,
        "rows": [
            {
                "row_index": i,
                "source_row_key": r.source_row_key,
                "data": r.data_json,
                "valid": r.valid,
                "errors": r.errors_json or [],
            }
            for i, r in enumerate(rows[:200])
        ],
    }


@router.get("/integration-runs/{run_id}/staging")
async def preview_run_staging_alias(
    run_id: str,
    user=Depends(require_any_capability(*_READ)),
    db: AsyncSession = Depends(get_db),
):
    """Alias chuẩn docs/06: GET /integration-runs/{id}/staging."""
    return await preview_run(run_id=run_id, user=user, db=db)


# --------------------------------------------------------------------------
# Confirm staging -> draft inventory request (v1: all_valid)
# --------------------------------------------------------------------------
@router.post("/integrations/runs/{run_id}/confirm")
async def confirm_run(
    run_id: str,
    request: Request,
    payload: dict | None = None,
    user=Depends(require_any_capability("integration:manage")),
    db: AsyncSession = Depends(get_db),
):
    """Xác nhận run -> tạo InventoryRequest draft (maker=user) đi qua approval."""
    from decimal import Decimal

    key = _require_key(request)
    cached = idempotency.check(key, idempotency.payload_hash({"run": run_id}))
    if cached is not None:
        return cached[1]
    try:
        rid = uuid.UUID(run_id)
    except ValueError:
        raise AppError(422, "INVALID_UUID", "run_id không hợp lệ") from None
    run = (
        await db.execute(select(IntegrationRun).where(IntegrationRun.id == rid))
    ).scalar_one_or_none()
    if run is None:
        raise AppError(404, "NOT_FOUND", "run không tồn tại")
    if run.status == "confirmed":
        raise AppError(409, "STATE_CONFLICT", "run đã được confirm trước đó")
    if run.invalid_count > 0:
        raise AppError(
            409, "CONFIRM_HAS_ERRORS",
            f"run còn {run.invalid_count} dòng lỗi — sửa nguồn rồi upload lại",
        )
    rows = (
        await db.execute(
            select(IntegrationStagingRow)
            .where(IntegrationStagingRow.run_id == rid, IntegrationStagingRow.valid.is_(True))
        )
    ).scalars().all()
    if not rows:
        raise AppError(409, "CONFIRM_EMPTY", "run không có dòng hợp lệ")
    items = {i.sku: i for i in (await db.execute(select(Item))).scalars().all()}
    locs = {loc.code: loc for loc in (await db.execute(select(Location))).scalars().all()}
    # Group theo (location, source) để mỗi location một phiếu.
    by_loc: dict[str, list] = {}
    for r in rows:
        d = r.data_json
        lc = str(d.get("location_code") or d.get("location") or "").strip()
        by_loc.setdefault(lc, []).append(d)
    created: list[str] = []
    for loc_code, items_rows in by_loc.items():
        loc = locs[loc_code]
        # Tôi quyết định dùng source_id = location để phiếu nhập về đúng kho.
        req = InventoryRequest(
            id=uuid.uuid4(), kind="receipt", status="draft",
            maker_id=user.id, created_by=user.id, source_id=loc.id, version=1,
            reason=f"import run {run_id}",
        )
        db.add(req)
        await db.flush()
        for d in items_rows:
            sku = str(d.get("sku") or d.get("item_sku") or "").strip()
            item = items[sku]
            db.add(
                InventoryRequestLine(
                    id=uuid.uuid4(), request_id=req.id, item_id=item.id,
                    quantity=Decimal(str(d.get("quantity"))),
                    serial=d.get("serial"),
                )
            )
        created.append(str(req.id))
    run.status = "confirmed"
    db.add(
        AuditEvent(
            id=uuid.uuid4(), actor_id=user.id, action="integration.run.confirmed",
            subject=f"integration_run:{rid}", occurred_at=_now(),
            metadata_json={"drafts": created},
        )
    )
    db.add(
        OutboxEvent(
            id=uuid.uuid4(), event_type="integration.run.confirmed",
            aggregate_id=str(rid),
            payload_json={"subject_type": "integration_run", "subject_id": str(rid),
                          "status": "confirmed", "drafts": created},
            created_at=_now(),
        )
    )
    await db.commit()
    body = {"run_id": str(rid), "status": "confirmed", "draft_requests": created}
    idempotency.remember(db, key, idempotency.payload_hash({"run": run_id}), 200, body)
    return body


@router.post("/integration-runs/{run_id}/confirm")
async def confirm_run_alias(
    run_id: str,
    request: Request,
    payload: dict | None = None,
    user=Depends(require_any_capability("integration:manage")),
    db: AsyncSession = Depends(get_db),
):
    """Alias chuẩn docs/06: POST /integration-runs/{id}/confirm."""
    return await confirm_run(run_id=run_id, request=request, payload=payload, user=user, db=db)


# --------------------------------------------------------------------------
# Google Sheets pull (sandbox, allowlist, timeout)
# --------------------------------------------------------------------------
@router.post("/integrations/google-sheets/fetch")
async def fetch_sheet(
    payload: dict,
    request: Request,
    user=Depends(require_any_capability("integration:manage")),
    db: AsyncSession = Depends(get_db),
):
    """Kéo 1 sheet sandbox qua Sheets API v4 -> staging (không ghi ledger)."""
    key = _require_key(request)
    cached = idempotency.check(key, idempotency.payload_hash(payload))
    if cached is not None:
        return cached[1]
    sheet_id = str(payload.get("sheet_id", "")).strip()
    if not _SHEET_ID_RE.match(sheet_id):
        raise AppError(422, "INVALID_SHEET_ID", "sheet_id không hợp lệ")
    api_key = getattr(settings, "google_sheets_api_key", "") or ""
    if not api_key:
        raise AppError(
            502, "SHEETS_UNAVAILABLE",
            "GOOGLE_SHEETS_API_KEY chưa cấu hình (sandbox chưa bật)",
        )
    tab = str(payload.get("tab", "Sheet1")).strip()
    url = (
        "https://sheets.googleapis.com/v4/spreadsheets/"
        f"{sheet_id}/values/{tab}"
    )
    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            resp = await client.get(url, params={"key": api_key, "majorDimension": "ROWS"})
    except httpx.HTTPError as e:
        raise AppError(502, "SHEETS_UNAVAILABLE", f"Google Sheets không phản hồi: {e}") from None
    if resp.status_code >= 400:
        raise AppError(
            502, "SHEETS_UNAVAILABLE",
            f"Sheets API {resp.status_code}: {resp.text[:200]}",
        )
    values = resp.json().get("values", [])
    if not values:
        return {"run_id": None, "rows": [], "valid_count": 0, "invalid_count": 0, "empty": True}
    header, *body = values
    rows = [dict(zip(header, r, strict=False)) for r in body]
    conn = (
        await db.execute(
            select(IntegrationConnection).where(IntegrationConnection.type == "google_sheets")
        )
    ).scalars().first()
    if conn is None:
        conn = IntegrationConnection(
            id=uuid.uuid4(), type="google_sheets", name="Google Sheets sandbox", status="connected"
        )
        db.add(conn)
        await db.flush()
    preview = await _stage_run(db, conn.id, f"sheet:{sheet_id}!{tab}", rows)
    preview["simulation"] = True
    idempotency.remember(db, key, idempotency.payload_hash(payload), 200, preview)
    return preview


# --------------------------------------------------------------------------
# Apps Script webhook (HMAC + timestamp anti-replay + event dedup)
# --------------------------------------------------------------------------
@router.post("/integrations/webhooks/{connection_id}")
async def apps_script_webhook(
    connection_id: str,
    request: Request,
    db: AsyncSession = Depends(get_db),
):
    """Nhận push từ Apps Script: HMAC SHA-256 của raw body + timestamp ±5 phút."""
    import hashlib
    import hmac as hmac_mod

    secret = getattr(settings, "apps_script_webhook_secret", "") or ""
    if not secret:
        raise AppError(503, "WEBHOOK_DISABLED", "apps_script_webhook_secret chưa cấu hình")
    ts = request.headers.get("x-bankops-timestamp", "")
    sig = request.headers.get("x-bankops-signature", "")
    try:
        ts_int = int(ts)
    except ValueError:
        raise AppError(401, "WEBHOOK_BAD_SIGNATURE", "thiếu/sai timestamp") from None
    if abs(time.time() - ts_int) > 300:
        raise AppError(401, "WEBHOOK_REPLAY", "timestamp lệch quá 5 phút")
    raw = await request.body()
    expected = hmac_mod.new(
        secret.encode(), f"{ts}.".encode() + raw, hashlib.sha256
    ).hexdigest()
    if not hmac_mod.compare_digest(expected, sig):
        raise AppError(401, "WEBHOOK_BAD_SIGNATURE", "HMAC không khớp")
    try:
        data = json.loads(raw.decode("utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError):
        raise AppError(422, "IMPORT_PARSE_ERROR", "payload không phải JSON") from None
    rows = _parse_json_payload(data.get("rows", data) if isinstance(data, dict) else data)
    event_id = data.get("event_id") if isinstance(data, dict) else None
    conn = (
        await db.execute(
            select(IntegrationConnection).where(IntegrationConnection.id == uuid.UUID(connection_id))
        )
    ).scalar_one_or_none()
    if conn is None:
        raise AppError(404, "NOT_FOUND", "connection không tồn tại")
    preview = await _stage_run(db, conn.id, "webhook", rows, event_id)
    preview["simulation"] = True
    return preview


# --------------------------------------------------------------------------
# Runs list (màn Tích hợp đọc lịch sử)
# --------------------------------------------------------------------------
@router.get("/integrations/runs")
async def list_runs(
    user=Depends(require_any_capability(*_READ)),
    db: AsyncSession = Depends(get_db),
    limit: int = 50,
    cursor: str | None = None,
):
    limit = max(1, min(limit, 100))
    runs = (
        await db.execute(
            select(IntegrationRun)
            .order_by(IntegrationRun.created_at.desc())
            .limit(limit + 1)
        )
    ).scalars().all()
    page, next_cursor = runs[:limit], None
    if len(runs) > limit:
        import base64

        next_cursor = base64.urlsafe_b64encode(str(page[-1].id).encode()).decode()
    return {
        "runs": [
            {
                "id": str(r.id),
                "connection_id": str(r.connection_id),
                "status": r.status,
                "valid_count": r.valid_count,
                "invalid_count": r.invalid_count,
                "created_at": r.created_at.isoformat() if r.created_at else None,
            }
            for r in page
        ],
        "next_cursor": next_cursor,
        "limit": limit,
    }


