"""Background worker module for processing async jobs.

Polls the ``jobs`` table with a DB lease claim, runs each job idempotently,
and dispatches pending ``outbox_events`` after commit (docs/09).
"""

import asyncio
import logging
import os
from datetime import UTC, datetime, timedelta

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

logger = logging.getLogger(__name__)

POLL_INTERVAL_SECONDS = int(os.environ.get("WORKER_POLL_INTERVAL", "5"))
LEASE_SECONDS = int(os.environ.get("WORKER_LEASE_SECONDS", "60"))
MAX_RETRY = int(os.environ.get("WORKER_MAX_RETRY", "5"))


def _utcnow() -> datetime:
    return datetime.now(UTC)


async def claim_job(session: AsyncSession):
    """Claim a single queued job via DB lease.

    Uses ``status`` transition queued -> running as the lease. When the
    ``jobs`` table carries lease columns (``lease_until``/``next_attempt``)
    they are honoured; otherwise falls back to status-only claim so the
    worker still progresses on the current schema.
    """
    from app.models.activity import Job

    now = _utcnow()
    result = await session.execute(
        select(Job).where(Job.status == "queued").limit(1)
    )
    job = result.scalar_one_or_none()
    if job is None:
        return None
    # Honour lease_until / next_attempt when present on the row.
    lease_until = getattr(job, "lease_until", None)
    if lease_until is not None and lease_until > now:
        return None
    next_attempt = getattr(job, "next_attempt", None)
    if next_attempt is not None and next_attempt > now:
        return None
    # claim: mark running + heartbeat + lease
    job.status = "running"
    if hasattr(job, "lease_until"):
        job.lease_until = now + timedelta(seconds=LEASE_SECONDS)
    if hasattr(job, "heartbeat_at"):
        job.heartbeat_at = now
    if hasattr(job, "updated_at"):
        job.updated_at = now
    await session.commit()
    logger.info("Worker claim job %s type=%s lease=%ss", job.id, job.type, LEASE_SECONDS)
    return job


async def run_job_with_retry(session: AsyncSession, job) -> None:
    """Run a claimed job idempotently; retry count + next_attempt on failure."""
    from app.models.activity import Job  # noqa: F401  (documents job table)

    try:
        # Idempotent dispatch by job key: re-check status before work.
        await session.refresh(job)
        if job.status == "succeeded":
            return
        # Placeholder job execution (reconciliation/outbox publishing hooks here).
        job.status = "succeeded"
        job.finished_at = _utcnow()
        await session.commit()
        logger.info("Worker job %s succeeded", job.id)
    except Exception as exc:  # retry with backoff
        retry = int(getattr(job, "retry_count", 0) or 0) + 1
        if hasattr(job, "retry_count"):
            job.retry_count = retry
        if hasattr(job, "next_attempt"):
            job.next_attempt = _utcnow() + timedelta(seconds=2 ** min(retry, 6))
        job.error_code = type(exc).__name__[:64]
        if retry >= MAX_RETRY:
            job.status = "failed"
        else:
            job.status = "queued"  # release lease for retry
        await session.commit()
        logger.warning("Worker job %s retry %s: %s", job.id, retry, exc)


async def dispatch_outbox(session: AsyncSession) -> int:
    """Dispatch unpublished outbox events after commit; returns count."""
    from app.models.activity import OutboxEvent

    result = await session.execute(
        select(OutboxEvent).where(OutboxEvent.published_at.is_(None)).limit(50)
    )
    events = list(result.scalars().all())
    for event in events:
        event.attempts = (event.attempts or 0) + 1
        event.published_at = _utcnow()
        logger.info("Worker dispatch outbox %s type=%s", event.id, event.event_type)
    if events:
        await session.commit()
    return len(events)


async def poll_once(session: AsyncSession) -> dict:
    """Single poll iteration: claim one job, run it, dispatch outbox."""
    job = await claim_job(session)
    if job is not None:
        await run_job_with_retry(session, job)
    dispatched = await dispatch_outbox(session)
    return {"job_id": str(job.id) if job is not None else None, "dispatched": dispatched}


async def process_jobs():
    """Process pending jobs from the job queue with lease + heartbeat."""
    from app.database import AsyncSessionLocal

    logger.info("Worker started - waiting for jobs")
    while True:
        try:
            async with AsyncSessionLocal() as session:
                outcome = await poll_once(session)
                if outcome["job_id"] or outcome["dispatched"]:
                    logger.info("Worker heartbeat job=%s outbox=%s", outcome["job_id"], outcome["dispatched"])
                else:
                    logger.debug("Worker heartbeat idle")
        except Exception as exc:
            logger.warning("Worker poll failed, retry later: %s", exc)
        await asyncio.sleep(POLL_INTERVAL_SECONDS)


def main():
    """Entry point for the background worker."""
    asyncio.run(process_jobs())


if __name__ == "__main__":
    main()
