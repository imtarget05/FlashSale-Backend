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

The current local evidence includes `mvn clean verify`, a local Docker build,
Kustomize rendering, Terraform format/validation, and deploy-script linting.
It does not include Azure provisioning, cloud deployment, Azure DevOps execution,
production security certification, or proof of five years' experience.
