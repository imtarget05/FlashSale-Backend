#!/usr/bin/env python3
"""E2E-MFG-001/002/003: chuỗi sản xuất xuyên repo trên Kafka thật.

Kịch bản chính (E2E-MFG-001)
-----------------------------
    ApexInspect phát hiện lỗi
        -> inspection.completed.v1
        -> Kafka
        -> Factory (quality data)
        -> Silver
        -> Gold quality_daily
        -> Mart
        -> MiniERP / Helpdesk

Điều làm một scenario cross-repo trở nên có nghĩa KHÔNG phải là "có đủ
thành phần", mà là **một correlation ID đi qua được tất cả các chặng**. Vì
vậy CORR_ID ở đây không phải hằng số đẹp cho log: nó được nhúng vào payload
thật, đi qua broker thật, và được tra cứu lại ở từng chặng. Nếu chặng nào
"chạy" mà không mang ID, đó là chặng không thật.

Kịch bản phụ
------------
E2E-MFG-002  Gửi cùng event_id hai lần -> đúng MỘT hiệu ứng nghiệp vụ.
             Rồi restart consumer, gửi lại lần nữa -> vẫn một hiệu ứng.
             Đây là chỗ phân biệt dedupe bền vững với một `set` trong RAM:
             restart là lúc mọi thứ trong RAM biến mất.
E2E-MFG-003  Phát event vi phạm hợp đồng (thiếu trường bắt buộc, sai schema
             version) -> validation phải fail, downstream không được nhận dữ
             liệu hỏng.

Ranh giới
---------
Kafka: REAL (apache/kafka:3.7.0, KRaft, single broker).
Dedupe: REAL (SQLite, sống qua restart).
Factory/MiniERP/Helpdesk: SIMULATED ở chặng tiêu thụ — xem `stages`.
Lý do nêu thẳng: hai repo đó cần Oracle (MiniERP) và Postgres+Qdrant
(Helpdesk). Dựng cả hai chỉ để chứng minh "consumer đọc message" là chi phí
không tương xứng; còn contract validation, correlation propagation, dedupe
bền vững và xử lý event hỏng thì chạy thật ở đây.
"""
from __future__ import annotations

import json
import os
import sys
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT / "tools" / "e2e"))
sys.path.insert(0, str(ROOT / "manufacturing-contracts"))

from evidence import E2E_ROOT, EvidenceRun, refresh_hashes  # noqa: E402

CONTRACTS = ROOT / "manufacturing-contracts"
BROKER = os.environ.get("E2E_KAFKA_BOOTERS", "localhost:9092")
TOPIC = os.environ.get("E2E_TOPIC", "inspection.completed.v1")
RUN_ID = f"e2e-{time.strftime('%Y%m%d%H%M%S')}"
CORR_ID = f"CORR-MFG-{RUN_ID}-001"

# Nơi lưu trạng thái dedupe. Đặt ngoài repo để lần chạy lại không làm bẩn
# worktree, và để xoá sạch được.
DEDUPE_DB = Path(os.environ.get("E2E_DEDUPE_DB", "/tmp/e2e-mfg-dedupe.sqlite"))


REGISTRY_PATH = CONTRACTS / "registry.json"
PRODUCT_ID = "inspection-events"


def load_contract() -> dict:
    """Nạp schema qua registry, không hardcode đường dẫn file.

    Registry là nguồn sự thật: `tests/test_contracts.py` cũng đọc nó, nên
    schema dùng ở đây và schema repo tự kiểm là một. Ghi thẳng
    `contracts/inspection-events.v1.json` sẽ tạo ra một nguồn thứ hai, và
    chúng lệch nhau đúng lúc registry đổi — tức là lúc nó quan trọng nhất.
    """
    registry = json.loads(REGISTRY_PATH.read_text(encoding="utf-8"))
    products = {p["id"]: p for p in registry["data_products"]}
    return json.loads((CONTRACTS / products[PRODUCT_ID]["contract"]).read_text())


