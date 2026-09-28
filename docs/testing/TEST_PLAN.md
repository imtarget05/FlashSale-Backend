# Test Plan — FlashSale Backend (High Concurrency)

**Contract:** [`docs/qa/QA_ACCEPTANCE.md`](../../docs/qa/QA_ACCEPTANCE.md).
**Case IDs:** `FS-001` → `FS-022` in `TEST_CASES.md`.
**Runner:** `dotnet test`. On disk: UnitTests **110 `[Fact]`+`[Theory]` methods (167 executed
with `[InlineData]`)**, IntegrationTests **51 methods** (enumerated 2026-09-27).
Background proof: `docs/testing/integration-tests.md` (map, extend to HTTP E2E).

## Scope

Redis Lua CAS reservation, Postgres conditional update, zero-oversell under
100–1000 concurrent buyers, idempotency (duplicate delivery/retry), Redis↔DB
compensation + resync, poison-outbox isolation, soak (no drift growth).

## Levels

| Level | What | Where |
|---|---|---|
| Unit | guards: conditional-UPDATE SQL, Lua CAS logic vs fakes, idempotency-key handling | `StockResyncTests` (5), `OrderProcessorTests` (4), `ArchitectureTests` (3) |
| Property | conservation equation over concurrent purchases | `InventoryConservationPropertyTests` (1 Theory) |
| Integration | Testcontainers per `integration-tests.md` (assert **Postgres state**, not just HTTP) | `OrderFlowTests` (3), `ResilienceTests` (5+1) — Docker only |
| Concurrency | 50×–1000× simultaneous buyers; exactly-N-success; conservation holds | `concurrency-harness` (CI BLOCKING: 50 buyers / stock 10 → exactly 10) |
| Chaos | Redis down, DB down, worker kill after commit, resync-during-buy, retry storm | `ResilienceTests`, `StockResyncTests`, `LiveInfraGuard` |
| E2E (gap to close) | HTTP end-to-end (current tests call repository/queue directly — see `integration-tests.md` §"Still not tested") | UNVERIFIED |
| Soak | long run, stock-drift metric must not grow | UNVERIFIED here |

## Environments

| Env | Command | Scope |
|---|---|---|
| Offline | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release` | **167/167 passed** 2026-09-27, no containers |
| CI (Docker) | `dotnet test` full + `concurrency-harness` (`RESULT: PASS` gate, BLOCKING) | unit + Testcontainers integration |
| Live-infra | Azure deploy | Service Bus live round-trip (ADR-005, compile-tested only here) |

## Entry / exit criteria

- Entry: seed product with known stock per test (EnsureDeleted → Migrate → seed).
- Exit: P0 100% PASS with the conservation invariant asserted on DB state in every concurrency test.

## Invariant under test

```text
available_stock >= 0
successful_orders <= initial_stock
initial_stock == remaining_stock + successfully_reserved_stock + legally_compensated_stock
```
