# Test Execution Report — FlashSale Backend

Cases: [`TEST_CASES.md`](TEST_CASES.md) (`FS-001`→`FS-022`).
Background: [`integration-tests.md`](integration-tests.md).
Evidence dir: [`evidence/`](evidence/). Session date: 2026-09-27 UTC.

| Date (UTC) | Command | Scope | Result | Verdict | Evidence |
|---|---|---|---|---|---|
| 2026-09-28 | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release` (.NET 10.0.401) | FS-001–009, 012–018, 020, 022 (unit slice) | **167 passed / 0 failed** (8 s) | VERIFIED ✅ | [`evidence/2026-09-28-unit-full.log`](evidence/2026-09-28-unit-full.log) |
| 2026-09-27 | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release` (.NET 10.0.401) | FS-001–009, 012–018, 020, 022 (unit slice) | **167 passed / 0 failed** (23 s) | VERIFIED ✅ | `evidence/2026-09-27-unit.log` |
| — | `dotnet test tests/IntegrationTests/` (Testcontainers PG/Redis/RabbitMQ) | FS-002/003, 010/011, 021 (51 methods) | UNVERIFIED — needs Docker; not run here | UNVERIFIED | run with Docker, record in `EVIDENCE.md` |
| — | `concurrency-harness` (50 buyers / stock 10) | FS-003 | UNVERIFIED here — CI BLOCKING gate (`RESULT: PASS`) | UNVERIFIED | CI log |
| 2026-09-27 | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release` (SDK 10.0.401 via `/usr/local/share/dotnet`; repo targets net10.0) | unit, full | **167 passed / 0 failed** (10 s) | VERIFIED ✅ | `evidence/2026-09-27-unit-full.log` |
| 2026-09-27 | `dotnet test tests/IntegrationTests/ — per-file --filter` (Testcontainers PG/Redis/RMQ, `--no-build`) | integration, per file | **60 passed / 0 failed** (OrderFlow 3, Resilience 7, AuthFlow 9, DailyReport 3, EndpointAuth 29, LowStock 4, Payment 5) | VERIFIED ✅ per-file | shell transcript (per-file; one-shot full run HANGS >600 s — DEF-FS-001) |
| — | load/soak/bench figures | FS-019, 020, 022 | UNVERIFIED (NOT A GATE — single local runs) | UNVERIFIED | — |

> SURPRISE: unit count is **167**, not the 162/162 in HARD_TEST_REPORT.md —
> 5 new tests added since; report is stale, not a regression (0 failures).

## How to record a run

1. Run `dotnet test` (add `--filter` for focused runs; full run spins ~3 containers).
2. For concurrency cases record: initial stock, concurrent clients, successes,
   rejections, final stock, invariant check — not just HTTP 200.
3. Save raw output under `evidence/YYYY-MM-DD-<scope>.log`.
4. Fill one row above; update `Status` in `TEST_CASES.md`.
5. Any FAIL/FLAKY gets an entry in `DEFECT_REPORT.md` before the run counts as reviewed.