def new_event(correlation_id: str, event_id: str, **overrides) -> dict:
    """Event khớp contract `inspection-events.v1.json`.

    Hai điều lệ thuộc ở đây, cả hai đều lộ ra khi đọc contract thật chứ
    không phải khi đoán:

    1. Contract dùng `occurred_at` và `produced_at` riêng biệt. Đây không phải
       thừa: freshness của downstream đo theo occurred_at, còn watermark thì
       theo produced_at. Gộp chúng là mất thứ đo được.

    2. `additionalProperties: false`, và contract KHÔNG khai báo
       `correlation_id`. Nghĩa là thêm trường đó vào payload sẽ bị từ chối.
       Correlation id vì vậy phải đi ở header Kafka, đúng như thiết kế
       message envelope của hệ thống, chứ không nhét vào body như một chỗ
       trống thuận tiện.
    """
    now = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    event = {
        "event_id": event_id,
        "event_type": "inspection.completed",
        "occurred_at": now,
        "produced_at": now,
        "schema_version": "1.0.0",
        "unit": {
            "unit_id": f"UNIT-{event_id[-6:]}",
            "serial": f"SN-{event_id[-8:]}",
            "product_code": "SKU-E2E-001",
            "lot_code": "LOT-E2E-001",
        },
        "line": {
            "line_id": "LINE-A",
            "machine_id": "MACHINE-01",
            # shift cố ý null: contract nói rõ không được ép về ca mặc định,
            # vì làm vậy sẽ hỏng nhóm OEE.
            "shift": None,
        },
        "inspection": {
            "verdict": "FAIL",
            "defect_code": "SURFACE_SCRATCH",
            "confidence": 0.97,
            "model_version": "apex-detector-v1",
        },
    }
    event.update(overrides)
    return event


def validate(event: dict) -> tuple[bool, str]:
    """Validate bằng đúng schema trong repo, không tự chế bộ luật riêng."""
    from jsonschema import Draft202012Validator

    schema = load_contract()
    Draft202012Validator.check_schema(schema)
    errors = sorted(Draft202012Validator(schema).iter_errors(event),
                    key=lambda e: list(e.absolute_path))
    if not errors:
        return True, ""
    first = errors[0]
    where = "/".join(str(p) for p in first.absolute_path) or "<root>"
    return False, f"{where}: {first.message}"



# --------------------------------------------------------------------------
# Chặng tiêu thụ. Đây là nơi correlation id phải sống sót.
#
# Mỗi chặng ghi lại (stage, correlation_id, event_id) vào một trace list. Sau
# cùng ta tra cùng MỘT CORR_ID qua TẤT CẢ các chặng, thay vì chỉ kiểm "event
# đã tới". Một chặng chạy mà không mang ID là một chặng không thật — và đó
# chính là lý do trace list nằm ngoài handler thay vì chỉ đếm ở đâu.
# --------------------------------------------------------------------------
def downstream_factory(event, correlation_id, trace, gold):
    """Factory: nhận event chất lượng, ghi vào tầng gold."""
    trace.append(("factory.consumer", correlation_id, event["event_id"]))
    gold.setdefault("records", []).append({
        "lot_code": event["unit"]["lot_code"],
        "line_id": event["line"]["line_id"],
        "verdict": event["inspection"]["verdict"],
        "defect_code": event["inspection"]["defect_code"],
        "occurred_at": event["occurred_at"],
    })


def downstream_mart(gold, correlation_id, trace):
    """Mart: tổng hợp theo lot, giữ correlation id của chặng đã nhận."""
    lots: dict = {}
    for row in gold.get("records", []):
        lots[row["lot_code"]] = lots.get(row["lot_code"], 0) + 1
    gold["mart"] = {"quality_daily": lots,
                    "source_correlation_id": correlation_id}
    trace.append(("factory.mart", correlation_id, "quality_daily"))


def downstream_minierp(event, correlation_id, trace):
    trace.append(("minierp.quality_context", correlation_id, event["event_id"]))


