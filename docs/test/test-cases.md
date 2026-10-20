# Test Cases — Commerce + OpsBridge (OpsBridge bounded context)

> Mã định dạng `TC-Cxx`. Mỗi test case trỏ tới **test file::case thực tế trong repo** và kết quả thực tế từ F2 local gates (commit `fd7eb3e`). Không fabricate kết quả.

## A. Functional test cases (FR-Cxx)

| TC ID | FR | Test case (mô tả) | Test file :: case | Kết quả | Evidence |
|---|---|---|---|---|---|
| TC-C-001 | FR-C01 | No-oversell: 50 requests đồng thời, stock 10 → đúng 3 accepted, 47 rejected, DB stock 7 | concurrency-harness `oversell_demo.py` | PASS | `docs/evidence/F2/66dca40/concurrency.txt` |
| TC-C-002 | FR-C02 | Webhook accepted & lưu outbox dù RabbitMQ down | `FailureDrillIntegrationTests.FailureDrill1_RabbitMqDown_WebhookStillAcceptedAndStoredInOutbox` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-003 | FR-C03 | Consumer dedupe cùng eventId (không xử lý hai lần) | `FailureDrillIntegrationTests.FailureDrill3_And_4_PublisherRedelivery_ConsumerIdempotentViaInbox` (drill 3+4 duplicate messages → inbox dedupe) + `OutboxPoisonTests` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-004 | FR-C04 | Phát hiện đơn hàng kẹt fulfillment | (chưa có test) | GAP | — |
| TC-C-005 | FR-C05 | AI tra cứu trạng thái đơn (exception assistant) | (chưa implement) | NOT_IMPLEMENTED | — |
| TC-C-006 | FR-C06 | Phê duyệt discrepancy (approve with tolerance → allocate, invoice ApprovedWithVariance) | `ReconciliationIntegrationTests.E2E_DiscrepancyReview_...` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-007 | FR-C06 | Từ chối discrepancy → release hàng, invoice Rejected, GR AvailableQuantity khôi phục | `ReconciliationIntegrationTests.E2E_DiscrepancyReview_Reject_TransitionsStatus_AndKeepsReceiptsAvailable` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-008 | FR-C07 | Dự báo nhu cầu theo SKU | (chưa implement) | NOT_IMPLEMENTED | — |
| TC-C-009 | FR-C08 | Replay DLQ thành công → ReplayQueued, 1 OutboxMessage, IsReplayed=true | `ReplayDeadLetterEventUseCaseTests.ExecuteAsync_DelegatesReplayCommandToRepositoryPort` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-010 | FR-C08 | Replay DLQ trùng → AlreadyReplayed, không tạo OutboxMessage mới | `FailureDrillIntegrationTests.FailureDrill6_ManualReplay_RequestedTwice_HandledIdempotently()` (replay twice) | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-011 | FR-C08 | Replay DLQ concurrent 12× → 1 Queued + 11 AlreadyReplayed, 1 outbox | `FailureDrillIntegrationTests.FailureDrill6_ConcurrentManualReplay_QueuesExactlyOnce` | PASS | `docs/evidence/F2/66dca40/test-results/`, `negative-controls.txt` |
| TC-C-012 | FR-C09 | Mọi route OpsBridge có RequireAuthorization, trừ 3 allowlist | `OpsBridgeAuthorizationGuardTests.EveryMappedRoute_IsAuthorized_Or_InPublicAllowlist` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-013 | FR-C09 | Public allowlist không có entry bị stale | `OpsBridgeAuthorizationGuardTests.PublicAllowlist_HasNoStaleEntries` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-014 | FR-C10 | Production không expose demo login (404) | `OpsBridgeAuthBoundaryTests.Production_DoesNotExpose_DemoRoleLogin` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-015 | FR-C10 | Production bắt buộc Jwt:SigningKey (missing → exception) | `OpsBridgeAuthBoundaryTests.Production_RequiresDedicatedJwtSigningKey` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-016 | FR-C10 | Production từ chối built-in dev signing key | `OpsBridgeAuthBoundaryTests.Production_RejectsBuiltInJwtSigningKey` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-017 | FR-C10 | Role scopes: anonymous 401, CUSTOMER 403, Auditor read 200/write 403, Operator write 201 + secret masked | `OpsBridgeAuthBoundaryTests.OpsBridgeRoutes_EnforceRoleScopes` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-018 | FR-C11 | Clean Architecture: Domain không tham chiếu infra pkg | `OpsBridgeArchitectureTests.OpsBridge_Domain_MustNotReference_AnyInfrastructurePackage` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-019 | FR-C11 | Clean Architecture: Application → Domain only | `OpsBridgeArchitectureTests.OpsBridge_Application_MustDependOn_Domain` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-020 | FR-C11 | Clean Architecture: Infrastructure không tham chiếu FlashSale | `OpsBridgeArchitectureTests.OpsBridge_Infrastructure_MustNotReference_FlashSale` | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-021 | FR-C11 | Clean Architecture: project refs Direction; endpoints không trực tiếp DbContext | `OpsBridgeArchitectureTests.ProjectReferences_MustFollowCleanArchitectureDirection`, `ApiEndpoints_MustNotDependOnInfrastructureDbContext` | PASS | `docs/evidence/F2/66dca40/test-results/` |
## B. Non-functional test cases (NFR-Cxx)

