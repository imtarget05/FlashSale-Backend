# Java Payment Service — Azure deployment status

Updated 2026-11-05. This document records what is configured and verified for the backend portfolio. It is not a production release approval.

## Current Azure environment

The separate `AKS-SRE-Platform` repository rebuilt a **public API validation** AKS cluster, `aks-portfolio-dev`, in eastasia on 2026-11-05. Azure reported `Running/Succeeded`, and `kubectl` from the local machine observed three Ready nodes (two system, one user). Its kubelet identity has `AcrPull` on `acrflashsalep6`. See that repository's `docs/evidence/2026-11-05-azure-aks-rebuild.md` for the command results and Terraform plan hash.

The cluster currently has no Argo CD namespace and no FlashSale workload. A reachable cluster is infrastructure evidence; it is not a Payment Service deployment or API verification. The cluster is running and incurs Azure charges.

Local Java verification on 2026-11-05: `mvn test` completed 22/22 tests (16 unit, 6 integration) using Docker-backed PostgreSQL, RabbitMQ and Redis. On the verification machine Docker 29 required `DOCKER_HOST` set to the local Docker socket and `-Dapi.version=1.44` for Testcontainers 1.19.3. This is local evidence, not a claim about a remote CI or Sonar run.

## Deployment configurations in this repository

| Path | Purpose | Current limit |
| --- | --- | --- |
| `src/payment-service-java/Dockerfile` | Multi-stage Java 17 container build | Image has not been published to ACR from this revision |
| `src/payment-service-java/azure.yaml` | Azure Developer CLI descriptor for a possible **Container Apps** deployment | No ACA infrastructure module, environment, or deployed service is provided; this file is not an AKS manifest |
| `infrastructure/kubernetes/overlays/azure-cloud/` | Historical AKS Kustomize overlay | Its payment Deployment still has `ASPNETCORE_*` variables and a .NET connection-string contract; it does **not** deploy the Java service as written |
| `.github/workflows/payment-service-java-ci.yml` | Build, tests and image scan | Does not publish a SHA-pinned Java image to ACR or promote it to AKS |

`azure.yaml` uses the `azd` schema (`services` is a map; `host: containerapp`). Azure region, compute plan and secrets belong to the provisioned ACA environment. The former file used Render Blueprint fields (`plan`, `autoDeploy`, `fromDatabase`) and did not configure Azure. No `render.yaml` or Cloudflare deployment config for this Java service is present in this repository.

## Work required for an AKS backend demonstration

1. Make the Java service's Kubernetes Deployment and Service explicit. Replace the historical .NET payment env names with Spring settings; add resource requests, non-root security, and `/health/live` plus readiness probes. Keep the AKS user pool as its scheduling target.
2. Provide PostgreSQL schema migration and secret references, plus reachable Redis/RabbitMQ/Kafka endpoints. Do not put credentials in manifests. The current Java service depends on these systems; a health response alone is not a transaction proof.
   Provide a production `UserDetailsService` as well: the in-memory demo users are active only with the `dev` profile, while authentication requires that interface at startup.
3. Build and scan the Java image, publish it to `acrflashsalep6` with an immutable SHA or digest, and reference that exact artifact in the AKS overlay. Keep the source commit, image digest and deployed revision together in evidence.
4. Render and validate the overlay before promotion. Deploy to the validation cluster, then verify pod readiness, authenticated payment API, idempotent replay/conflict, one database write, and the intended message flow. Record both success and failure cases.
5. Record costs and a teardown decision for the validation cluster. A bank production environment requires its own identity, private networking and owner approvals; this portfolio cluster does not supply those controls.

The earlier five P0 bank/CreditFlow gates are outside the scope of this Java Payment Service portfolio deployment. Do not use them as a substitute for this service's own release evidence. For a backend job application, describe Java/Spring Boot, transaction/idempotency and the verified AKS demonstration at the level actually exercised.
