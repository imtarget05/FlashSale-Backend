# Test Cases — FlashSale Backend (High Concurrency)

**Plan:** [`TEST_PLAN.md`](TEST_PLAN.md). **Contract:** `docs/qa/QA_ACCEPTANCE.md`.
Background: [`integration-tests.md`](integration-tests.md). Unit on disk: 110
`[Fact]`+`[Theory]` (167 executed); Integration: 51 methods. ✅ = ran green 2026-09-27.

| ID | file::case (on disk) | Invariant asserted | Runnable offline? |
|---|---|---|---|
| FS-001 | `OrderProcessorTests.cs` (4) ✅ | stock 10−1=9 | ✅ 167/167 unit |
| FS-002 | `InventoryConservationPropertyTests.cs` (1 Theory) ✅ + `OrderFlowTests.cs` (3, integration) | exactly 1 success, stock ≥ 0 | unit ✅; integration needs Docker |
| FS-003 | `concurrency-harness` (CI BLOCKING: 50 buyers/stock 10 → exactly 10) + conservation Theory ✅ | sold ≤ stock, final = 0 | ✅ unit slice; harness = CI |
| FS-004 | `StockResyncTests.cs` (5) ✅ zero-stock path | all rejected, 0 orders | ✅ |
| FS-005 | `ArchitectureTests.cs` (3) ✅ + application DTO validation | negative qty rejected | ✅ |
| FS-006 | `StockResyncTests.cs` retry/idempotency path ✅ | retry → stock −1 once | ✅ |
| FS-007 | Redis Lua CAS path (`ArchitectureTests` + `MessagingProviderSelectorTests` 11+2 ✅) | reservations ≤ stock | ✅ (logic vs fakes) |
| FS-008 | `InventoryConservationPropertyTests` ✅ | DB stock never `<0` | ✅ |
| FS-009 | `StockResyncUseCase.cs` + `StockResyncTests.cs` ✅ | Redis✓/DB✗ → compensation | ✅ |
| FS-010 | `ResilienceTests.cs` (5+1, integration) + ADR-003 fallback | Redis down → sync DB path, no oversell | needs Docker |
| FS-011 | `LiveInfraGuard.cs` + `ResilienceTests.cs` | DB down → busy error, no fake success | needs Docker |
| FS-012 | `OutboxPoisonTests.cs` (2) ✅ + `OutboxBackoffTests.cs` (7+2) ✅ | crash-after-commit → retry, no dup | ✅ |
| FS-013 | `StockResyncTests.cs` dedup path ✅ | duplicate event → exactly-once effect | ✅ |
| FS-014 | `OutboxPoisonTests.cs` ✅ | poison → DLQ, queue flows | ✅ |
| FS-015 | `StockResyncTests.cs` drift path ✅ | Redis>DB → resync to PG truth | ✅ |
| FS-016 | `StockResyncTests.cs` drift path ✅ | Redis<DB → resync correct | ✅ |
| FS-017 | `InventoryConservationPropertyTests` ✅ | resync-during-buy holds conservation | ✅ |
| FS-018 | `StockResyncTests.cs` repeated-resync path ✅ | idempotent resync | ✅ |
| FS-019 | `docs/benchmarks/phase5-tiered-experiment.md` (no test file) | P95 29 ms tiered vs 574 ms naive | UNVERIFIED (NOT A GATE) |
| FS-020 | `InventoryConservationPropertyTests` ✅ | initial == remaining + sold | ✅ |
| FS-021 | `LiveInfraGuard.cs` + `ResilienceTests.cs` | latency → no double order | needs Docker |
| FS-022 | ADR-014 pool-headroom (80/100) + `OutboxBackoffTests.cs` ✅ | retry storm → no oversell | ✅ unit slice |

## Per-file inventory (UnitTests)

| File | Fact/Theory | Notes |
|---|---|---|
| `AdminBootstrapTests` 12+2, `AuthServiceTests` 11+1, `Pbkdf2PasswordHasherTests` 5+1, `EndpointAuthorizationGuardTests` 4+1 | 36 | auth/bootstrap guards |
| `AutomationDashboardTests` 2, `AutomationEventProcessorTests` 10+1, `CheckoutSagaTests` 5, `LowStockRuleTests` 1+1, `PaymentTimeoutRuleTests` 7, `ReportWindowTests` 2 | 29 | saga/automation rules |
| `MessagingProviderSelectorTests` 11+2, `MessagingRegistrationGuardTests` 6+1 | 20 | messaging selection |
| `OutboxBackoffTests` 7+2, `OutboxPoisonTests` 2, `StockResyncTests` 5, `OrderProcessorTests` 4, `InventoryConservationPropertyTests` 0+1, `ArchitectureTests` 3 | 24 | reliability core |
| `AuthTestDoubles`, `FakeOrderRepository`, `SagaTestDoubles`, `LiveInfraGuard` | 0 | fakes/guards (no tests) |

**Property invariant (assert on DB state, never just HTTP 200):** see `TEST_PLAN.md`.

## Full-run verdicts 2026-09-27 (SDK 10.0.401 — repo targets net10.0; SDK 8 on PATH cannot build it)

- Unit: **167 passed** re-verified (`evidence/2026-09-27-unit-full.log`).
- Integration (Testcontainers, per-file — one-shot full run HANGS, see DEF-FS-001):
  OrderFlow 3, Resilience 7, AuthFlow 9, DailyReport 3, EndpointAuthorization 29,
  LowStockAutomation 4, PaymentAutomation 5 = **60 passed, 0 failed**. "Needs Docker"
  rows above are now VERIFIED-local except:
- FS-003 → scale GAP (max proven: 50 buyers/stock-10 harness + property ≤200 attempts/
  16 parallel; spec 100 stock / 1000 buyers unproven — DEF-FS-002, P0).
- FS-011 → GAP (no DB-down test executed; ResilienceTests list has RedisDown but no
  DB-down case; LiveInfraGuard is a skip-helper, not a test — DEF-FS-003, P0).
- FS-019 → P1 exception stands (benchmark doc only).
- FS-020 → PASS with note (conservation equation proven by property tests; timed soak unproven).
- FS-021 → GAP (no latency-injection test — DEF-FS-004, P0).
- FS-022 → PASS with note (backoff unit slice green; storm scale unproven).

**Gate verdict: NOT QA READY** — P0 gaps FS-003/FS-011/FS-021 + one-shot hang (DEF-FS-001, P2).
