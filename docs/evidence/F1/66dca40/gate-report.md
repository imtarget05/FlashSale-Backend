# F1 Gate Report — Restore CI/Security/Format

**Repo:** OpsBridge (FlashSale-Backend)
**Branch:** feature/g1-opsbridge-foundation
**Base commit:** ea82884
**Head commit:** 66dca40
**Phase:** F1 (P0)
**Status:** PASS

## Changed files
- `tests/UnitTests/EndpointAuthorizationGuardTests.cs` — line 158: `Assert.Single(ReadRoutes().Where(r => r.Display == display))` → `Assert.Single(ReadRoutes(), r => r.Display == display)` (fix xUnit2031 analyzer)

## Gate results

| Gate ID | Command | Result | Evidence |
|---|---|---|---|
| F1.T1 | `dotnet format AzureFlashSale.slnx --verify-no-changes --verbosity minimal` | PASS | lint.txt |
| F1.T2 | `dotnet build AzureFlashSale.slnx -c Release` | PASS | build.txt |
| F1.T3 | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj -c Release --no-build` | PASS — 172/172 | build + test run |
| F1.T4 | `dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj -c Release --no-build` | PASS — 62/62 | test run (Testcontainers PG/Redis/RabbitMQ) |
| F1.T5 | Concurrency harness: `python3 load-tests/concurrency/oversell_demo.py --stock 10 --requests 50 --psql-container flashsale-pg --settle-seconds 3 --ops-token <token>` | PASS — 3 accepted / 47 rejected / stock 7 | negative-tests.txt |
| F1.T6 | `trivy image --severity HIGH,CRITICAL --scanners vuln flashsale/order-api:ci-local` | PASS — 0 vulnerabilities | security.txt |
| F1.T7 | `trivy image --severity HIGH,CRITICAL --scanners vuln flashsale/order-worker:ci-local` | PASS — 0 vulnerabilities | security.txt |

## Negative controls
- xUnit2031 warning was present before fix (line 158 used `Where` clause before `Assert.Single`). After fix, analyzer warning eliminated.
- Trivy base image `mcr.microsoft.com/dotnet/aspnet:10.0` (ubuntu 24.04): 0 HIGH/CRITICAL vulnerabilities. Remote CI failure (libssl3t64 CVE-2026-84782) was due to older image digest; current digest is clean.

## Regression check
- Full F0 regression (build + unit + integration + format) re-run on head commit: all green.
- No changes to FlashSale core API routes, schema, or no-oversell invariant.

## Evidence files
- `lint.txt` — dotnet format --verify-no-changes output (exit 0)
- `build.txt` — dotnet build output (exit 0, 9 warnings pre-existing, none new)
- `security.txt` — Trivy image scan for order-api and order-worker (0 HIGH/CRITICAL each)

## Next phase
F2 — OpsBridge Bounded Context Foundation (approved to proceed)