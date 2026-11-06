# Sacombank JD evidence — repository-only assessment

This is a map of verifiable artifacts in this repository, not a claim of
employment history, years of experience, production operation, or bank
compliance. A checked-in configuration is not evidence that a cloud resource
was deployed or operated.

## Backend Java

| Requirement | Repository evidence | Honest status |
|---|---|---|
| Java 17 and Spring Boot | `src/payment-service-java/pom.xml` | Java 17 is configured; Spring Boot is 4.0.9, not the JD's exact 3.2.5. |
| REST, JWT, Spring Security, JPA, PostgreSQL | Payment controller, security filters, JPA entities, PostgreSQL profile and integration tests | Implemented and locally tested. |
| Redis and RabbitMQ | Cache and transactional outbox / consumer configuration | Implemented; integration coverage exists. Kafka is not part of this Java service. |
| MongoDB / NoSQL | Optional RabbitMQ-fed audit projection; PostgreSQL remains authoritative | Implemented and locally integration-tested. |
| gRPC | Protocol Buffers contract, unary payment/health RPC, JWT authentication and Staff/Admin authorization | Implemented and tested. Listener defaults off and requires TLS files; no deployed endpoint is claimed. |
| Oracle | JDBC driver and Oracle Spring profile | Configuration only; no Oracle runtime or compatibility test has been performed. |
| Kubernetes / AKS | Kustomize manifests and explicitly gated deploy script | Local render/YAML checks pass; no cluster apply or AKS runtime evidence. |

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

The Java service has local `mvn clean test` (76 tests), Docker build, Kustomize
rendering, and a negative startup check with the signing key unset. Its hosted
unit/integration/Docker/Trivy gate passed on
[`dfaf73a`](https://github.com/imtarget05/FlashSale-Backend/actions/runs/42151304486).
The AKS repository has Terraform modules and offline validation evidence, but
its own recruiter evidence says the `aks-portfolio-dev` cluster was destroyed;
the current Azure context does not expose a matching cluster and the saved
Kubernetes API hostname does not resolve. No Azure resource was created or
changed in this assessment.

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
| REST and gRPC | REST and unary gRPC contracts/services have tests. The gRPC listener is disabled by default and requires TLS files when enabled; no deployed endpoint is claimed. | `DEMONSTRATED` |
| Microservices / event-driven processing | Payment service uses RabbitMQ, a PostgreSQL transactional outbox, confirms, retries, and dead-letter handling with integration coverage. | `DEMONSTRATED` |
| Kafka or ActiveMQ | This Java service intentionally uses RabbitMQ and does not include Kafka or ActiveMQ runtime evidence. | `NOT_YET` for the named brokers |
| Redis | Spring Cache is used for payment lookups with eviction on payment processing; PostgreSQL remains the source of truth. No Redis lock or overselling claim is made. | `DEMONSTRATED` |
| Database design, indexing, query optimization | PostgreSQL schema constraints and idempotency concurrency are tested. No measured `EXPLAIN ANALYZE` optimization or Oracle runtime is evidenced. | `PARTIAL` |
| Oracle and NoSQL | MongoDB audit projection is implemented and integration-tested. Oracle driver/profile configuration exists, but Oracle runtime compatibility is untested. | `PARTIAL` |
| GitLab / GitFlow | Git/GitHub branches, PR checks, and GitHub Actions are evidenced; GitLab CI and a claimed GitFlow practice are not. | `PARTIAL` |
| JWT, cryptography, and OWASP controls | JWT HMAC key validation, BCrypt passwords, stateless role authorization, and input constraints are implemented. RSA and a complete OWASP/security certification are not evidenced. | `PARTIAL` |
| Unit/integration testing and vulnerability patching | 76 local Java tests pass; hosted unit/integration/build and configured Trivy HIGH/CRITICAL scanning pass on `dfaf73a`. This is not a claim of zero vulnerabilities. | `DEMONSTRATED` |
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