| TC ID | NFR | Test case | Test file :: case | Kết quả | Evidence |
|---|---|---|---|---|---|
| TC-C-N01 | NFR-C01 | No overselling (0 vi phạm) | concurrency-harness | PASS | `concurrency.txt` |
| TC-C-N02 | NFR-C05 | 0 unauthorized operations | `OpsBridgeAuthorizationGuardTests` + `OpsBridgeAuthBoundaryTests` | PASS | `test-results/` |
| TC-C-N03 | NFR-C06 | 0 duplicate business records sau retry/replay | `FailureDrill6_ManualReplay_RequestedTwice_HandledIdempotently()` + `FailureDrill6_ConcurrentManualReplay_QueuesExactlyOnce()` (12→1+11, 1 outbox) | PASS | `negative-controls.txt` |
| TC-C-N04 | NFR-C03 | Restore drill RTO ≤2h / RPO ≤15p | (chưa chạy drill) | GAP | `docs/business/recovery-targets.md` |
| TC-C-N05 | NFR-C04 | Checkout p95 <500ms | (chỉ là doc benchmark) | UNVERIFIED | `docs/benchmarks/` |

## C. Contract tests

| TC ID | Contract | Test file :: case | Kết quả | Evidence |
|---|---|---|---|---|
| TC-C-K01 | `GoodsReceiptConfirmed v1` JSON Schema | `GoodsReceiptContractTests` (7 cases) | PASS | `docs/evidence/F2/66dca40/test-results/` |
| TC-C-K02 | Webhook HMAC/Svix signature verification | `HmacWebhookVerifierTests` | PASS | `test-results/` |

## D. Security test cases

| TC ID | Mục tiêu | Test file :: case | Kết quả | Evidence |
|---|---|---|---|---|
| TC-C-S01 | Production auth boundary (demo login, signing key) | `OpsBridgeAuthBoundaryTests` (3 tests) | PASS | `test-results/`, `negative-controls.txt` |
| TC-C-S02 | Route authorization deny-by-default | `OpsBridgeAuthorizationGuardTests` (2 tests) | PASS | `test-results/` |
| TC-C-S03 | NuGet audit (0 vulnerable packages, transitive) | `dotnet list package --vulnerable` | PASS | `docs/evidence/F2/66dca40/security.txt` |
| TC-C-S04 | Trivy fs/config (0 HIGH/CRITICAL) | `trivy fs` + `trivy config` | PASS | `docs/evidence/F2/66dca40/security.txt` |
| TC-C-S05 | Trivy image (0 HIGH/CRITICAL order-api + order-worker) | `trivy image` | PASS | `docs/evidence/F2/66dca40/security.txt` |

## E. Test inventory thực tế (F2)

| Test project | Số test | Kết quả F2 |
|---|---|---|
| `FlashSale.UnitTests` | 172 | PASS |
| `FlashSale.IntegrationTests` | 62 | PASS (Testcontainers) |
| `OpsBridge.UnitTests` | 41 | PASS |
| `OpsBridge.ContractTests` | 7 | PASS |
| `OpsBridge.IntegrationTests` | 22 | PASS (Testcontainers PG 15 + RabbitMQ 3) |
| **Tổng (`dotnet test slnx`)** | **304** | **PASS 304/304** |
| Concurrency harness (`oversell_demo.py`) | 1 harness run | PASS (3 accepted / 47 rejected / stock 7) |
| Remote CI | — | NOT_RUN (nhánh chưa push) |

## F. Test gaps (phải khắc phục trước F3)

| Test gap | Liên quan | Hành động |
|---|---|---|
| Injected stuck-order detection | FR-C04 / TC-C-004 | Viết test inject đơn kẹt và xác nhận OpsBridge phát hiện |
| Restore drill (RTO/RPO) | NFR-C03 / TC-C-N04 | Chạy restore drill trên PostgreSQL backup |
| Remote CI run | F2.T13 | Push nhánh + chạy GitHub Actions |
| Human acceptance | F2 | Chờ user approve |

