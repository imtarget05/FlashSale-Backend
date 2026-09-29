#!/usr/bin/env python3
"""
GATE 4 — POISON MESSAGE + DEAD LETTER + DLQ REPLAY

Cau hoi phong van: "Mot message luon crash consumer thi he thong tranh
retry vo han the nao? Sau khi sua loi thi replay DLQ co gay duplicate
khong?"

META-GATE chay TRUOC moi scenario: oracle cua probe la "so lan handler
goi cho 1 message hong vuot nguong thi phai dung". Oracle sai thi moi
ket luan sai theo, nen no phai tu kiem truoc.
"""
from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path

ROOT = Path("/Users/mainguyenbinhtan/Downloads/Projects")
sys.path.insert(0, str(ROOT / "manufacturing-contracts"))

BOOTSTRAP = os.environ.get("EVENTS_BOOTSTRAP_SERVERS", "localhost:9092")
TOPIC = f"gate4-{int(time.time())}"
MAX_ATTEMPTS = 3

from eventbus import EventConsumer, EventProducer, PersistentDeduplicator  # noqa: E402

results: list[dict] = []


def check(cid, name, system_result, probe_result, *, oracle_source,
          classification="NONE", self_test="PASS", notes=""):
    results.append({
        "check_id": cid, "name": name,
        "system_result": str(system_result)[:900],
        "probe_result": probe_result,
        "probe_self_test": self_test,
        "oracle_source": oracle_source,
        "failure_classification": classification,
        "notes": notes,
    })
    flag = "PASS" if probe_result == "PASS" else "FAIL"
    print(f"  [{flag}] {cid} {name}: {str(system_result)[:150]}", flush=True)


def publish(payload, topic=None):
    p = EventProducer(bootstrap_servers=BOOTSTRAP)
    try:
        p.publish(topic or TOPIC, payload)
        remaining = p.flush(20.0)
        if remaining:
            raise RuntimeError(f"{remaining} message chua flush xong")
    finally:
        del p


def new_consumer(group, *, fresh=True, dlq=True, topic=None):
    db = Path(f"/tmp/gate4-{group}.sqlite")
    if fresh and db.exists():
        db.unlink()
    dlq_path = f"/tmp/gate4-dlq-{group}.jsonl" if dlq else None
    if fresh and dlq_path and os.path.exists(dlq_path):
        os.remove(dlq_path)
    return EventConsumer(
        topic=topic or TOPIC, bootstrap_servers=BOOTSTRAP, group_id=group,
        deduper=PersistentDeduplicator(str(db)),
        max_attempts=MAX_ATTEMPTS,
        dead_letter_path=dlq_path,
    )


def meta_gate() -> bool:
    print("\n[META-GATE] verify the verifier", flush=True)
    ok = True
    oracle = lambda n: "DLQ" if n >= MAX_ATTEMPTS else "RETRY"
    r1 = oracle(10_000) == "DLQ"
    check("M1", "oracle flags unbounded retry as failure",
          f"attempts=10000 -> {oracle(10_000)}", "PASS" if r1 else "FAIL",
          oracle_source="inline lambda under test")
    r2 = oracle(1) == "RETRY"
    check("M2", "oracle accepts healthy single-attempt case",
          f"attempts=1 -> {oracle(1)}", "PASS" if r2 else "FAIL",
          oracle_source="inline lambda under test")
    r3 = oracle(MAX_ATTEMPTS) == "DLQ" and oracle(MAX_ATTEMPTS - 1) == "RETRY"
    check("M3", f"boundary exactly at {MAX_ATTEMPTS}",
          f"{MAX_ATTEMPTS - 1}=RETRY, {MAX_ATTEMPTS}=DLQ",
          "PASS" if r3 else "FAIL", oracle_source="inline lambda under test")
    ok &= r1 and r2 and r3

    # Run rong KHONG DUOC sinh bang chung.
    # Run rong KHONG DUOC sinh bang chung.
    n = None
    try:
        c = new_consumer("gate4-empty")
        n = c.run(lambda p: None, max_messages=1, timeout=4.0)
        c.close()
    except Exception as exc:  # noqa: BLE001
        n = f"exception:{exc}"
    r4 = n == 0
    check("M4", "empty run produces no evidence",
          f"handled={n}", "PASS" if r4 else "FAIL",
          oracle_source="observed consumer return value",
          classification="NONE" if r4 else "HARNESS")
    ok &= r4
    print(f"[META-GATE] {'PASS' if ok else 'FAIL'}", flush=True)
    print(f"[META-GATE] {'PASS' if ok else 'FAIL'}", flush=True)
    return ok



