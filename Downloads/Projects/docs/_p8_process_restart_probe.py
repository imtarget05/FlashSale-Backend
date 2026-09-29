#!/usr/bin/env python
"""
BẰNG CHỨNG PHASE 8 — durable thread state qua PROCESS RESTART thật.

Đây không phải unit test có mock. Script này:

    giai doan 1 (process A): tao thread, ghi state, INTERRUPT
    --- het process A, tat interpreter that ---
    giai doan 2 (process B): doc state tu Postgres, RESUME, kiem tra giong het

Cùng database, hai PID khac nhau, không process nao giu state trong RAM.
Neu state con sau khi process A chet, chung to da duoc luu that.

Dung:
    MAIA_POSTGRES_DSN='postgresql://maia:maia_test_pw@localhost:55432/maia' \
        python docs/_p8_process_restart_probe.py write
    ... python docs/_p8_process_restart_probe.py read
"""
from __future__ import annotations

import asyncio
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MAIA = os.path.join(REPO, "MAIA-clean")
sys.path.insert(0, os.path.join(MAIA, "src"))

from langgraph.graph import END, START, StateGraph  # noqa: E402
from typing import TypedDict  # noqa: E402

from maia.persistence import durable_checkpointer  # noqa: E402

THREAD_ID = "p8-restart-probe"
STATE_FILE = os.path.join(
    REPO, "docs", "evidence", "maia-p8", "restart_handoff.json")


class ProbeState(TypedDict):
    """State có 2 bước, đứng giữa ở Bước 2 để restart xảy ra đúng chỗ."""
    collected: list
    phase: str
    marker: str


def build_probe_graph():
    def collect(state: ProbeState) -> dict:
        # Bước 1: ghi dữ liệu, đánh dấu đã đi qua bước 1
        return {"collected": state.get("collected", []) + ["from-step-1"],
                "phase": "after-step-1"}

    def finish(state: ProbeState) -> dict:
        # Bước 2: chỉ chạy được nếu state đã sống sót qua restart
        return {"phase": "completed-after-restart",
                "marker": f"resumed-with-{len(state.get('collected', []))}-items"}

    builder = StateGraph(ProbeState)
    builder.add_node("collect", collect)
    builder.add_node("finish", finish)
    builder.add_edge(START, "collect")
    builder.add_edge("collect", "finish")
    builder.add_edge("finish", END)
    return builder


async def phase_write() -> int:
    """Process A: chạy tới giữa, dừng có chủ đích, để state nằm trong Postgres."""
    builder = build_probe_graph()
    async with durable_checkpointer() as saver:
        graph = builder.compile(checkpointer=saver)
        config = {"configurable": {"thread_id": THREAD_ID}}

        # Chạy TỪNG BƯỚC. invoke với interrupt_before ở 'finish' nên step 1
        # chạy xong, step 2 chưa chạy — checkpoint giữ state giữa chừng.
        result = await graph.ainvoke(
            {"collected": [], "phase": "start", "marker": ""},
            config=config,
            interrupt_before=["finish"],
        )
        snapshot = result.get("collected", [])
        handoff = {
            "thread_id": THREAD_ID,
            "pid": os.getpid(),
            "collected": list(snapshot),
            "phase": result.get("phase"),
            "marker": result.get("marker"),
        }
        os.makedirs(os.path.dirname(STATE_FILE), exist_ok=True)
        with open(STATE_FILE, "w", encoding="utf-8") as fh:
            json.dump(handoff, fh, indent=2)
        print(json.dumps(handoff, ensure_ascii=False))
    return 0


async def phase_read() -> int:
    """Process B (PID khác): đọc lại state, chạy nốt, so kết quả."""
    builder = build_probe_graph()
    with open(STATE_FILE, encoding="utf-8") as fh:
        handoff = json.load(fh)

    async with durable_checkpointer() as saver:
        graph = builder.compile(checkpointer=saver)
        config = {"configurable": {"thread_id": THREAD_ID}}

        state = await graph.aget_state(config)
        recovered = dict(state.values)

        # Chạy tiếp phần còn lại
        final = await graph.ainvoke(None, config=config)

    out = {
        "write_pid": handoff["pid"],
        "read_pid": os.getpid(),
        "processes_differ": handoff["pid"] != os.getpid(),
        "collected_before_restart": handoff["collected"],
        "recovered_from_postgres": recovered.get("collected"),
        "state_survived_restart": recovered.get("collected") == handoff["collected"],
        "final_phase": final.get("phase"),
        "final_marker": final.get("marker"),
        "resumed_correctly": final.get("phase") == "completed-after-restart",
    }
    print(json.dumps(out, indent=2, ensure_ascii=False))
    ok = (out["processes_differ"] and out["state_survived_restart"]
          and out["resumed_correctly"])
    print("RESULT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "write"
    raise SystemExit(asyncio.run(phase_write() if mode == "write" else phase_read()))
