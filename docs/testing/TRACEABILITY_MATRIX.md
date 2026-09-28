# Traceability Matrix — FlashSale Backend

`Requirement → Business Rule → Test Case → Automated Test → Execution Evidence`

| Business invariant | Test Case | Automated test (exact node, on disk) | Source file | Evidence |
|---|---|---|---|---|
| OVERSELL_ZERO (`sold ≤ stock`, `final ≥ 0`) | FS-002, FS-003, FS-008 | `InventoryConservationPropertyTests` (Theory) + `concurrency-harness` (CI BLOCKING: 50 buyers/stock 10 → exactly 10) | `tests/UnitTests/`, harness | VERIFIED unit slice 2026-09-27 (167-run); harness = CI |
| CONSERVATION (`initial == remaining + reserved + compensated`) | FS-003, FS-009, FS-015–017, FS-020 | `StockResyncTests` (5), conservation Theory | `tests/UnitTests/StockResyncTests.cs` | VERIFIED 2026-09-27 (in 167-run) |
| EXACTLY_ONCE (same key N× → 1 order, stock −1) | FS-006, FS-013 | `StockResyncTests` retry/dedup paths | `tests/UnitTests/StockResyncTests.cs` | VERIFIED 2026-09-27 |
| POISON_ISOLATED (DLQ, queue flows; crash-retry no-dup) | FS-012, FS-014 | `OutboxPoisonTests` (2), `OutboxBackoffTests` (7+2) | `tests/UnitTests/` | VERIFIED 2026-09-27 |
| NEGATIVE_NEVER_MUTATES (0/−5 qty, unknown product → 0 orders) | FS-004, FS-005 | `ArchitectureTests` (3) + DTO validation paths | `tests/UnitTests/` | VERIFIED 2026-09-27 |
| QUOTA_429 / BACKPRESSURE (pool 80/100 sheds storm) | FS-022 | ADR-014 + `OutboxBackoffTests` | `docs/adr/014-*`, unit | VERIFIED unit slice; live figures UNVERIFIED |
| P95_LATENCY (29 ms tiered vs 574 ms naive) | FS-019 | `docs/benchmarks/phase5-tiered-experiment.md` (no test file) | docs | UNVERIFIED (NOT A GATE) |
| INTEGRATION_LOCAL (Testcontainers per-file, one-shot hangs) | FS-002, FS-010, FS-012–016 | OrderFlow 3, Resilience 7 (RedisDown, RabbitMq round-trip, InvalidQuantity, UnknownProduct, RestartSimulation), AuthFlow 9, DailyReport 3, EndpointAuth 29, LowStock 4, Payment 5 | `tests/IntegrationTests/` | VERIFIED 60/60 per-file 2026-09-27; one-shot HANGS → DEF-FS-001 |
| SCALE_AND_FAULT_GAPS | FS-003, FS-011, FS-021 | **none on disk** | — | GAPS → DEF-FS-002/003/004 (all P0, OPEN) |
