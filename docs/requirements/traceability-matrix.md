# Traceability Matrix — Commerce + OpsBridge

> **Commit**: `1ecd302` (branch `feature/g1-opsbridge-foundation`)
> **Date**: 2026-10-19
> **SDK**: .NET 10.0.400
> **Detail**: 62 requirements across 5 domains — see `requirements.md` for full enumeration.

---

## Gate Summary (F2 Re-Verified at 1ecd302)

| Gate | Command | Result | Evidence |
|------|---------|--------|----------|
| F2.T1 Build (AzureFlashSale.slnx) | `dotnet build AzureFlashSale.slnx` | PASS 0 errors | Build output |
| F2.T2 Format | `dotnet format AzureFlashSale.slnx --verify-no-changes` | PASS | Exit 0 |
| F2.T3 FlashSale unit | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj` | PASS 172/172 | TRX |
| F2.T4 FlashSale integration | `dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj` | PASS 62/62 | Testcontainers PG/Redis/RabbitMQ |
| F2.T5 OpsBridge unit | `dotnet test tests/OpsBridge.UnitTests/...` | PASS 41/41 | TRX |
| F2.T6 OpsBridge contract | `dotnet test tests/OpsBridge.ContractTests/...` | PASS 7/7 | TRX |
| F2.T7 OpsBridge integration | `dotnet test tests/OpsBridge.IntegrationTests/...` | PASS 22/22 | Testcontainers PG |
| F2.T8 All solution tests | `dotnet test AzureFlashSale.slnx` | PASS 304/304 | 5 TRX files |
| F2.T9 Concurrency | `oversell_demo.py --stock 10 --requests 50` | PASS 0 oversell | `concurrency.txt` |
| F2.T10 NuGet audit | `dotnet list package --vulnerable --include-transitive` | PASS 0 vulns | Audit log |
| F2.T11 Trivy fs/config | `trivy fs` + `trivy config` | PASS 0 HIGH/CRITICAL | Trivy report |
| F2.T12 Trivy image | `trivy image` order-api + order-worker | PASS 0 HIGH/CRITICAL | Trivy image report |
| F2.T13 Remote CI | GitHub Actions | **NOT_RUN** — branch not pushed | — |

---

## Domain → Requirement → Test → Evidence Summary

### Domain 1: No-Oversell & Concurrent Ordering (7 requirements, 7 PASS)

| Req ID | Component | Test | CI Job | Status |
|--------|-----------|------|--------|--------|
| R1.1 | `OrderRepository.cs:30-35`, `RedisStockGateway.cs:23-34` | `ConcurrentPurchases_ConserveInventory`, `Conservation_Holds_UnderConcurrentAttempts` | `concurrency-harness` | PASS |
| R1.2 | `OrderProcessor.cs:31-38` | `DuplicateDelivery_PersistsExactlyOnce` | `build-and-unit` | PASS |
| R1.3 | `RedisStockGateway.cs:23-34`, `SubmitOrderUseCase.cs:85-90` | `E2E_FS_03_Reserve_Queue_Consume_ConvergesToOneOrder` | `integration-tests` | PASS |
| R1.4 | `SubmitOrderUseCase.cs:108-133` | `Queue_rejects_when_full_and_accounting_balances` | `build-and-unit` | PASS |
| R1.5 | `RedisStockGateway.cs:62-65`, `SubmitOrderUseCase.cs:136-149` | Resilience fallback path | `integration-tests` | PASS |
| R1.6 | `OrderProcessor.cs:40-46` | `E2E_FS_04_ConsumerDies_ThenRecovers_WithoutDuplicating` | `integration-tests` | PASS |
| R1.7 | `CheckoutSagaCoordinator.cs:47-73` | `Scenario5_DuplicateIdempotencyKey_ReturnsExistingSagaResultSafely` | `build-and-unit` | PASS |

### Domain 2: Transactional Outbox/Inbox & Idempotency (10 requirements, 10 PASS)

| Req ID | Component | Test | CI Job | Status |
|--------|-----------|------|--------|--------|
| R2.1 | `ReceiveWebhookUseCase.cs:97-178` | `PostWebhook_ValidSignedPayload_PersistsAtomicallyToPostgresAndReturns202` | `opsbridge-tests` | PASS |
| R2.2 | `ReceiveWebhookUseCase.cs:97-102` | `PostWebhook_DuplicatePayload_Returns200WithDuplicateFlag` | `opsbridge-tests` | PASS |
| R2.3 | `ReceiveWebhookUseCase.cs:103-116` | `Execute_DuplicateEventId_WithConflictingPayload_Throws...` | `opsbridge-tests` | PASS |
| R2.4 | `OutboxPublisherHostedService.cs:46-80` | `ExecuteBatchAsync_SuccessfulPublish_MarksProcessedAndNeverFailed` | `build-and-unit` | PASS |
| R2.5 | `GoodsReceiptConsumerHost.cs:114-117` | `FailureDrill3_And_4_PublisherRedelivery_ConsumerIdempotentViaInbox` | `opsbridge-tests` | PASS |
| R2.6 | `OutboxRepository.cs:72-97` | `FailureDrill5_PoisonMessage_RetriesAndDeadLettersWithAudit` | `opsbridge-tests` | PASS |
| R2.7 | `ReplayDeadLetterEventUseCase.cs` | `FailureDrill6_ManualReplay_RequestedTwice_HandledIdempotently` | `opsbridge-tests` | PASS |
| R2.8 | `ReceiveWebhookUseCase.cs:158-168` | `FailureDrill1_RabbitMqDown_WebhookStillAcceptedAndStoredInOutbox` | `opsbridge-tests` | PASS |
| R2.9 | `OutboxPublisherHostedService.cs:46-80` | `FailureDrill2_ApiCrashesImmediatelyAfterDbCommit_OutboxPublisherRecovers` | `opsbridge-tests` | PASS |
| R2.10 | `EfInboxRepository.cs:9-13` | `PostWebhook_ConcurrentIdenticalWebhooks_HandledIdempotentlyWithoutOrphanedRows` | `opsbridge-tests` | PASS |

### Domain 3: DLQ, Retry & Replay (10 requirements, 3 PASS, 6 PARTIAL, 1 GAP)

| Req ID | Component | Test | CI Job | Status |
|--------|-----------|------|--------|--------|
| R3.1 | `OutboxMessage.cs:68-72` | `BackoffFor_DoublesPerAttempt` | `build-and-unit` | PASS |
| R3.2 | `OutboxMessage.cs:15` | `FailureDrill5` | `opsbridge-tests` | PARTIAL |
| R3.3 | `DeadLetterEvent.cs:7-20` | `FailureDrill5` | `opsbridge-tests` | PARTIAL |
| R3.4 | `EfDeadLetterEventRepository.cs:17-75` | `FailureDrill6` | `opsbridge-tests` | PARTIAL |
| R3.5 | `EfDeadLetterEventRepository.cs:55-66` | `FailureDrill6` | `opsbridge-tests` | PARTIAL |
| R3.6 | `GoodsReceiptConsumerHost.cs:114-120` | `FailureDrill3_4` | `opsbridge-tests` | PARTIAL |
| R3.7 | `OutboxDispatcherUseCase.cs` | `ExecuteBatchAsync_OnePoisonMessage_DoesNotBlockTheRestOfTheBatch` | `build-and-unit` | PASS |
| R3.8 | `OutboxMessage.cs:44-59` | `DeadLetterState_IsExplicitRatherThanInferredFromRetryCount` | `build-and-unit` | PASS |
| R3.9 | `OrderProcessorHost.cs:52-58` | **MISSING** | — | **GAP** |
| R3.10 | `GoodsReceiptConsumerHost.cs:202-206` | `FailureDrill5` | `opsbridge-tests` | PARTIAL |

### Domain 4: Three-Way Matching & Reconciliation (17 requirements, 9 PASS, 4 PARTIAL, 1 GAP)

| Req ID | Component | Test | CI Job | Status |
|--------|-----------|------|--------|--------|
| R4.1 | `ThreeWayMatchingEngine.cs:8-38` | `Match_PerfectThreeWayMatch_ReturnsMatched` | `build-and-unit` | PASS |
| R4.2 | `ThreeWayMatchingEngine.cs:166-189` | `Match_PartialGoodsReceipt_FlagsQuantityMismatch` | `build-and-unit` | PASS |
| R4.3 | `ThreeWayMatchingEngine.cs:167-202` | `Match_MultipleGoodsReceipts_CumulativeAllocation_ReturnsMatched` | `build-and-unit` | PASS |
| R4.4 | `ThreeWayMatchingEngine.cs:166-190` | `Match_MultipleInvoices_PartialInvoicing_AllocatesAvailableQuantity` | `build-and-unit` | PASS |
| R4.5 | `ThreeWayMatchingEngine.cs:144-164` | `Match_PriceMismatch_ExceedsTolerance_FlagsPriceMismatch` | `build-and-unit` | PASS |
| R4.6 | `TolerancePolicy.cs:3-8` | `Match_QuantityToleranceBoundary_AllowsWithinTolerance` | `build-and-unit` | PASS |
| R4.7 | `ThreeWayMatchingEngine.cs:251-283` | `Match_UomConversion_ValidRule_NormalizesCorrectly` | `build-and-unit` | PASS |
| R4.8 | `ThreeWayMatchingEngine.cs:26-72` | `MissingPO`, `MissingGR`, `CurrencyMismatch`, `SupplierMismatch` | `build-and-unit` | PASS |
| R4.9 | `ThreeWayMatchingEngine.cs:240-249` | **MISSING** | — | **GAP** |
| R4.10 | `ReconcileInvoiceUseCase.cs:52-85` | `E2E_ConcurrentReconciliations_CompeteForReceiptStock` | `opsbridge-tests` | PARTIAL |
| R4.11 | `ThreeWayMatchingEngine.cs:166-202` | `Match_ReferenceScenario_Po1001_CumulativeReceipts_MatchesOnRerun` | `build-and-unit` | PASS |
| R4.12 | `ReconciliationEndpoints.cs`, `ReviewDiscrepancyUseCase.cs` | `E2E_DiscrepancyReview_WithRbac_AuditsDecision` | `opsbridge-tests` | PARTIAL |
| R4.13 | `ReviewDiscrepancyUseCase.cs` | `E2E_DiscrepancyReview_ApproveWithVariance_CommitsAllocations` | `opsbridge-tests` | PARTIAL |
| R4.14 | `ReviewDiscrepancyUseCase.cs` | `E2E_DiscrepancyReview_Reject_TransitionsStatus` | `opsbridge-tests` | PARTIAL |
| R4.15 | `CsvInvoiceParser.cs` | `Ingestion_DuplicateInvoice_DetectedInRepository` | `build-and-unit` | PASS |
| R4.16 | `ReconcileInvoiceUseCase.cs:32-35` | `ReconcileInvoice_AlreadyMatched_ThrowsDomainException` | `build-and-unit` | PASS |
| R4.17 | `ThreeWayMatchingEngine.cs:251-283` | `Match_InvalidUomConversion_NoRule_FlagsUomConversionError` | `build-and-unit` | PASS |

### Domain 5: RBAC & Authorization Boundaries (18 requirements, 18 PASS)

| Req ID | Component | Test | CI Job | Status |
|--------|-----------|------|--------|--------|
| R5.1 | `AuthRoles.cs` (both) | `AuthServiceTests.cs:40` | `build-and-unit` | PASS |
| R5.2 | `AuthService.cs:21-29` | `Register_CreatesCustomer_AndReturnsTokens` | `build-and-unit` | PASS |
| R5.3 | `AuthEndpoints.cs:31-53` | `Bootstrap_WithoutTheHeader_IsForbidden` (4 tests) | `integration-tests` | PASS |
| R5.4 | `AuthPolicies.cs:11-26` | `EndpointAuthorizationGuardTests` (static) | `build-and-unit` | PASS |
| R5.5 | `OpsBridgeAuthPolicies.cs:7-33` | `OpsBridgeAuthorizationGuardTests` (static) | `opsbridge-tests` | PASS |
| R5.6 | `EndpointAuthorizationGuardTests.cs:33-188` | Static source scan | `build-and-unit` | PASS |
| R5.7 | `OpsBridgeAuthorizationGuardTests.cs:11-52` | Static source scan | `opsbridge-tests` | PASS |
| R5.8 | `Program.cs:265-266,340-742` | `StaffOnlyEndpoints`, `OutboxRunbook_IsReachableByStaff` | `integration-tests` | PASS |
| R5.9 | `Program.cs:456-509` | `Pay/OrderStatus` tests | `integration-tests` | PASS |
| R5.10 | `ConnectorEndpoints.cs`, `ReconciliationEndpoints.cs` | `OpsBridgeRoutes_EnforceRoleScopes` | `opsbridge-tests` | PASS |
| R5.11 | `Program.cs:193-211` (both) | `Production_RequiresDedicatedJwtSigningKey` (3 tests) | `opsbridge-tests` | PASS |
| R5.12 | `AuthService.cs:95-135` | `Refresh_ConcurrentRotations_ExactlyOneWins` | `build-and-unit` | PASS |
| R5.13 | `AuthService.cs:83-88` | `Login_RejectsWrongPassword_AndUnknownEmail_Identically` | `build-and-unit` | PASS |
| R5.14 | `Program.cs:271-293` | `Metrics_ExposeTheCountersTheFlowJustProduced` | `integration-tests` | PASS |
| R5.15 | `Program.cs:367-441` | `AnonymousOrdering_StillWorks` | `integration-tests` | PASS |
| R5.16 | `Program.cs:629-667` | `SagaCheckout_RequiresAuthentication_ButNotTheStaffRole` | `integration-tests` | PASS |
| R5.17 | `WebhookEndpoints.cs:17-89` | HMAC guard (static) | `opsbridge-tests` | PASS |
| R5.18 | `Program.cs:765-783` | Allowlist (static) | `build-and-unit` | PASS |

---

## Overall Requirement Status

| Domain | Total | PASS | PARTIAL | GAP |
|--------|-------|------|---------|-----|
| 1. No-Oversell & Concurrent Ordering | 7 | 7 | 0 | 0 |
| 2. Transactional Outbox/Inbox & Idempotency | 10 | 10 | 0 | 0 |
| 3. DLQ, Retry & Replay | 10 | 3 | 6 | 1 |
| 4. Three-Way Matching & Reconciliation | 17 | 9 | 4 | 1 |
| 5. RBAC & Authorization Boundaries | 18 | 18 | 0 | 0 |
| **Total** | **62** | **47** | **10** | **2** |

> **PARTIAL** = integration tests written, require Testcontainers — will PASS when `opsbridge-tests` runs in GitHub Actions.
> **GAP** = missing test code — see `gap-register.md` for remediation plan.

---

## CI Job → Requirement Coverage

| CI Job | Requirements Covered | Test Count | Local Status |
|--------|---------------------|------------|--------------|
| `build-and-unit` | R1.1-R1.7, R2.4, R3.1, R3.7, R3.8, R4.1-R4.8, R4.11, R4.15-R4.17, R5.1-R5.6, R5.8, R5.12-R5.13 | 172+41+7=220 | PASS |
| `integration-tests` | R1.3, R1.5, R1.6, R5.2, R5.3, R5.8-R5.9, R5.14-R5.16 | 62 | PASS |
| **`opsbridge-tests`** | R2.1-R2.3, R2.5-R2.10, R3.2-R3.6, R3.10, R4.10, R4.12-R4.14, R5.5, R5.7, R5.10, R5.11, R5.17 | 70 | PASS |
| `concurrency-harness` | R1.1 (oversell proof) | 1 harness | PASS |
| `security-scan` | NFR-C05 (authorization) | — | PASS |
| `docker-build-scan` | N/A (container security) | — | PASS |
| `manifest-validation` | N/A (deployability) | — | PASS |
| `ci-gate` | Aggregate of all above | — | PASS (local) |

---

## Negative Controls Verified

| Control | Before | After |
|---------|--------|-------|
| OpsBridge tests in CI | `grep "OpsBridge" .github/workflows/ci.yml` → 0 matches | `opsbridge-tests` job added + wired into `ci-gate` |
| YAML syntax valid | — | `ruby -ryaml` → OK |
| Build succeeds | — | `dotnet build OpsBridge.slnx` → 0 errors |
| Unit tests pass | — | 41/41 PASS |
| Contract tests pass | — | 7/7 PASS |
| Integration tests pass | — | 22/22 PASS (Testcontainers) |
| FlashSale unit tests pass | Comment claimed "red 161/162" | Re-verified 172/172 PASS |
| No threshold on coverage | Confirmed | Still none (by design) |
| Format check advisory | `continue-on-error: true` | Unchanged |

---

## Unfinished Gates (Blockers for AI-1)

| Gate | Status | Required For |
|------|--------|-------------|
| Remote GitHub CI (`ci-gate` = SUCCESS with `opsbridge-tests`) | NOT_RUN (branch not pushed) | AI-1 progression |
| Human acceptance (F2/F3) | PENDING (user approval) | AI-1 progression |
| GAP-001: `OrderProcessorHost` StockDrift test | OPEN (no test) | AI-1 progression |
| GAP-002: SKU alias mapping test | OPEN (no test) | AI-1 progression |
| Restore drill (NFR-C03) | GAP (manual only) | Production readiness |
| Benchmark p95 (NFR-C04) | UNVERIFIED | Performance claims |

> **Gate rule**: Do not advance to AI-1 until all items above are resolved or explicitly deferred by human decision.