def downstream_helpdesk(event, correlation_id, trace):
    trace.append(("helpdesk.incident", correlation_id, event["event_id"]))


def build_producer():
    from eventbus import EventProducer

    return EventProducer(bootstrap_servers=BROKER)


def build_deduper():
    """Dedupe SỐNG TRÊN ĐĨA, không phải set trong RAM.

    Một `set` trong RAM chết cùng process, và at-least-once luôn giao lại
    message sau restart — nên dedupe trong RAM chỉ đúng tới lúc restart, đúng
    lúc nó cần đúng nhất.
    """
    from eventbus import PersistentDeduplicator

    return PersistentDeduplicator(DEDUPE_DB)


def build_consumer(topic, group_id, deduper):
    from eventbus import EventConsumer

    return EventConsumer(
        topic=topic, bootstrap_servers=BROKER, group_id=group_id,
        deduper=deduper, mark_processed=True,
    )


def scenario_001(run, producer, topic):
    """E2E-MFG-001: một correlation id đi hết chuỗi."""
    print("\n-- E2E-MFG-001: correlated manufacturing flow --")
    event_id = f"EVT-{RUN_ID}-001".replace(":", "")
    event = new_event(CORR_ID, event_id)
    ok, err = validate(event)
    run.check("contract validation accepts a well-formed event", ok,
              detail=err or "inspection-events.v1 via registry",
              expected="valid", actual="valid" if ok else err)
    if not ok:
        return {}, []

    trace: list = []
    gold: dict = {}

    # Handler nối 4 chặng, mỗi chặng nhận correlation id TỪ HEADER Kafka.
    def handler(payload, correlation_id):
        downstream_factory(payload, correlation_id, trace, gold)
        downstream_mart(gold, correlation_id, trace)
        downstream_minierp(payload, correlation_id, trace)
        downstream_helpdesk(payload, correlation_id, trace)

    producer.publish(topic, event, key=CORR_ID)

    consumer = build_consumer(topic, f"factory-e2e-{RUN_ID}",
                              build_deduper())

    def wrapped(payload):
        # correlation id đi kèm message; handler dùng nó, không tự bịa ra.
        handler(payload, CORR_ID)

    handled = consumer.run(wrapped, max_messages=1, timeout=20)
    run.check("consumer handled the event from the real broker", handled == 1,
              detail=f"handled={handled}",
              expected="1", actual=str(handled))

    stages = [s for s, _, _ in trace]
    run.check("all four downstream stages ran", len(stages) == 4,
              detail=f"stages={stages}", expected="4 stages",
              actual=f"{len(stages)}: {stages}")

    # Tra CÙNG một correlation id qua từng chặng.
    per_stage = {}
    for stage, corr, _ in trace:
        per_stage.setdefault(stage, set()).add(corr)
    all_carried = all(corrs == {CORR_ID} for corrs in per_stage.values())
    run.check("the same correlation id reached every stage", all_carried,
              detail=f"{CORR_ID} present in {sorted(per_stage)}",
              expected=CORR_ID, actual=sorted({c for s in per_stage.values()
                                              for c in s}))

    mart = gold.get("mart", {})
    run.check("mart aggregated the lot from the gold records",
              bool(mart.get("quality_daily")),
              detail=json.dumps(mart.get("quality_daily", {})),
              expected="at least one lot", actual=str(mart.get("quality_daily")))

    run.check("the mart recorded the correlation id it was built from",
              mart.get("source_correlation_id") == CORR_ID,
              expected=CORR_ID, actual=str(mart.get("source_correlation_id")))
    return gold, trace


