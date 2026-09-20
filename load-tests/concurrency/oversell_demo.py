#!/usr/bin/env python3
"""
Phase 2 Experiment: Reproduce the Flash-Sale Overselling Race Condition.

Business problem
----------------
A flash sale has limited inventory. Many clients purchase simultaneously.
The naive API does: SELECT stock -> check -> decrement in memory -> save.
Between SELECT and UPDATE, other requests read the same stale value, so the
same unit of inventory is sold more than once (overselling).

This script is the *evidence generator*: it resets stock to a small known
value, fires N concurrent purchase requests, then audits the database.

Acceptance criteria for the experiment
--------------------------------------
1. All requests get an HTTP response (no 5xx crashes required to see the bug).
2. accepted + rejected == total requests (no lost responses).
3. final_stock == initial_stock - accepted  (inventory conservation law).
   If final_stock < 0  -> OVERSOLD  (bug reproduced, Phase 2 documented).
   If final_stock == 0 -> race did NOT reproduce (increase load / retry).

Run
---
  python3 load-tests/concurrency/oversell_demo.py --stock 10 --requests 50
Requires: python3 (stdlib only), the Order API running locally, and psql
access either via docker exec or a local psql binary.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field


@dataclass
class Result:
    status: int | None = None
    body: str = ""
    error: str | None = None
    latency_ms: float = 0.0


@dataclass
class Summary:
    total: int = 0
    accepted: int = 0
    rejected: int = 0
    failed: int = 0
    status_counts: dict = field(default_factory=dict)
    latencies_ms: list = field(default_factory=list)

    def p95(self) -> float:
        if not self.latencies_ms:
            return 0.0
        s = sorted(self.latencies_ms)
        return s[min(len(s) - 1, int(0.95 * len(s)))]

def http_post(url: str, payload: dict, timeout: float = 10.0) -> "Result":
    data = json.dumps(payload).encode()
    req = urllib.request.Request(
        url, data=data, headers={"Content-Type": "application/json"}, method="POST"
    )
    start = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return Result(status=resp.status, body=resp.read().decode(),
                          latency_ms=(time.perf_counter() - start) * 1000)
    except urllib.error.HTTPError as exc:  # 4xx/5xx are expected outcomes here
        return Result(status=exc.code, body=exc.read().decode(errors="replace"),
                      latency_ms=(time.perf_counter() - start) * 1000)
    except Exception as exc:  # noqa: BLE001 - record transport failures
        return Result(error=str(exc), latency_ms=(time.perf_counter() - start) * 1000)


def fire_batch(base_url: str, product_id: int, quantity: int, n: int) -> list:
    results = [Result() for _ in range(n)]
    start_barrier = threading.Barrier(n + 1)  # +1 for the coordinator thread

    def worker(i: int) -> None:
        start_barrier.wait()  # everyone fires at the same instant
        results[i] = http_post(f"{base_url}/api/orders", {"productId": product_id, "quantity": quantity})

    threads = [threading.Thread(target=worker, args=(i,), daemon=True) for i in range(n)]
    for t in threads:
        t.start()
    start_barrier.wait()  # release the flood
    for t in threads:
        t.join(timeout=120)
    return results


def classify(results: list, summary: "Summary") -> None:
    for r in results:
        summary.total += 1
        if r.error is not None:
            summary.failed += 1
        elif r.status is not None and 200 <= r.status < 300:
            # 200 = placed (sync path), 202 = reserved + queued (async path).
            summary.accepted += 1
        elif r.status is not None and 400 <= r.status < 500:
            summary.rejected += 1
            key = str(r.status)
            summary.status_counts[key] = summary.status_counts.get(key, 0) + 1
        else:
            summary.failed += 1
        if r.latency_ms:
            summary.latencies_ms.append(r.latency_ms)


def psql_scalar(query: str, container: str | None, retries: int = 5, delay: float = 1.0) -> str:
    """Run a scalar query with retries — during a burst the server may briefly
    refuse new connections (max_connections), so audit reads must be patient."""
    cmd = (["docker", "exec", container, "psql", "-U", "postgres", "-d", "FlashSaleDb", "-At", "-c", query]
           if container else ["psql", "-At", "-c", query])
    last_err: Exception | None = None
    for attempt in range(retries):
        out = subprocess.run(cmd, capture_output=True, text=True)
        if out.returncode == 0:
            return out.stdout.strip()
        last_err = RuntimeError(out.stderr.strip() or f"psql exit {out.returncode}")
        time.sleep(delay)
    raise last_err  # type: ignore[misc]


def reset_stock(container: str | None, product_id: int, stock: int) -> None:
    psql_scalar(f'UPDATE "Products" SET "AvailableStock" = {stock} WHERE "Id" = {product_id};', container)


def read_stock(container: str | None, product_id: int) -> int:
    return int(psql_scalar(f'SELECT "AvailableStock" FROM "Products" WHERE "Id" = {product_id};', container))


def count_orders(container: str | None, product_id: int) -> int:
    return int(psql_scalar(f'SELECT COUNT(*) FROM "Orders" WHERE "ProductId" = {product_id};', container))


def main() -> int:
    ap = argparse.ArgumentParser(description="Reproduce the flash-sale overselling race condition.")
    ap.add_argument("--base-url", default="http://localhost:5065")
    ap.add_argument("--product-id", type=int, default=1)
    ap.add_argument("--quantity", type=int, default=1)
    ap.add_argument("--stock", type=int, default=10, help="Initial stock before the experiment.")
    ap.add_argument("--requests", type=int, default=50, help="Concurrent requests fired at once.")
    ap.add_argument("--psql-container", default="flash-sale-platform-postgres-1",
                    help="Docker container name running psql (empty string to use local psql).")
    ap.add_argument("--settle-seconds", type=int, default=2,
                    help="Seconds to wait before the DB audit so async fulfillment settles (Phase 5+).")
    args = ap.parse_args()
    container = args.psql_container or None

    print(f"Target              : {args.base_url}")
    print(f"Product             : #{args.product_id}")
    print(f"Initial stock       : {args.stock}")
    print(f"Concurrent requests : {args.requests}")
    print("-" * 60)

    reset_stock(container, args.product_id, args.stock)

    # Redis is the fast-path stock mirror (Phase 5+): keep it consistent with the
    # DB we just reset, via the operational runbook endpoint (ADR-003).
    try:
        resync = urllib.request.Request(
            f"{args.base_url}/internal/resync-stock/{args.product_id}", method="POST")
        urllib.request.urlopen(resync, timeout=5)
    except Exception:  # noqa: BLE001 - resync is best-effort (pre-Phase 5 API has none)
        pass

    orders_before = count_orders(container, args.product_id)
    print(f"Stock reset to {args.stock}. Orders before: {orders_before}. Firing...")

    results = fire_batch(args.base_url, args.product_id, args.quantity, args.requests)
    summary = Summary()
    classify(results, summary)

    # Phase 5+: fulfillment is asynchronous (queue + worker). Give the worker a
    # moment so the audit measures the settled end state, not the in-flight one.
    time.sleep(args.settle_seconds)

    final_stock = read_stock(container, args.product_id)
    orders_created = count_orders(container, args.product_id) - orders_before
    expected_stock = args.stock - (summary.accepted * args.quantity)

    print("-" * 60)
    print("HTTP outcomes        :")
    print(f"  200 accepted       : {summary.accepted}")
    print(f"  4xx rejected       : {summary.rejected} {summary.status_counts or ''}")
    print(f"  transport failures : {summary.failed}")
    print(f"  p95 latency        : {summary.p95():.0f} ms")
    print("Inventory audit      :")
    print(f"  initial stock      : {args.stock}")
    print(f"  accepted units     : {summary.accepted * args.quantity}")
    print(f"  expected stock     : {expected_stock}")
    print(f"  final stock in DB  : {final_stock}")
    print(f"  orders created     : {orders_created}")
    print("-" * 60)

    oversold = final_stock < 0
    accounting_broken = final_stock != expected_stock
    if oversold:
        print(f"RESULT: OVERSELLING REPRODUCED - stock went to {final_stock} (negative).")
        print("The naive read-check-write endpoint allowed more orders than inventory.")
    elif accounting_broken:
        print(f"RESULT: Race condition reproduced (lost update) - expected {expected_stock}, got {final_stock}.")
        print("Accepted responses exceeded the real inventory decrement.")
    else:
        print("RESULT: PASS - inventory conserved (no overselling, no lost updates).")
        print(f"final_stock({final_stock}) == initial_stock({args.stock}) - accepted_units({summary.accepted * args.quantity}).")

    print("\nNext step (Phase 3): make the stock decrement atomic at the database level")
    print("and re-run this exact experiment to show final_stock == expected_stock.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

