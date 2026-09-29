#!/usr/bin/env python3
"""E2E-FS-*: bằng chứng end-to-end cho FlashSale, trên hạ tầng thật.

Kịch bản
--------
E2E-FS-01  Zero oversell: 50 request cùng lúc trên tồn kho 10
E2E-FS-02  Idempotency: cùng idempotency key nhiều lần -> đúng một order
E2E-FS-03  Luồng bất đồng bộ: Redis reserve -> RabbitMQ -> Postgres
E2E-FS-04  Consumer chết giữa chừng rồi hồi phục, không mất và không trùng

Ranh giới
---------
Postgres, Redis và RabbitMQ đều là container thật do Testcontainers dựng, và
test gọi vào chúng qua đúng adapter mà production dùng. Không có mock ở
đâu cả. Nếu bỏ RabbitMQ đi thì E2E-FS-03/04 chỉ còn là unit test, nên
chúng được ghi là REAL chứ không phải "giả lập hàng đợi".

Cách chạy
---------
    dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj \
      --logger 'trx;LogFileName=<out>/integration.trx'
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from evidence import E2E_ROOT, EvidenceRun, refresh_hashes  # noqa: E402

REPO = Path(__file__).resolve().parent.parent.parent / "FlashSale-Backend"
TRX = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/tmp/fs-ev")

INITIAL_STOCK = 10
CONCURRENT_ATTEMPTS = 50
IDEMPOTENCY_REPEATS = 5


def parse_trx(path: Path) -> dict:
    """Đọc kết quả thật từ .trx của VSTest.

    Cần thiết vì lý do rất cụ thể: trạng thái `check(..., True)` viết tay
    không phải bằng chứng, đó là một lời khẳng định được viết ra sau khi đã
    biết kết quả. Nếu test fail lần sau, scenario vẫn báo VERIFIED vì con
    số `True` nằm cứng trong script. Ở đây mọi invariant đều lấy từ tập
    test thật đã pass trong file .trx, nên đổi code mà test rơi thì báo cáo
    cũng rơi theo.
    """
    import xml.etree.ElementTree as ET

    tree = ET.parse(path)
    root = tree.getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    by_name: dict[str, dict] = {}
    for unit in root.iterfind(".//t:UnitTestResult", ns):
        name = unit.get("testName", "")
        by_name[name] = {
            "outcome": unit.get("outcome"),
            "duration": unit.get("duration"),
        }
    passed = [n for n, r in by_name.items() if r["outcome"] == "Passed"]
    failed = [n for n, r in by_name.items() if r["outcome"] == "Failed"]
    return {
        "path": str(path),
        "total": len(by_name),
        "passed": len(passed),
        "failed": len(failed),
        "failed_names": failed[:20],
        "by_name": by_name,
    }


def scenario_ok(results: dict, suffix: str) -> bool:
    """Một scenario coi như chạy thật khi có đúng một test tên chứa hậu tố
    đó và nó PASS."""
    matches = [r for name, r in results["by_name"].items() if name.endswith(suffix)]
    return len(matches) == 1 and matches[0]["outcome"] == "Passed"


def main() -> int:
    trx_files = sorted(TRX.glob("*.trx"))
    if not trx_files:
        print(f"no .trx artifacts under {TRX}; run dotnet test first")
        return 2

    integration = parse_trx([t for t in trx_files if "integration" in t.name][0])
    unit = parse_trx([t for t in trx_files if "unit" in t.name][0])
    fs05 = TRX / "fs05-backup-restore.txt"
    fs05_ok = False
    fs05_facts: dict = {}
    if fs05.exists():
        fs05_facts = dict(
            line.split(":", 1) for line in
            fs05.read_text(encoding="utf-8").strip().splitlines()
            if ":" in line
        )
        # Cả ba mốc phải đúng: trước backup hợp lệ, mutation phá hỏng thật,
        # và restore phục hồi lại. Chỉ kiểm "restore xong" thì một restore
        # im lặng không làm gì cũng qua.
        fs05_ok = (
            fs05_facts.get("before_backup", "").strip() == "PASS"
            and fs05_facts.get("after_mutation", "").strip() == "FAIL"
            and fs05_facts.get("after_restore", "").strip() == "PASS"
            and fs05_facts.get("result", "").strip() == "PASS"
        )

    with EvidenceRun(
        "E2E-FS",
        [REPO],
        "LOCAL",
        "dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj",
        boundaries={
            "postgres": "REAL (Testcontainers postgres:15-alpine)",
            "redis": "REAL (Testcontainers redis:7-alpine)",
            "rabbitmq": "REAL (Testcontainers rabbitmq:3-management-alpine)",
            "payment-gateway": "NOT_RUN (no external payment provider in scope)",
        },
        notes="Zero oversell, idempotency, async queue flow and consumer "
              "recovery, all against real infrastructure. Payment gateway and "
              "Azure Service Bus remain unexercised.",
    ) as run:
        run.check("unit suite green", unit["failed"] == 0,
                  detail=f"{unit['passed']}/{unit['total']} passed",
                  expected="0 failed", actual=f"{unit['failed']} failed")
        run.check("integration suite green", integration["failed"] == 0,
                  detail=f"{integration['passed']}/{integration['total']} passed",
                  expected="0 failed",
                  actual=f"{integration['failed']} failed: "
                         f"{integration['failed_names']}")

        run.check(
            "E2E-FS-01 inventory conservation under "
            f"{CONCURRENT_ATTEMPTS} concurrent attempts",
            scenario_ok(integration, "ConcurrentPurchases_ConserveInventory"),
            boundary="REAL",
            expected=f"sold == {INITIAL_STOCK}, stock == 0, never negative",
            actual="see ConcurrentPurchases_ConserveInventory",
            detail="MaxDegreeOfParallelism equals the attempt count, so the "
                   "requests genuinely race instead of being serialised",
        )
        run.check(
            "E2E-FS-02 one order per idempotency key",
            scenario_ok(integration, "DuplicateDelivery_PersistsExactlyOnce"),
            boundary="REAL",
            expected=f"{IDEMPOTENCY_REPEATS} deliveries -> 1 order",
            actual="see DuplicateDelivery_PersistsExactlyOnce",
        )
        run.check(
            "E2E-FS-03 reserve -> queue -> consume converges to one order",
            scenario_ok(integration,
                        "E2E_FS_03_Reserve_Queue_Consume_ConvergesToOneOrder"),
            boundary="REAL",
            expected="stock 9, orders 1, stock + qty == 10",
            actual="see AsyncFlowE2ETests",
        )
        run.check(
            "E2E-FS-04 consumer dies mid-flight, then recovers",
            scenario_ok(integration,
                        "E2E_FS_04_ConsumerDies_ThenRecovers_WithoutDuplicating"),
            boundary="REAL",
            expected="zero orders while unprocessed, then exactly one order",
            actual="see AsyncFlowE2ETests",
            detail="asserts the intermediate state: a killed consumer must "
                   "leave zero orders, not a compensating fake one",
        )
        run.check(
            "E2E-FS-05 backup -> corrupt -> restore recovers the invariant",
            fs05_ok, boundary="REAL",
            expected="PASS before, FAIL after corruption, PASS after restore",
            actual=f"before={fs05_facts.get('before_backup', 'n/a')} "
                   f"after_mutation={fs05_facts.get('after_mutation', 'n/a')} "
                   f"after_restore={fs05_facts.get('after_restore', 'n/a')}",
            detail="pg_dump -Fc plus pg_restore --clean --if-exists against a "
                   "real postgres:15-alpine whose schema came from the "
                   "repository's own EF migrations. Plain 'pg_dump | psql' was "
                   "tried first and silently failed to recover, because every "
                   "CREATE INDEX hit 'already exists'.",
        )
        for trx in trx_files:
            # Copy vào TRONG cây evidence. Nếu để ở /tmp thì `refresh_hashes`
            # không nhìn thấy và bằng chứng không có hash nào — tức là
            # reviewer không kiểm chứng lại được.
            dest = E2E_ROOT / "flashsale" / "artifacts" / trx.name
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(trx.read_bytes())
            run.add_artifact(dest)
        run.finalise(E2E_ROOT / "flashsale")

    hashes, count = refresh_hashes()
    print(f"  hashes: {count} artifacts -> {hashes.name}")
    return 0



if __name__ == "__main__":
    raise SystemExit(main())
