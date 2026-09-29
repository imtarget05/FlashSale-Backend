#!/usr/bin/env python
"""
BẰNG CHỨNG: persistent dedupe sống qua PROCESS RESTART thật.

Không mock. Script chạy HAI PROCESS RIÊNG BIỆT:

  process A: consume event, ghi id vào SQLite, thoát
  --- process A chết hoàn toàn ---
  broker: giao lại CHÍNH event đó (seek offset 0)
  process B: consume lại, phải BỎ QUA vì đã xử lý

Đây là thứ nâng claim từ "in-process dedupe" lên "restart-safe idempotent
consumption". Chỉ đọc code sẽ không chứng minh được điều đó.

Chạy:
  EVENTS_BOOTSTRAP_SERVERS='localhost:59092' P12_TOPIC=t P12_GROUP=g \
      MAIA-clean/.venv-maia/bin/python docs/_p12b_durable_dedupe_probe.py write
  ... cùng lệnh ... read
"""
from __future__ import annotations

import json
import os
import sys
import uuid

REPO = "/Users/mainguyenbinhtan/Downloads/Projects"
sys.path.insert(0, os.path.join(REPO, "manufacturing-contracts"))

from eventbus import EventConsumer, EventProducer, PersistentDeduplicator  # noqa: E402

TOPIC = os.environ.get("P12_TOPIC", "inspection-events")
GROUP = os.environ.get("P12_GROUP", "durable-dedupe-probe")
DEDUPE_DB = os.environ.get("P12_DEDUPE_DB", "/tmp/p12_dedupe.db")
HANDOFF = "/tmp/p12_dedupe_handoff.json"


def make_event(eid: str) -> dict:
    return {
        "event_id": eid,
        "event_type": "inspection.completed",
        "occurred_at": "2026-09-29T08:00:00Z",
        "produced_at": "2026-09-29T08:00:01Z",
        "schema_version": "1.0.0",
        "unit": {"unit_id": "U-1", "serial": "SN-1",
                 "product_code": "PCB-A", "lot_code": "LOT-9"},
        "line": {"line_id": "L1", "machine_id": "M1"},
        "inspection": {"verdict": "PASS", "defect_code": None,
                       "confidence": 0.97, "model_version": "probe"},
    }


def phase_write() -> int:
    """Process A: produce 1 event, consume nó, lưu id vào SQLite."""
    eid = f"p12-durable-{uuid.uuid4().hex[:12]}"
    p = EventProducer()
    p.publish(TOPIC, make_event(eid), key=eid)
    p.flush(15.0)

    handled: list = []
    deduper = PersistentDeduplicator(DEDUPE_DB)
    c = EventConsumer(TOPIC, group_id=GROUP, deduper=deduper)
    c.run(lambda e: handled.append(e), max_messages=1, timeout=25.0)
    c.close()
    stats = c.stats()
    deduper.close()

    with open(HANDOFF, "w", encoding="utf-8") as fh:
        json.dump({"event_id": eid, "pid": os.getpid(), "stats": stats}, fh)
    print(json.dumps({"phase": "write", "event_id": eid, "pid": os.getpid(),
                      "handled": stats["handled"],
                      "persisted": stats["handled"]}, ensure_ascii=False))
    return 0 if stats["handled"] == 1 else 1


def phase_read() -> int:
    """Process B (PID khác): broker giao lại event, phải bị suppress."""
    from confluent_kafka import TopicPartition

    with open(HANDOFF, encoding="utf-8") as fh:
        ho = json.load(fh)

    deduper = PersistentDeduplicator(DEDUPE_DB)
    restored = deduper.restored_count

    handled: list = []
    c = EventConsumer(TOPIC, group_id=GROUP, deduper=deduper)
    c._c.poll(2.0)          # hoàn tất rebalance
    c._c.seek(TopicPartition(TOPIC, 0, 0))   # cưỡng ép giao lại từ đầu
    c.run(lambda e: handled.append(e), max_messages=1, timeout=25.0)
    stats = c.stats()
    c.close()
    deduper.close()

    out = {
        "phase": "read",
        "write_pid": ho["pid"],
        "read_pid": os.getpid(),
        "processes_differ": ho["pid"] != os.getpid(),
        "dedupe_restored_ids": restored,
        "event_id": ho["event_id"],
        "handled_after_restart": stats["handled"],
        "duplicates_suppressed": stats["duplicates_suppressed"],
        "suppressed": stats["handled"] == 0 and stats["duplicates_suppressed"] >= 1,
    }
    out["result"] = "PASS" if (out["processes_differ"] and out["suppressed"]) else "FAIL"
    print(json.dumps(out, indent=2, ensure_ascii=False))
    return 0 if out["result"] == "PASS" else 1


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "write"
    raise SystemExit(phase_write() if mode == "write" else phase_read())
