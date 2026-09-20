# Phase 5 — DevSecOps CI pipeline (run evidence)

Status: **PASS on a real GitHub Actions run** — all 7 jobs green.
PR: <https://github.com/imtarget05/FlashSale-Backend/pull/1>
Green run: <https://github.com/imtarget05/FlashSale-Backend/actions/runs/35524030380>
(head `021e9592d1740ab3542321a0f615b9c3bb5b243e`)

## Pipeline shape (`.github/workflows/ci.yml`)

```text
Pull request
   │
   ├─ build-and-unit        dotnet build + unit tests + NuGet vulnerable-package audit
   ├─ integration-tests     Testcontainers: real Postgres/Redis/RabbitMQ per fixture
   ├─ concurrency-harness   50 buyers vs stock 10 — inventory must be conserved
   ├─ security-scan         Trivy fs: vuln + secret + IaC misconfig (HIGH+ blocks)
   ├─ docker-build-scan     build order-api + order-worker, Trivy image scan
   ├─ sonarqube             analysis + QUALITY GATE (skips until SONAR_TOKEN exists)
   └─ ci-gate               single aggregate check (branch-protection ready)
```

## Real runs (both failures and the fix — the loop, not just the green)

**Run 1 — 35523852067: FAILED (honestly kept).** First run of the pipeline
failed: `security-scan` and `docker-build-scan` could not even start because
`aquasecurity/trivy-action@0.28.0` does not exist — the action's tags carry a
`v` prefix. Fixed by pinning `@v0.36.0`
(commit "CI fix: pin trivy-action to v0.36.0").

**Run 2 — 35524030380: ALL GREEN.** Every job succeeded on GitHub's runners:

| Job | Conclusion |
|---|---|
| build-and-unit | success |
| integration-tests | success |
| concurrency-harness | success |
| security-scan | success |
| docker-build-scan | success |
| sonarqube | success (explicit skip — see below) |
| ci-gate | success |

## Real security findings the pipeline forced (all fixed)

1. **3 × CRITICAL — Azure storage account keys in local Terraform state files.**
   Trivy flagged `terraform.tfstate.backup` (`primary_access_key`/connection
   strings with `AccountKey=…`). Fix: Phase 3C already moved the state to the
   remote encrypted backend, so the local `terraform.tfstate*` copies were
   redundant and deleted (backups retained under `/tmp/`). Defence-in-depth note
   recorded in ADR-009: the account has `allowSharedKeyAccess=false`, so those
   keys were already inert for data operations.
2. **3 × HIGH — Kubernetes base deployment without a container-level
   securityContext** (KSV-0014 `readOnlyRootFilesystem`, KSV-0118 default
   security context allowing root). Fix: `runAsNonRoot: true`, `runAsUser: 1654`
   (matches the image's `app` user), `allowPrivilegeEscalation: false`,
   `readOnlyRootFilesystem: true` with an `emptyDir` at `/app/logs` for the file
   DLQ, `capabilities.drop: [ALL]`. Also moved the readinessProbe from the
   `/healthz` alias to `/health/ready` so readiness gates on PostgreSQL.
   Lesson recorded: Trivy checks `securityContext` at the *container* level —
   pod-level alone does not satisfy KSV checks.
3. **2 × HIGH — SSH.NET 2023.0.0 (transitive via Testcontainers 4.0.0)**,
   CVE-2026-48798 and CVE-2026-85756. Fix: direct reference to `SSH.NET
   2026.0.0` (the package ID moved from `Renci.SshNet` to `SSH.NET`) pinned in
   `tests/IntegrationTests/FlashSale.IntegrationTests.csproj`; integration
   tests re-run locally: **10/10 PASS**. NuGet's own audit (`NU1903`) corroborated
   Trivy before the fix.

After the fixes, local `trivy fs --exit-code 1` returns **exit 0** (0 HIGH /
0 CRITICAL) and both image scans return **exit 0**.

## SonarQube quality gate — honest status

The `sonarqube` job is wired (dotnet-sonarscanner begin → build → test with
trx coverage → end, with `sonar.qualitygate.wait=true` so the job fails when
the gate fails) but is **explicitly skipped** until `SONAR_TOKEN` exists:

```text
SKIP: SONAR_TOKEN is not configured on this repository yet.
Quality gate is NOT enforced in CI. … Until then Trivy + NuGet audit carry
the security evidence and this job reports itself as skipped.
```

To enable (any time):

```bash
# SonarCloud (free for public repos):
#   create project imtarget05_FlashSale-Backend → generate an analysis token
gh secret set SONAR_TOKEN -R imtarget05/FlashSale-Backend
# re-run: the job analyses and fails the PR if the quality gate fails
```

Until that secret exists, the quality gate is **not enforced in CI** — this
document intentionally does not claim it is.

## Local evidence captured before pushing (found real issues)

| Step | Command (local) | Result |
|---|---|---|
| fs scan (before fixes) | `trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL --exit-code 1 .` | **exit 1** — 3 CRIT secrets, 3 HIGH misconfig, 2 HIGH CVEs |
| fs scan (after fixes) | same | **exit 0** |
| image scan | `trivy image --severity HIGH,CRITICAL --exit-code 1 flashsale/order-{api,worker}:p5evidence` | **exit 0** both |
| NuGet audit | `dotnet list <proj> package --vulnerable --include-transitive` | clean after the SSH.NET pin |
| unit tests | `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj` | **14/14 PASS** |
| integration tests | `dotnet test tests/IntegrationTests/…` | **10/10 PASS** (Testcontainers) |

## Merge

Merged to `main` after the green run (the PR also drives the same workflow on
`push` to `main` for a second confirmation).