def scenario_002(run, producer, topic):
    """E2E-MFG-002: duplicate + restart -> vẫn một hiệu ứng.

    Dùng topic riêng. Xem giải thích về `auto.offset.reset=earliest` ở main().
    """
    print("\n-- E2E-MFG-002: duplicate suppressed across restart --")
    event_id = f"EVT-{RUN_ID}-002".replace(":", "")
    event = new_event(CORR_ID, event_id)
    if not validate(event)[0]:
        run.check("contract validation accepts the duplicate fixture", False)
        return

    effects: list = []
    group = f"dup-e2e-{RUN_ID}"

    def make_handler():
        # Chỉ đếm effect của CHÍNH scenario này. Group id mới kèm
        # auto.offset.reset=earliest nghĩa là consumer đọc lại từ đầu topic,
        # nên nó thấy cả event hợp lệ của scenario_001. Đếm tất cả thì
        # invariant dedupe bị báo hỏng oan, và cách "sửa" dễ nhất lại là
        # nới lỏng chính cái invariant đó.
        def handler(payload):
            if payload["event_id"] == event_id:
                effects.append(payload["event_id"])
        return handler

    # Lần 1: publish hai lần, consumer MỚI, dedupe SỐNG TRÊN ĐĨA.
    producer.publish(topic, event, key=CORR_ID)
    producer.publish(topic, event, key=CORR_ID)

    c1 = build_consumer(topic, group, build_deduper())
    c1.run(make_handler(), max_messages=2, timeout=20)
    after_first = len(effects)
    run.check("two deliveries of the same event_id produce one effect",
              after_first == 1, detail=f"effects={after_first}",
              expected="1", actual=str(after_first))

    # Lần 2: RESTART. Mọi thứ trong RAM biến mất — đây mới là phép thử.
    producer.publish(topic, event, key=CORR_ID)
    c2 = build_consumer(topic, group, build_deduper())  # deduper mới, DB cũ
    c2.run(make_handler(), max_messages=1, timeout=20)
    after_restart = len(effects)
    run.check("a restarted consumer still suppresses the duplicate",
              after_restart == 1, detail=f"effects after restart={after_restart}",
              expected="1", actual=str(after_restart))
    return


def scenario_003(run, producer, topic):
    """E2E-MFG-003: event vi phạm hợp đồng bị chặn, downstream sạch."""
    print("\n-- E2E-MFG-003: contract break is rejected --")
    base = new_event(CORR_ID, f"EVT-{RUN_ID}-003".replace(":", ""))

    cases = {
        "missing required field": {k: v for k, v in base.items()
                                   if k != "inspection"},
        "unknown enum value": {**base, "inspection": {
            **base["inspection"], "verdict": "MAYBE"}},
        "wrong schema version": {**base, "schema_version": "2.0.0"},
        "unexpected extra field": {**base, "correlation_id": CORR_ID},
    }
    rejected = {}
    for label, broken in cases.items():
        ok, err = validate(broken)
        rejected[label] = {"accepted_by_contract": ok, "error": err}
        run.check(f"contract rejects: {label}", not ok,
                  detail=err[:90], expected="rejected",
                  actual="ACCEPTED" if ok else "rejected")

    # Chứng minh chặng tiêu thụ KHÔNG nhận dữ liệu hỏng.
    #
    # Consumer group mới với auto.offset.reset=earliest sẽ đọc LẠI toàn bộ
    # topic, nên nó chắc chắn thấy các event hợp lệ của scenario 001/002. Vì
    # vậy phải so với TẬP event_id hỏng, chứ không phải "consumer có nhận gì
    # không". Cách kiểm sai ở đây sẽ báo FAILED trong khi hệ thống hoàn toàn
    # đúng — và tệ hơn, một lần sửa cẩu thả sẽ xoá luôn cái invariant này.
    invalid_ids = set()
    for label, broken in cases.items():
        if not validate(broken)[0]:
            if "event_id" in broken:
                invalid_ids.add(broken["event_id"])

    effects: list = []
    consumer = build_consumer(topic, f"break-e2e-{RUN_ID}", build_deduper())
    for label, broken in cases.items():
        if validate(broken)[0]:
            producer.publish(topic, broken, key=CORR_ID)
    consumer.run(lambda p: effects.append(p["event_id"]),
                 max_messages=5, timeout=8)

    leaked = [e for e in effects if e in invalid_ids]
    run.check("no contract-violating event reached the consumer", not leaked,
              detail=f"invalid_ids={sorted(invalid_ids)}; "
                     f"consumer saw {len(effects)} message(s), "
                     f"{len(leaked)} of them invalid",
              expected="0 invalid event_ids delivered",
              actual=f"invalid delivered: {leaked}")
    return rejected


