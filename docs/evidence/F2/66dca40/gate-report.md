# F2 Gate Report — OpsBridge Bounded Context Foundation

**Repo:** OpsBridge (FlashSale-Backend)
**Branch:** feature/g1-opsbridge-foundation
**Base/HEAD:** 66dca40380e352757a84d1bf47a7de3a6ed77fe0 (unchanged; working tree is uncommitted)
**Phase:** F2 (P0)
**Status:** IN_PROGRESS — local gates pass; remote CI and human acceptance are NOT_RUN/PENDING.
**Environment:** LOCAL (Docker Desktop/Testcontainers; isolated local harness)

## Scope
Preserve and verify the existing OpsBridge bounded context; integrate its projects into the shared solution, enforce Clean Architecture dependency direction and route authorization, close Production demo-auth exposure, move DLQ access behind Application ports, and define ownership/role scopes.

## Worktree provenance
The initial worktree was already dirty at HEAD `66dca40`, including solution membership, auth policies, endpoint authorization, fixture changes, F1 evidence, and the architecture/authorization guard tests. Those user-provided changes were preserved and validated. This execution added the Production auth boundary, DLQ Application port/use case/adapter, endpoint DbContext guard, concurrent replay test, dependency override, ADR-017, and this report. No commit or push was made.

## Gate results

| Gate | Command | Result |
|---|---|---|
| F2.T1 Build | `dotnet build AzureFlashSale.slnx -c Release` | PASS, 0 errors; existing NU1603 package-resolution warnings |
| F2.T2 Format | `dotnet format AzureFlashSale.slnx --verify-no-changes --verbosity minimal` | PASS, exit 0 |
| F2.T3 FlashSale unit | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release --no-build` | PASS 172/172 |
| F2.T4 FlashSale integration | `dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj -c Release --no-build` | PASS 62/62 |
| F2.T5 OpsBridge unit | `dotnet test tests/OpsBridge.UnitTests/OpsBridge.UnitTests.csproj -c Release --no-build` | PASS 41/41 |
| F2.T6 OpsBridge contract | `dotnet test tests/OpsBridge.ContractTests/OpsBridge.ContractTests.csproj -c Release --no-build` | PASS 7/7 |
| F2.T7 OpsBridge integration | `dotnet test tests/OpsBridge.IntegrationTests/OpsBridge.IntegrationTests.csproj -c Release --no-build` | PASS 22/22 |
| F2.T8 All solution tests | `dotnet test AzureFlashSale.slnx -c Release --no-build --logger trx --results-directory docs/evidence/F2/66dca40/test-results` | PASS 304/304; five TRX files retained |
| F2.T9 Concurrency | `oversell_demo.py --stock 10 --requests 50` against isolated throwaway Postgres/Redis/API | PASS: 3 accepted, 47 rejected, 0 transport failures, DB stock 7 |
| F2.T10 NuGet audit | CI-equivalent `dotnet list <project> package --vulnerable --include-transitive` over all `src/` and `tests/` projects | PASS; no vulnerable packages |
| F2.T11 Trivy FS/config | `trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL --exit-code 1 ...`; `trivy config --severity HIGH,CRITICAL --exit-code 1 .` | PASS; 0 blocking findings |
| F2.T12 OCI images | `docker build` API + Worker; `trivy image --severity HIGH,CRITICAL --exit-code 1 --ignore-unfixed ...` | PASS; both images 0 HIGH/CRITICAL |
| F2.T13 Remote CI | GitHub Actions aggregate on this exact worktree | NOT_RUN; no commit/push was authorized |

## Architecture guards (unit)
- `OpsBridge_Domain_MustNotReference_AnyInfrastructurePackage` — Domain has no EF Core/Redis/RabbitMQ/ASP.NET/FlashSale refs.
- `OpsBridge_Application_MustDependOn_Domain` — Application → Domain only.
- `OpsBridge_Infrastructure_MustNotReference_FlashSale` — no FlashSale assembly dependency (separate DbContext).
- `ProjectReferences_MustFollowCleanArchitectureDirection` — project references match Domain → Application → Infrastructure → API.
- `ApiEndpoints_MustNotDependOnInfrastructureDbContext` — endpoint files cannot directly resolve `OpsBridgeDbContext`.
- `EveryMappedRoute_IsAuthorized_Or_InPublicAllowlist` — deny-by-default; 3 public routes documented (login, healthz, FactoryFlow webhook).
- `PublicAllowlist_HasNoStaleEntries` — stale entries fail the test.

## Root-cause fixes
- Production `/api/v1/auth/login` was a demo persona issuer that accepted caller-selected roles. It is now mapped only in Development/Test; Production requires a configured signing key of at least 32 UTF-8 bytes and rejects the built-in development key.
- DLQ list/replay endpoints directly depended on `OpsBridgeDbContext`. They now use an Application repository port and use case; Infrastructure performs conditional replay-state update and outbox insertion in one transaction.
- The reconciliation route inherits `AuditorOrAbove` and also requires `Review`. `FinanceReviewer` was omitted from the inherited read policy, causing 403 despite passing `Review`; the role was added to the read policy only. The integration test now passes.
- `Testcontainers` resolved vulnerable transitive `SSH.NET 2023.0.0`; the OpsBridge integration test project now pins `SSH.NET 2026.0.0`, matching the existing FlashSale integration project. NuGet audit is clean.

## Auth boundary (integration)
- `Production_DoesNotExpose_DemoRoleLogin` — persona login returns 404 in Production.
- `Production_RequiresDedicatedJwtSigningKey` — fails fast without a dedicated key.
- `Production_RejectsBuiltInJwtSigningKey` — fails fast with the built-in development key.
- `OpsBridgeRoutes_EnforceRoleScopes` — anonymous 401, CUSTOMER 403, Auditor read-only, Operator write allowed, connector secret never echoed.
- `FailureDrill6_ConcurrentManualReplay_QueuesExactlyOnce` — 12 concurrent replay requests create exactly one outbox entry.

## Evidence and acceptance
- TRX: `docs/evidence/F2/66dca40/test-results/` (304 passed, 0 failed, 0 skipped).
- Security and concurrency summaries: `docs/evidence/F2/66dca40/security.txt`, `negative-controls.txt`, and `concurrency.txt`.
- Base and current HEAD are unchanged at `66dca40380e352757a84d1bf47a7de3a6ed77fe0`; changes are uncommitted.
- GitHub CI URL: NOT_RUN. Human acceptance: PENDING. Do not start F3 until CI and explicit phase acceptance are recorded.

## Non-claims
- No real credential authentication or external identity-provider integration; Production tokens must come from a trusted issuer.
- At-least-once delivery only; no exactly-once claim.
- Modular monolith, not microservices. The local concurrency result is environment-specific and not a performance benchmark.