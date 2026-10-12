"""Idempotency-Key store (docs/06:3, docs/10 A3/A4).

`db` được nhận để sau này chuyển sang bảng Postgres mà caller không phải sửa:
hiện tại in-memory per-process (đủ cho demo + test), production cần store dùng
chung giữa các replica và sống sót qua restart.

Hợp đồng:
- replay cùng key + cùng payload -> trả lại status/body đã lưu (A3)
- cùng key + khác payload       -> raise IdempotencyConflict -> 409 IDEMPOTENCY_KEY_REUSED (A4)
"""
import hashlib
import json
import time
from typing import Any

_store: dict[str, tuple[str, int, dict, float]] = {}  # key -> (hash, status, body, ts)
_RETENTION_SECONDS = 24 * 3600  # khớp idempotency_key_retention_hours (docs/04)


def payload_hash(payload: Any) -> str:
    """Hash canonical (sorted keys) — thứ tự key trong JSON không được phép
    làm retry trở thành 'payload khác' (A3 phải replay, không phải 409)."""
    canonical = json.dumps(payload, sort_keys=True, default=str)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


class IdempotencyConflict(Exception):
    """Cùng key nhưng payload khác (A4) — không được ghi thêm."""

    def __init__(self, existing_status: int, existing_body: dict):
        self.existing_status = existing_status
        self.existing_body = existing_body
        super().__init__("Idempotency-Key đã dùng cho payload khác")


def _purge_expired() -> None:
    now = time.time()
    for key in [k for k, (*_, ts) in _store.items() if now - ts > _RETENTION_SECONDS]:
        del _store[key]


def check(key: str, body_hash: str) -> tuple[int, dict] | None:
    """Trả (status, body) để replay nếu key đã dùng; None nếu là request mới."""
    _purge_expired()
    entry = _store.get(key)
    if entry is None:
        return None
    stored_hash, status, body, _ts = entry
    if stored_hash != body_hash:
        raise IdempotencyConflict(status, body)
    return status, body


def remember(db: Any, key: str, body_hash: str, status: int, body: dict) -> None:
    """Lưu kết quả sau khi transaction đã commit thành công.

    `db` chưa dùng tới (in-memory), chỉ để tương lai thay bằng UPSERT vào
    `idempotency_keys` mà không phải sửa 5 call site.
    """
    _store[key] = (body_hash, status, body, time.time())
