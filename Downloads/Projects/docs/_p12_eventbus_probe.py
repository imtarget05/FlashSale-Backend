#!/usr/bin/env python
"""
BẰNG CHỨNG PHASE 12 — produce -> broker -> consume, chạy thật qua Redpanda.

Không mock broker. Script này:
    1. produce N message inspection-events vao topic that
    2. consume chung mot topic do, xac nhan nhan duoc
    3. gui lai message trung event_id, xac nhan consumer bo qua
    4. lam handler fail mot lan, xac nhan khong commit offset va retry duoc
    5. kiem tra so message thuc su nam trong topic tren broker

Dung:
    EVENTS_BOOTSTRAP_SERVERS='localhost:59092' python docs/_p12_eventbus_probe.py
"""
from __future__ import annotations

import json
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTRACTS = os.path.join(REPO, "manufacturing-contracts")
sys.path.insert(0, CONTRACTS)

from confluent_kafka import TopicPartition  # noqa: E402

from eventbus import (  # noqa: E402
    Deduplicator,
    EventConsumer,
    EventProducer,
    bootstrap_from_env,
)

TOPIC = os.environ.get("P12_TOPIC", "inspection-events")
GROUP = os.environ.get("P12_GROUP", "p12-probe-group")


def make_event(i: int) -> dict:
    return {
        "event_id": f"p12-probe-{i:04d}",
        "event_type": "inspection.completed",
        "occurred_at": "2026-09-29T08:00:00Z",
        "produced_at": "2026-09-29T08:00:01Z",
        "schema_version": "1.0.0",
        "unit": {"unit_id": f"U-{i}", "serial": f"SN-{i}",
                 "product_code": "PCB-A", "lot_code": "LOT-9"},
        "line": {"line_id": "L1", "machine_id": "M1"},
        "inspection": {"verdict": "PASS", "defect_code": None,
                       "confidence": 0.97, "model_version": "probe-1"},
    }


def ensure_topic(topic: str) -> bool:
    """Tạo topic nếu chưa có, qua external address. True nếu topic sẵn sàng."""
    from confluent_kafka.admin import AdminClient, NewTopic
    a = AdminClient({"bootstrap.servers": bootstrap_from_env()})
    md = a.list_topics(timeout=10)
    if topic in md.topics:
        return True
    f = a.create_topics([NewTopic(topic, num_partitions=1,
                                  replication_factor=1)])[topic]
    f.result(timeout=20)
    return True


def broker_message_count(topic: str) -> int:
    """
    Đếm message thật trong topic trên broker, không phải đếm phía client.

    Dùng AdminClient từ host: `rpk` trong container phải resolve được tên
    broker nội bộ, còn từ host ta nối external address.
    """
    from confluent_kafka import Consumer, TopicPartition
    from confluent_kafka.admin import AdminClient

    a = AdminClient({"bootstrap.servers": bootstrap_from_env()})
    md = a.list_topics(timeout=10)
    if topic not in md.topics:
        return -1

    # High watermark = số message broker đã giữ, tức là những gì thực sự
    # nằm trên topic chứ không phải những gì client tưởng đã gửi.
    c = Consumer({"bootstrap.servers": bootstrap_from_env(),
                  "group.id": f"hw-probe-{os.getpid()}"})
    try:
        lo, hi = c.get_watermark_offsets(
            TopicPartition(topic, 0), timeout=10, cached=False)
        return int(hi - lo)
    finally:
        c.close()


def main() -> int:
    report: dict = {"topic": TOPIC,
                    "bootstrap": bootstrap_from_env()}

    report["topic_ready"] = ensure_topic(TOPIC)

    # ---------------------------------------------------------------- produce
    p = EventProducer()
    n = 6
    for i in range(n):
        p.publish(TOPIC, make_event(i), key=f"U-{i}")
    remaining = p.flush(15.0)
    report["produced"] = n
    report["flush_remaining"] = remaining
    report["flush_clean"] = remaining == 0

    # ---------------------------------------------------------------- consume
    got: list[dict] = []
    c = EventConsumer(TOPIC, group_id=GROUP)
    handled = c.run(lambda e: got.append(e), max_messages=n, timeout=25.0)
    first_stats = c.stats()
    first_ids = c.deduper.seen_ids()
    c.close()
    report["consumed_first_pass"] = handled
    report["unique_event_ids"] = len({e["event_id"] for e in got})
    report["first_pass_duplicates_suppressed"] = first_stats["duplicates_suppressed"]

    # -------------------------------------------------- duplicate suppression
    # Ngữ nghĩa at-least-once nghĩa là broker CÓ THỂ giao lại message đã
    # commit. Để chứng minh consumer chịu được việc đó, ta dùng LẠI group
    # đã commit xong, seek về offset 0 — cưỡng ép giao lại toàn bộ — và
    # truyền lại DEDUPER ĐÃ CÓ TỪ LẦN TRƯỚC.
    #
    # Truyền lại deduper là mô phỏng state bền vững. Nếu ta tạo deduper
    # mới ở đây thì mọi message đều "lạ" và không test được gì cả.
    shared_deduper = Deduplicator.restore(first_ids)
    replay_events: list[dict] = []
    replay = EventConsumer(TOPIC, group_id=GROUP, deduper=shared_deduper)
    # `seek` chỉ hợp lệ SAU khi consumer đã được gán partition. poll() một
    # lần để hoàn tất rebalance rồi mới seek.
    replay._c.poll(2.0)
    replay._c.seek(TopicPartition(TOPIC, 0, 0))
    replay.run(lambda e: replay_events.append(e), max_messages=n, timeout=25.0)
    replay_stats = replay.stats()
    replay.close()
    report["duplicate_replay"] = {
        "note": "cung group + seek offset 0 + deduper da co: cuong ep giao lai toan bo",
        "ids_carried_over": len(first_ids),
        "handled": replay_stats["handled"],
        "duplicates_suppressed": replay_stats["duplicates_suppressed"],
        "suppressed_everything": replay_stats["handled"] == 0,
    }

    # ------------------------------------------------------ failure & recovery
    attempts = {"count": 0}

    def flaky(payload: dict) -> None:
        attempts["count"] += 1
        if attempts["count"] == 1:
            raise RuntimeError("giả lập sự cố handler lần đầu")

    rec = EventConsumer(TOPIC, group_id=f"{GROUP}-recover")
    rec.run(flaky, max_messages=1, timeout=25.0)
    rec.close()
    report["failure_recovery"] = {
        "handler_attempts": attempts["count"],
        "retried_after_failure": attempts["count"] >= 2,
        "first_run_stats": rec.stats(),
    }

    # ------------------------------------------------------------ broker-side
    report["broker_high_watermark"] = broker_message_count(TOPIC)
    report["broker_has_messages"] = report["broker_high_watermark"] >= n

    ok = (report["flush_clean"]
          and report["consumed_first_pass"] == n
          and report["unique_event_ids"] == n
          and report["duplicate_replay"]["suppressed_everything"]
          and report["failure_recovery"]["retried_after_failure"]
          and report["broker_has_messages"])

    report["result"] = "PASS" if ok else "FAIL"
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