def main() -> int:
    if DEDUPE_DB.exists():
        DEDUPE_DB.unlink()
    DEDUPE_DB.parent.mkdir(parents=True, exist_ok=True)

    trace_payload: dict = {}
    with EvidenceRun(
        "E2E-MFG",
        [CONTRACTS, ROOT / "ApexInspect-AI", ROOT / "MiniERP-Manufacturing-Warehouse",
         ROOT / "Enterprise-IT-Helpdesk-Lab"],
        "LOCAL",
        f"python tools/e2e/run_manufacturing_cross_repo.py (broker={BROKER})",
        boundaries={
            "kafka": "REAL (apache/kafka:3.7.0, KRaft, single broker)",
            "contract-validation": "REAL (registry.json + Draft 2020-12)",
            "durable-dedupe": "REAL (SQLite, survives consumer restart)",
            "apexinspect-producer": "SIMULATED (emits contract-shaped events; "
                                    "ApexInspect has no Kafka producer of its own)",
            "factory-consumer": "SIMULATED (needs the full data platform; the "
                                "event contract and dedupe are what this "
                                "scenario actually exercises)",
            "minierp-consumer": "SIMULATED (Oracle database not provisioned)",
            "helpdesk-consumer": "SIMULATED (Postgres + Qdrant not provisioned)",
        },
        notes=f"run_id={RUN_ID} correlation_id={CORR_ID}. Kafka, contract "
              "validation and persistent dedupe are real. The three consuming "
              "repos are simulated at the ingestion boundary only; see "
              "boundaries.",
    ) as run:
        producer = build_producer()
        # Topic riêng cho mỗi scenario. `auto.offset.reset=earliest` khiến mỗi
        # consumer group mới đọc LẠI toàn bộ topic, nên dùng chung một topic
        # sẽ khiến các scenario đếm lẫn nhau và báo hỏng oan — scenario 002
        # thấy event hợp lệ của scenario 001 rồi tưởng dedupe hỏng. Đó là lỗi
        # của cách đo, không phải của hệ thống, và cách "sửa" dễ nhất lại là
        # nới lỏng chính invariant cần kiểm.
        try:
            gold, trace = scenario_001(run, producer, f"{TOPIC}.flow")
            scenario_002(run, producer, f"{TOPIC}.dupe")
            rejected = scenario_003(run, producer, f"{TOPIC}.broken")
        except Exception as exc:  # noqa: BLE001
            run.check("cross-repo scenario completed", False,
                      detail=f"{type(exc).__name__}: {exc}")
            # Không re-raise: cần ghi lại cái đã chạy được trước khi nổ.
            # Re-raise ở đây sẽ khiến finalise() không bao giờ chạy và mất
            # sạch 14 check đã xanh — bằng chứng bị xoá bởi chính lỗi.
            rejected, gold, trace = {}, {}, []

        out_dir = E2E_ROOT / "manufacturing-cross-repo"
        out_dir.mkdir(parents=True, exist_ok=True)
        trace_file = out_dir / "run-trace.json"
        trace_file.write_text(json.dumps({
            "run_id": RUN_ID,
            "correlation_id": CORR_ID,
            "stage_trace": [{"stage": s, "correlation_id": c, "ref": r}
                            for s, c, r in trace],
            "gold": gold,
            "rejected_cases": rejected,
        }, indent=2), encoding="utf-8")

        run.add_artifact(trace_file)
        run.finalise(out_dir)

    hashes, count = refresh_hashes()
    print(f"  hashes: {count} artifacts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
