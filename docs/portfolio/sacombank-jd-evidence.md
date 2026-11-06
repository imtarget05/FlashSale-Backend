# Sacombank JD evidence — repository-only assessment

This is a map of verifiable artifacts in this repository, not a claim of
employment history, years of experience, production operation, or bank
compliance. A checked-in configuration is not evidence that a cloud resource
was deployed or operated.

## Current Backend JD evidence (2026-11-06)

The longer validation section below is a historical snapshot at `07b77bd`.
These newer, merged changes are the current repository evidence. `DEMONSTRATED`
means implemented and tested locally/CI; it does not mean operated in a bank.

| JD area | Status | Direct evidence and remaining limit |
|---|---|---|
| REST API, Java/Spring Boot, Spring Security/JPA | `DEMONSTRATED` | [Payment controller](../../src/payment-service-java/src/main/java/com/flashsale/payment/controller/PaymentController.java), [security configuration](../../src/payment-service-java/src/main/java/com/flashsale/payment/security/SecurityConfig.java), and [API authorization tests](../../src/payment-service-java/src/test/java/com/flashsale/payment/unit/ProductionApiDocsSecurityTest.java). [PR #13](https://github.com/imtarget05/FlashSale-Backend/pull/13) closed anonymous Swagger/OpenAPI access outside demo. |
| PostgreSQL design, indexing, query optimization | `DEMONSTRATED` | [Reproducible query-plan experiment](../benchmarks/payment-service-postgres-query-plans.md) on synthetic data, merged in [PR #11](https://github.com/imtarget05/FlashSale-Backend/pull/11). Measurements do not represent production latency. |
| Oracle runtime, compiled gRPC endpoint, and GitLab CI execution | `NOT_YET` | Oracle driver/profile are configuration only; gRPC has a contract but no compiled runtime; no Oracle compatibility test or GitLab pipeline run is evidenced. |
| Redis cache fallback | `DEMONSTRATED` | [Cache fallback test](../../src/payment-service-java/src/test/java/com/flashsale/payment/unit/CacheFailureFallbackTest.java) and [handler wiring](../../src/payment-service-java/src/main/java/com/flashsale/payment/config/CacheConfig.java), merged in [PR #14](https://github.com/imtarget05/FlashSale-Backend/pull/14), demonstrate simulated cache get/put/evict failures. |
| Redis live outage and recovery | `NOT_YET` | No integration drill disconnects Redis during a request or proves recovery/cache consistency after reconnection. |
| RabbitMQ, transactional outbox, retry and DLQ | `DEMONSTRATED` | Java integration tests exercise broker-backed payment flows and malformed-message dead lettering; [outbox retry test](../../src/payment-service-java/src/test/java/com/flashsale/payment/unit/PaymentOutboxRelayTest.java) verifies a failed publish remains pending and can be retried. |
| JWT/RBAC, OIDC resource-server role mapping, and protected API docs | `DEMONSTRATED` | Negative tests cover invalid, expired, missing, and role-insufficient credentials; production OIDC validates issuer/JWKS and a required API audience, and maps a configurable `roles` claim. Mocked-decoder tests verify role mapping and that local login is denied; no live identity provider/JWKS run is claimed. |
| OIDC signing-key rotation rehearsal and gateway rate-limit enforcement | `PARTIAL` | A local test simulates publishing a new JWKS key alongside the old key and verifies the same decoder accepts tokens signed by both. No live issuer rotation has run. The selected rate-limit boundary is an API gateway, but no gateway is configured or verified in this repository. |
| Kafka and ActiveMQ runtime | `THEORY_ONLY` | The [interview notes](./flashsale-java-interview-notes.md) discuss these brokers; this service uses RabbitMQ and has no Kafka/ActiveMQ runtime test. |
| Docker build, image scan, GitHub Actions, and local Kubernetes rendering | `DEMONSTRATED` | Build/scan and repository CI evidence exist; manifests render locally. This does not demonstrate a live AKS workload or production deployment. |

### Production OIDC configuration boundary

The `production`/`prod` profile uses `OIDC_ISSUER_URI` (HTTPS), `OIDC_AUDIENCE`,
and `OIDC_AUTHORITIES_CLAIM` (default `roles`). The configured issuer's
discovery/JWKS metadata is used for signature and issuer validation; tokens
must carry the configured audience and the role claim must contain values such
as `CUSTOMER`, `STAFF`, or `ADMIN`. Local tests verify the mocked decoder's
authorization behavior and simulate JWKS overlap with a real RSA decoder; they
do not validate a live provider or operational key rotation. Production request
authentication does not require the legacy `JWT_SIGNING_KEY`; that key remains
required by non-production HS256 profiles. Local `/api/auth/**` login is denied
under production profiles. Rate limiting is assigned to an API gateway; `wrangler.toml` configures only the
frontend Pages build, and no payment-service gateway integration or 429 test
exists in this repository yet.

The [PR #14 source SHA `4e1a662`](https://github.com/imtarget05/FlashSale-Backend/commit/4e1a662)
passed local `mvn -B -q -f src/payment-service-java/pom.xml clean verify`:
81 tests with zero failures, errors, or skips. A later local verification on the
current working branch passed 88 tests; a subsequent rerun could not start two
RabbitMQ-backed test classes because the Testcontainers broker did not become
ready before timeout. It merged as
[`7d6e37a`](https://github.com/imtarget05/FlashSale-Backend/commit/7d6e37ab98ce2af980fa2a5bf1d568d26f962c8f)
after all PR checks passed. The production deploy jobs were skipped; repository
CI is not production readiness.

## Backend Java

| Requirement | Repository evidence | Honest status |
|---|---|---|
| Java 17 and Spring Boot | `src/payment-service-java/pom.xml` | Java 17 is configured; Spring Boot is 4.0.9, not the JD's exact 3.2.5. |
| REST, JWT, Spring Security, JPA, PostgreSQL | Payment controller, security filters, JPA entities, PostgreSQL profile and integration tests | Implemented and locally tested. |
| Redis and RabbitMQ | Cache and transactional outbox / consumer configuration | Implemented; integration coverage exists. Kafka is not part of this Java service. |
| MongoDB / NoSQL | Optional RabbitMQ-fed audit projection; PostgreSQL remains authoritative | Implemented and locally integration-tested. |
| gRPC | Protocol Buffers contract (`src/main/proto/payment.proto`) defining unary `ProcessPayment` and `CheckHealth` RPCs. Java listener classes were removed because `protoc` binary is unavailable offline; no compiled gRPC service or deployed endpoint is claimed. | `THEORY_ONLY` — contract spec exists; runtime not compiled or tested locally. |
| Oracle | JDBC driver and Oracle Spring profile | Configuration only; no Oracle runtime or compatibility test has been performed. |
| Kubernetes / AKS | Kustomize manifests (namespace, configmap, secret, deployment, service ClusterIP, hpa, ingress, networkpolicy, pdb) restructured into production-grade separate files; explicitly gated deploy script | Local render/YAML checks pass; no cluster apply or AKS runtime evidence. |

## Azure / Infrastructure

| Requirement | Repository evidence | Honest status |
|---|---|---|
| Azure resources and IaC | `infrastructure/terraform/` includes Azure Container Apps, ACR, PostgreSQL Flexible Server, Redis, Service Bus, Log Analytics, and a separate backup-storage configuration | Terraform validates locally; no apply, live plan, or operational evidence. The Java service's AKS manifests do not create an AKS cluster. |
| Azure VM, App Service, AKS, VNet, load balancer / Application Gateway | No corresponding Azure resource definitions in this repository's Terraform | Not demonstrated here. Kubernetes manifests alone do not prove AKS experience. |
| Azure Blob / Files / Disk | Backup-storage Terraform has an Azure Storage Account and containers | IaC evidence only; no deployed storage or restore drill verified here. |
| Azure Monitor / Log Analytics | A Log Analytics workspace exists in Terraform; application metrics expose Prometheus | Partial configuration only; Azure Monitor exporter, Application Insights wiring, and live telemetry are not verified. |
| CI/CD | GitHub Actions exist; `azure-pipelines.yml` adds Java build/test and local image-build CI | Azure DevOps file is not connected to a project or run remotely; no Azure DevOps deployment pipeline or GitLab/Jenkins pipeline is demonstrated. Render/Cloudflare deployment still requires owner-configured GitHub secrets and variables. |
| Banking compliance, cloud cost optimization, on-prem migration | No audit, control evidence, measured cost baseline, or migration execution evidence in this repository | Not demonstrated; do not claim based on this code. |

## Validation boundary

The Java source at release SHA
`07b77bd5f06ceadb2a252d6e810db8cc8acb7109` was revalidated locally. The
worktree had no Java-source changes from that SHA. These commands passed:

| Check | Command / evidence | Result |
|---|---|---|
| Unit and integration tests | `mvn -B clean verify -f src/payment-service-java/pom.xml` | Exit 0; 76 tests, 0 failures/errors/skips. PostgreSQL, Redis, RabbitMQ and MongoDB Testcontainers-backed coverage ran. |
| Docker image build | `docker build -t flashsale/payment-service-java:evidence-07b77bd -f src/payment-service-java/Dockerfile src/payment-service-java` | Exit 0. |
| Trivy image gate | `trivy image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 flashsale/payment-service-java:evidence-07b77bd` | Exit 0; 0 findings reported for scanned OS/JAR/gobinary targets. This is not a zero-vulnerability guarantee. |
| Local Docker smoke | `docker compose -p flashsale-release-evidence -f docker-compose.yml -f docker-compose.mongodb.yml -f - up --build --detach --wait --wait-timeout 180 payment-service-java` (stdin override assigned unique container names), then `SMOKE_BASE_URL=http://127.0.0.1:8081 DEMO_AUTH_PASSWORD=<ephemeral> src/payment-service-java/scripts/smoke-local.sh` | Exit 0; ephemeral credentials; login, success, same-key replay, changed-payload 409, decline, timeout, and asynchronous Mongo audit projection passed. Test containers and volumes were removed afterward. |

The same-SHA hosted checks also passed:
[Payment Service CI](https://github.com/imtarget05/FlashSale-Backend/actions/runs/42152620528),
[backend CI](https://github.com/imtarget05/FlashSale-Backend/actions/runs/42152614208),
and [release verification](https://github.com/imtarget05/FlashSale-Backend/actions/runs/42152614201).
The release verification's preview and production deployment jobs were skipped.

The later [manual workflow run 42154887759](https://github.com/imtarget05/FlashSale-Backend/actions/runs/42154887759)
used the same SHA. Its verification job passed, but the production deploy job
failed on the initial POST to the masked `RENDER_DEPLOY_HOOK_PRODUCTION` value:
HTTP 404 and curl exit 22, before the health-check loop ran. The repository
blueprint defines `flashsale-payment-api-prod` on `main` with auto-deploy
disabled and `/health/live`; the workflow's non-empty setting checks passed.
The actual hook URL is a GitHub environment secret and is not inspectable from
the repository. No repository-controlled mismatch was identified, but the
secret's target and the Render account/service state cannot be verified here.
The account owner must verify or regenerate a hook for that Render service and
correct the production environment secret; also confirm
`RENDER_PRODUCTION_URL` targets the same service. Do not retry the production
workflow until the owner confirms the settings. No production deploy was
triggered during this assessment, and this result is not evidence of a live
deployment.

| Claim | Status | Evidence boundary |
|---|---|---|
| Java payment behavior, transactional outbox and idempotency | `DEMONSTRATED` | Local tests and same-SHA hosted CI passed. Integration coverage includes concurrent same-payload requests producing one payment/outbox row, conflicting payload rejection, duplicate order-event suppression, and outbox retry after broker failure. |
| Container build and configured HIGH/CRITICAL scan | `DEMONSTRATED` | Local image build and Trivy policy gate passed; no claim that every vulnerability class or future image is clean. |
| API smoke and audit projection | `DEMONSTRATED` | Passed against a local Docker Compose stack only. |
| Render/Azure cloud operation | `LAB_ONLY` for checked-in configuration; live operation is `OPEN` | The Render production hook returned 404. No successful live deploy, Azure apply, or production workload was verified. |
| Real payment processing, bank integration, compliance, production readiness | `OPEN` | The payment provider is simulated; there is no bank/PSP operation or compliance sign-off. |

The AKS repository is at
[`3fac87e1ad867940946a36e04d3afb3ffe71722e`](https://github.com/imtarget05/AKS-SRE-Platform/commit/3fac87e1ad867940946a36e04d3afb3ffe71722e).
Its `pr-gate`, GitOps validation, OIDC contract, and source SBOM workflows
passed on that SHA. These are repository/CI results, not a live AKS deployment.
The `aks-portfolio-dev` cluster is recorded as destroyed; the current Azure
context does not expose a matching cluster and the saved Kubernetes API
hostname does not resolve. No Azure resource was created or changed in this
assessment.

## Cloud lab gates C1–C4 — repository and local evidence only

Evidence snapshot: Java `07b77bd5f06ceadb2a252d6e810db8cc8acb7109`, AKS
`3fac87e1ad867940946a36e04d3afb3ffe71722e`, and QuanLyKho PR #14 head `3c982f2`
(merged to `main` as `5febecc`). These revisions do not establish personal work history
or current cloud operation.

### C1. Azure service and IaC inventory

| Service area | Repository evidence | Live status |
|---|---|---|
| VM / App Service / VPN / Application Gateway / Azure Files / managed disks | No dedicated resource implementation or verified operations evidence in the reviewed portfolio IaC. | `NOT_IMPLEMENTED` or `NOT_VERIFIED`; the read-only Azure inventory in AKS PR #30 found no VM, App Service, or VNet in the selected subscription. This is not an all-subscription claim. |
| AKS, VNet and subnets | Terraform modules in [AKS-SRE-Platform](https://github.com/imtarget05/AKS-SRE-Platform/tree/3fac87e1ad867940946a36e04d3afb3ffe71722e/terraform); main-branch Terraform and GitOps gates passed on the recorded SHA. | `LAB_ONLY`; the AKS cluster is destroyed. |
| Log Analytics and flow-log storage | AKS observability/network Terraform modules include a Log Analytics workspace and storage account. | Read-only inventory in AKS PR #30 found two Log Analytics workspaces and storage resources; ingestion, alert delivery, and restore remain unverified. |
| Azure Container Apps, ACR, PostgreSQL Flexible Server, Redis, Service Bus, backup storage | FlashSale Terraform contains these definitions; this is separate from the Java AKS manifests. | Configuration only; no deployment or restore drill is claimed. |

The Azure CLI context used for this assessment did not expose a matching
portfolio AKS cluster. The later
[AKS read-only inventory](https://github.com/imtarget05/AKS-SRE-Platform/pull/30)
found Container Apps, ACR, Storage and Log Analytics resources in the selected
subscription. This is not proof that every resource in every subscription is
absent; runtime and cost verification remain open.

### C2. Monitoring and incident exercise

The local `kind-local-platform` evidence in
[L10](https://github.com/imtarget05/AKS-SRE-Platform/blob/3fac87e1ad867940946a36e04d3afb3ffe71722e/docs/evidence/local-platform/l10-observability-PASS.md)
records a pinned Prometheus/Grafana stack, 25 active scrape targets, three
dashboards, and a local alert that fired and resolved with timestamps. This is
a local lab, not Azure Monitor or Log Analytics live evidence.
`AZURE_MONITOR_LIVE_OPEN`: no current Azure alert, KQL query, or cloud incident
drill is verified.

### C3. Cost and migration

The [2026-09-21 cost note](https://github.com/imtarget05/AKS-SRE-Platform/blob/3fac87e1ad867940946a36e04d3afb3ffe71722e/docs/evidence/phase7a/cost.md)
contains a historical Retail Prices API estimate. It is a modeled estimate,
not a Cost Management export, invoice, or current spend measurement. No
on-premises-to-Azure migration rehearsal or backup/cutover/rollback log is
available. Both live cost evidence and migration rehearsal are `NOT_RUN`.

### C4. Threat model, compliance boundary, and CI/CD

Repository-scoped threat model:

| Threat / asset | Evidence-backed control | Residual risk or boundary |
|---|---|---|
| Forged or over-privileged payment request | JWT validation, role checks, input constraints, and authentication/authorization tests in the simulated Java service. | No bank identity provider, production key-management/rotation evidence, or live authorization review. |
| Duplicate/replayed payment or message | PostgreSQL idempotency and conflict behavior are tested; payment and outbox writes share a transaction. | Outbox delivery is at-least-once; duplicates remain possible and must be handled idempotently by consumers. |
| Missing or exposed runtime credentials | The Java service fails closed when required signing/configuration values are absent; deployment manifests reference external Secrets. | Secret rotation, cloud vault integration, and production access review are not demonstrated. |
| Dependency or build-chain compromise | Java CI runs a Trivy gate; AKS CI runs source dependency/SBOM checks. | Passing configured scanners is not proof of zero vulnerabilities, signed provenance, or a bank-approved supply-chain control. |
| Confidentiality and availability of financial data | The payment workflow is a mock; readiness, retry/DLQ behavior, and local monitoring are tested. | No real card/bank data or PSP integration; no production recovery, retention, encryption-compliance, or incident-response evidence. |

This is a repository-level gap assessment, not a formal financial-sector threat
assessment. There is no PCI/bank audit, compliance certification, or bank
sign-off.

| Repository / revision | CI/CD evidence | Deployment boundary |
|---|---|---|
| FlashSale `07b77bd5` | GitHub Actions unit/integration, Docker, Trivy, backend CI, and release verification passed on the exact SHA. | Preview and production jobs were skipped; no cloud deployment is claimed. `azure-pipelines.yml` has no verified remote run; no GitLab/Jenkins pipeline is evidenced. |
| QuanLyKho PR #14, head `3c982f2` | Secret guard, lint, test, build, and SonarCloud checks passed on the PR head; merged as `5febecc`. | Repository evidence only; no deployment is claimed. |
| AKS-SRE-Platform `3fac87e` | `pr-gate`, GitOps validation, OIDC contract, and source SBOM workflows passed on the exact `main` SHA. | Argo CD forward/revert evidence is local `kind`; no live AKS deployment is claimed. |

## Match against the supplied Sacombank job descriptions

Status definitions: `DEMONSTRATED` means checked-in implementation plus
relevant tests; `LAB_ONLY` means code/configuration or offline validation
without live Azure operation; `PARTIAL` means only part of the stated
requirement is evidenced; `NOT_YET` means no evidence was found;
`USER_EVIDENCE_REQUIRED` cannot be established from repositories.

### Cloud / Infrastructure role

| Requirement | Evidence and limitation | Status |
|---|---|---|
| Design and operate enterprise Azure architecture | AKS, network, identity, and observability Terraform modules exist in [AKS-SRE-Platform](https://github.com/imtarget05/AKS-SRE-Platform/tree/main/terraform). The live cluster is recorded as destroyed; there is no current operational proof. | `LAB_ONLY` |
| VM / App Service operations | No verified live VM or App Service operation in these repositories. | `NOT_YET` |
| AKS, VNet, subnets, network security, private DNS | Terraform modules and tests exist; historical evidence is not a current deployment. Current pod state, live networking, and rollback are unverified. | `LAB_ONLY` |
| Azure Storage | Storage-account IaC exists for flow logs/backup; no current live storage or restore drill is verified. | `LAB_ONLY` |
| CI/CD pipelines | GitHub Actions test/build/scan passed on the Java SHA above. `azure-pipelines.yml` is checked in but no remote Azure DevOps run is evidenced; no GitLab/Jenkins pipeline is shown. | `PARTIAL` |
| Terraform / IaC | Terraform modules, offline tests, and hosted GitOps validation exist. No current Azure apply or live plan is claimed. | `LAB_ONLY` |
| Azure Monitor / Log Analytics | Log Analytics and collection-rule configuration exist; no current live alert, incident, or Azure telemetry evidence is available. | `LAB_ONLY` |
| Financial-sector compliance/security sign-off | JWT/RBAC, secret handling, and a Trivy HIGH/CRITICAL gate are demonstrated for a simulated service. No bank audit, compliance certification, or sign-off exists. | `NOT_YET` |
| Cloud cost optimization | No current cost export, measured baseline, or optimization experiment is evidenced. | `NOT_YET` |
| On-premises migration | No migration rehearsal or cutover/rollback evidence is present. | `NOT_YET` |
| Docker / Kubernetes | Docker image build and Kubernetes manifest rendering are verified. No live AKS workload is verified. | `PARTIAL` |
| Python / PowerShell scripting | Python backend code exists in [QuanLyKho](https://github.com/imtarget05/QuanLyKho); this does not establish PowerShell or Azure-operations scripting experience. | `PARTIAL` |
| 5+ years Cloud, 3+ years Azure, education, and team collaboration | These are personal-history/credential claims, not repository facts. | `USER_EVIDENCE_REQUIRED` |

### Backend Developer role

| Requirement | Evidence and limitation | Status |
|---|---|---|
| Java, Spring Boot, Spring Security, Spring Data | Java 17/Spring Boot payment service, REST APIs, JWT role checks, JPA, unit and integration tests. | `DEMONSTRATED` |
| REST and gRPC | REST contracts/services have tests. gRPC Protocol Buffers contract exists but Java listener is not compiled locally (no `protoc` binary); no deployed endpoint is claimed. | `PARTIAL` — REST demonstrated; gRPC contract only. |
| Microservices / event-driven processing | Payment service uses RabbitMQ, a PostgreSQL transactional outbox, confirms, retries, and dead-letter handling with integration coverage. | `DEMONSTRATED` |
| Kafka or ActiveMQ | This Java service intentionally uses RabbitMQ and does not include Kafka or ActiveMQ runtime evidence. | `NOT_YET` for the named brokers |
| Redis | Spring Cache is used for payment lookups with eviction on payment processing; PostgreSQL remains the source of truth. No Redis lock or overselling claim is made. | `DEMONSTRATED` |
| Database design, indexing, query optimization | PostgreSQL constraints and idempotency concurrency are tested. A [local PostgreSQL query-plan experiment](../benchmarks/payment-service-postgres-query-plans.md) measures the primary-key lookup and partial outbox index against 100,000 synthetic rows per table; it is not a production benchmark or an index change. Oracle runtime remains untested. | `PARTIAL` |
| Oracle and NoSQL | MongoDB audit projection is implemented and integration-tested. Oracle driver/profile configuration exists, but Oracle runtime compatibility is untested. | `PARTIAL` |
| GitLab / GitFlow | Git/GitHub branches, PR checks, and GitHub Actions are evidenced; GitLab CI and a claimed GitFlow practice are not. | `PARTIAL` |
| JWT, cryptography, and OWASP controls | JWT HMAC key validation, BCrypt passwords, stateless role authorization, and input constraints are implemented. RSA and a complete OWASP/security certification are not evidenced. | `PARTIAL` |
| Unit/integration testing and vulnerability patching | 76 local tests pass on `07b77bd5`; same-SHA hosted unit/integration, Docker, and configured Trivy HIGH/CRITICAL gates passed. Local Docker smoke also verified replay, 409 conflict, and async Mongo audit projection. No claim of zero vulnerabilities or live deployment. | `DEMONSTRATED` |
| English technical reading, 1+ year experience, degree, and cross-team delivery | Repository docs include English README/ADRs; language proficiency, employment duration, degree, and collaboration require personal evidence. | `USER_EVIDENCE_REQUIRED` |
| Banking/payment production experience | Payment and inventory financial flows are simulations; there is no real PSP, bank integration, or production banking operation. | `NOT_YET` |

The inventory business workflow is additionally evidenced in
[QuanLyKho PR #14](https://github.com/imtarget05/QuanLyKho/pull/14); it is a
Python/PostgreSQL project, not Java experience. Use the
[Java-scoped interview notes](./flashsale-java-interview-notes.md), not
cross-repository examples, when discussing this service.

## Overall fit

The repositories provide strong evidence for several backend engineering
requirements, but **do not satisfy every requirement in the supplied JDs**.
The cloud role has substantial live-operations, cost, migration, and years-of-
experience gaps. The backend role has specific gaps around Kafka/ActiveMQ,
Oracle, RSA, and personal experience/education evidence. This assessment is
not a hiring prediction or bank approval.
