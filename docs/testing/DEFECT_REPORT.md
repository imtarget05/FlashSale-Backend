# Defect Report — FlashSale Backend

| ID | Severity | Title | Repro | Evidence | Status |
|---|---|---|---|---|---|
| _(none this session)_ | — | Unit suite 167/167 green 2026-09-27; no failures observed | `TEST_EXECUTION_REPORT.md` 2026-09-27 row | `evidence/2026-09-27-unit.log` | — |
| HIST-FS-001 | S3 (ops, documented) | RabbitMQ 512M container limit trips the memory alarm under load (mgmt healthcheck timeouts; AMQP still OK for smoke) | On disk: `docker-compose.yml:61` (`memory: 512M`), `:134` (`# Localhost note (2026-09-27): 512M trips RabbitMQ's …`), `plans/plan-20260923-2349-local-v2-platform.md:96` (RAM 498/512MiB → restart), sizing in `docs/adr/007-container-runtime-strategy.md:23` | files cited | MITIGATED (documented limit + restart runbook; needs live Azure round-trip per ADR-005 to close) |
| DEF-FS-001 | P2 (suite reliability) | Full integration one-shot run HANGS (>600 s, killed, no results) while every file passes individually (60/60). Suspect parallel Testcontainers contention (shared `flashsale` collection fixture + 50-context tests + RabbitMQ). Masks real regressions in CI if it also hangs there | 2026-09-27: one-shot `dotnet test tests/IntegrationTests/` → timeout, only build warnings in log; per-file `--filter` runs all green | `evidence/2026-09-27-integration-full.log` (truncated) + shell transcript | OPEN (try `xunit.runner.json` maxParallelThreads=1 or per-class containers; verify CI doesn't hang the same way) |
| DEF-FS-002 | P0 (scale gap) | FS-003 exact scenario unproven: 100 stock / 1000 simultaneous buyers. Max proven: harness 50/10 + property ≤200 attempts/16 parallelism | no 100/1000 test on disk | `TEST_CASES.md` FS-003 | OPEN |
| DEF-FS-003 | P0 (coverage gap) | FS-011 (DB down → no fake success) never executed: no DB-down test in ResilienceTests; LiveInfraGuard is a skip-helper, not a test | ResilienceTests node list 2026-09-27 (7 tests, none DB-down) | shell transcript | OPEN |
| DEF-FS-004 | P0 (coverage gap) | FS-021 (injected latency → no double order) has no test | grep latency-injection in `tests/` → none | `TEST_CASES.md` FS-021 | OPEN |

## Lifecycle

`OPEN → FIXED` (with re-test evidence) or `OPEN → MITIGATED` (workaround +
root-cause tracking ID) or `→ WONTFIX` (justification required for P0/P1).
Every defect links the failing case ID from `TEST_CASES.md`.