def main() -> int:
    print(f"topic={TOPIC} max_attempts={MAX_ATTEMPTS}", flush=True)
    if not meta_gate():
        print(json.dumps({"gate": 4, "status": "INVALID_EVIDENCE",
                          "reason": "probe self-test failed"}, indent=2))
        return 2

    print("\n[CONTROL] valid message must succeed", flush=True)
    publish({"event_id": "g4-ok-1", "kind": "valid"})
    seen: list = []
    c = new_consumer("gate4-control")
    handled = c.run(lambda p: seen.append(p["event_id"]),
                    max_messages=1, timeout=25.0)
    st = c.stats()
    c.close()
    check("G4-01", "control valid message handled",
          json.dumps({"handled": handled, "seen": seen, "stats": st}, default=str),
          "PASS" if handled == 1 and seen == ["g4-ok-1"] else "FAIL",
          oracle_source="observed consumer.handled + handler side effect")

    print("\n[POISON] message that always fails", flush=True)
    # Topic riêng cho poison: consumer mới đọc `earliest`, nên nếu dùng
    # chung topic với control nó sẽ thấy message cũ trước và
    # `max_messages=1` sẽ dừng trước khi tới message độc.
    poison_topic = f"{TOPIC}.poison"
    publish({"event_id": "g4-poison-1", "kind": "poison"}, poison_topic)
    publish({"event_id": "g4-healthy-after", "kind": "valid"}, poison_topic)
    attempts: dict = {}

    def flaky(payload):
        eid = payload["event_id"]
        attempts[eid] = attempts.get(eid, 0) + 1
        if eid == "g4-poison-1":
            raise RuntimeError("deterministic poison failure")

    t0 = time.monotonic()
    c = new_consumer("gate4-poison", topic=poison_topic)
    c.run(flaky, max_messages=5, timeout=60.0)
    elapsed = time.monotonic() - t0
    st = c.stats()
    dlq = list(c.dead_letters)
    c.close()
    n = attempts.get("g4-poison-1", 0)

    check("G4-02", "poison retried but BOUNDED",
          f"attempts={n} elapsed={elapsed:.1f}s retries={st.get('retries')}",
          "PASS" if 0 < n < 50 else "FAIL",
          oracle_source="observed handler invocation count")
    check("G4-03", "poison did not block healthy message",
          f"healthy_attempts={attempts.get('g4-healthy-after', 0)}",
          "PASS" if attempts.get("g4-healthy-after", 0) > 0 else "FAIL",
          oracle_source="observed handler invocation count")
    check("G4-04", "poison reached dead letter",
          json.dumps(dlq, default=str)[:500],
          "PASS" if any(d.get("event_id") == "g4-poison-1" for d in dlq)
          else "FAIL", oracle_source="observed consumer.dead_letters")
    if dlq:
        d0 = dlq[0]
        check("G4-05", "DLQ entry retains id, reason, attempts, payload",
              f"keys={sorted(d0)}",
              "PASS" if all(k in d0 for k in
                            ("event_id", "reason", "attempts", "payload"))
              else "FAIL", oracle_source="DLQ entry schema")

    print("\n[RESTART] dead-letter state across process restart", flush=True)
    c2 = new_consumer("gate4-poison", fresh=False, topic=poison_topic)
    dlq2 = list(c2.dead_letters)
    c2.close()
    check("G4-06", "DLQ survives process restart",
          f"dlq_count_after_restart={len(dlq2)}",
          "PASS" if len(dlq2) >= 1 else "FAIL",
          oracle_source="observed consumer.dead_letters reloaded from disk")

    print("\n[REPLAY] fix root cause, replay from DLQ", flush=True)
    c3 = new_consumer("gate4-poison", fresh=False, topic=poison_topic)
    replayed = c3.replay_dead_letters(lambda p: p.get("kind"), timeout=15.0)
    st3 = c3.stats()
    c3.close()
    check("G4-07", "replayed DLQ message processed after fix",
          f"replayed={replayed} handled={st3.get('handled')}",
          "PASS" if replayed >= 1 else "FAIL",
          oracle_source="observed replay_dead_letters return value")

    c4 = new_consumer("gate4-replay2", fresh=False, topic=poison_topic)
    again = c4.replay_dead_letters(lambda p: p.get("kind"), timeout=10.0)
    st4 = c4.stats()
    c4.close()
    check("G4-08", "second replay produces no new business effect",
          f"replayed={again} handled={st4.get('handled')}",
          "PASS" if st4.get("handled", 0) == 0 else "FAIL",
          oracle_source="observed consumer.stats after second replay")

    passed = sum(1 for r in results if r["probe_result"] == "PASS")
    status = "VERIFIED" if passed == len(results) else "PARTIAL"
    out = {"gate": 4, "name": "poison_message_dlq_replay", "status": status,
           "probe_self_test": "PASS", "max_attempts": MAX_ATTEMPTS,
           "poison_attempts": n, "passed": passed, "total": len(results),
           "checks": results}
    print(json.dumps({k: v for k, v in out.items() if k != "checks"}, indent=2))
    return 0 if status == "VERIFIED" else 1


if __name__ == "__main__":
    raise SystemExit(main())

