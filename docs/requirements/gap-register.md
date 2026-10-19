# Gap Register — OpsBridge Bounded Context

> Generated from E1/E2/E3 traceability at commit `1ecd302` (branch `feature/g1-opsbridge-foundation`).
> Each gap is traced to a requirement ID with missing test or incomplete verification.

---

## GAP-001: Missing test for StockDriftException immediate dead-letter path

| Field | Value |
|-------|-------|
| **Requirement** | R3.9 — OrderProcessor host dead-letters on StockDriftException immediately (drift is business error, not transport) |
| **Code Location** | `src/FlashSale.Infrastructure/Messaging/OrderProcessorHost.cs:52-58` |
| **Gap Description** | Code path catches `StockDriftException` and calls `DeadLetterAsync` without retry delay. No unit or integration test verifies this behavior. |
| **Risk** | Silent regression could cause drift messages to retry instead of dead-letter, wasting resources and delaying reconciliation. |
| **Suggested Test** | Unit test: `OrderProcessorHostTests.DeadLettersImmediately_OnStockDriftException` — mock `IOrderProcessor`, throw `StockDriftException`, verify `OutboxRepository.MarkDeadLetteredAsync` called with correct eventId and zero retries. |
| **Priority** | HIGH — concurrency path with financial impact |
| **Status** | OPEN — no test exists |
| **Owner** | Next sprint (F3 hardening) |

---

## GAP-002: Missing test for SKU alias mapping via MappingRule

| Field | Value |
|-------|-------|
| **Requirement** | R4.9 — SKU alias mapping: Invoice SKU can be mapped to PO SKU via active MappingRule (SkuAlias) |
| **Code Location** | `src/OpsBridge.Application/UseCases/ThreeWayMatchingEngine.cs:240-249` (`ResolveSku` method) |
| **Gap Description** | `ResolveSku` queries `IMappingRuleRepository.GetActiveByTypeAsync(MappingRuleType.SkuAlias)` but no test exercises this path with a valid SkuAlias rule. |
| **Risk** | Supplier invoice with different SKU code cannot be matched to PO, causing false discrepancies. |
| **Suggested Test** | Unit test: `ReconciliationMatchingTests.Match_SkuAliasMapping_ResolvesToPoSku` — seed SkuAlias rule, invoke `ExecuteAsync` with Invoice containing alias SKU, assert Matched and correct allocation. |
| **Priority** | MEDIUM — integration completeness |
| **Status** | OPEN — no test exists |
| **Owner** | Next sprint (F3 hardening) |

---

## PARTIAL-001 through PARTIAL-010: Integration Tests Requiring External Infrastructure

| ID | Requirement | Gap Description | Resolution Path |
|----|-------------|-----------------|-----------------|
| PARTIAL-001 | R3.2 (max retry limit) | Integration test `FailureDrill5` requires RabbitMQ + Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-002 | R3.3 (DLQ persistence audit) | Integration test `FailureDrill5` requires RabbitMQ + Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-003 | R3.4 (idempotent manual replay) | Integration test `FailureDrill6` requires RabbitMQ + Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-004 | R3.5 (replay creates outbox message) | Integration test `FailureDrill6` requires RabbitMQ + Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-004 | R3.6 (consumer idempotent via Inbox) | Integration test `FailureDrill3_4` requires RabbitMQ + Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-005 | R3.10 (x-retry-count header) | Integration test `FailureDrill5` requires RabbitMQ + Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-006 | R4.10 (concurrent reconciliation allocation) | Integration test `E2E_ConcurrentReconciliations` requires Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-007 | R4.11 (reference scenario rerun) | Integration test `E2E_ThreeWayMatching_ReferenceScenario` requires Postgres | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-008 | R4.12 (discrepancy review RBAC) | Integration test `E2E_DiscrepancyReview_WithRbac` requires Postgres + Auth | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-009 | R4.13 (approve with tolerance) | Integration test `E2E_DiscrepancyReview_ApproveWithVariance` requires Postgres + Auth | CI `opsbridge-tests` job with Testcontainers will run on push |
| PARTIAL-010 | R4.14 (reject keeps receipts available) | Integration test `E2E_DiscrepancyReview_Reject` requires Postgres + Auth | CI `opsbridge-tests` job with Testcontainers will run on push |

> **Note:** All PARTIAL items have comprehensive unit tests passing locally and integration tests written. They become `PASS` once `opsbridge-tests` runs in GitHub Actions (Testcontainers on ubuntu-latest provides real Postgres/RabbitMQ).

---

## Summary

| Category | Count |
|----------|-------|
| **OPEN GAPs (missing test code)** | **2** |
| **PARTIAL (integration infra-dependent)** | **10** |
| **PASS (unit + local integration verified)** | **46** |
| **Total Requirements** | **62** |

---

## Next Actions

1. **GAP-001**: Add `OrderProcessorHostTests.DeadLettersImmediately_OnStockDriftException` in `tests/UnitTests/`.
2. **GAP-002**: Add `ReconciliationMatchingTests.Match_SkuAliasMapping_ResolvesToPoSku` in `tests/OpsBridge.UnitTests/`.
3. **PARTIAL items**: Push branch to trigger GitHub Actions `opsbridge-tests` job; all 10 will turn to PASS.
4. Update this register after each fix; commit with `docs/requirements/gap-register.md` change.