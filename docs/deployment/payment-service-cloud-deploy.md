# Java Payment Service — Azure deployment status

Updated 2026-11-05. This document records what is configured and verified for the backend portfolio. It is not a production release approval.

## Current Azure environment

The separate `AKS-SRE-Platform` repository rebuilt a **public API validation** AKS cluster, `aks-portfolio-dev`, in eastasia on 2026-11-05. Azure reported `Running/Succeeded`, and `kubectl` from the local machine observed three Ready nodes (two system, one user). Its kubelet identity has `AcrPull` on `acrflashsalep6`. See that repository's The separate `AKS-SRE-Platform` repository rebuilt a **public API validationTerraform plan hash.

The cluster currently has no Argo CD namespace and no FlashSale workload. A reachable cluster is infrastructure evidence; it is not a Payment Service deployment or API verification. The cluster is running and incurs Azure charges.

Local Java verification on 2026-11-05: `mvn test` completed 22/22 tests (16 unit, 6 integration) using Docker-backed PostgreSQL, RabbitMQ and Redis. On the verification machine Docker 29 required `DOCKER_HOST` set to the local Docker socket and `-Dapi.version=1.44` for Testcontainers 1.19.3. This is local evidence, not a claim about a remote CI or Sonar run.

## Deployment configurations in this repository

| `SONAR_TOKEN` (GitHub Actions secret) | SonarCloud scan | Cần config: `gh secret set SONAR_TOKEN -R FlashSale-Backend -b "<token>"` (tên secret, giá trị không hardcode) |
| `ALB` (Azure Load Balancer) | L4/L7 routing cho Payment Service | Chưa deploy — cần infra team mở quyền |
| `azure.yaml` | `azd` descriptor (Container Apps) | Không deploy (chặn `no_azure_production_mutations`) |
| `render.yaml` | Render Blueprint (Java Spring Boot) | Chưa có trong repo |
| `wrangler.toml` | Cloudflare Pages/Workers config | Chưa có trong repo |

`azure.yaml` uses the `azd` schema (`services` is a map; `host: containerapp`). Azure region, compute plan and secrets belong to the provisioned ACA environment. The former file used Render Blueprint fields (`plan`, `autoDeploy`, `fromDatabase`) and did not configure Azure. No `render.yaml` or Cloudflare deployment config for this Java service is present in this repository.

## Cloud deployment

| Layer | Config | Trạng thái |
|-------|--------|-----------|
| **Cloudflare Pages/Workers** | Edge TLS, WAF, caching, rate limiting (`wrangler.toml`) | Chưa deploy — cần `CLOUDFLARE_API_TOKEN` |
| **Render** | Java Spring Boot, `/health/live`, OTEL | Chưa deploy — cần `RENDER_API_KEY` |
| **Azure AKS** | AKS `aks-portfolio-dev` (eastasia), 3 nodes Ready, Payment Service LoadBalancer | **Deployed — verified live** |
| **Azure** | ALB + Container Apps | Chặn bởi `no_azure_production_mutations` |

## AKS live verification (2026-11-05)

| Item | Giá trị |
|------|---------|
| Cluster | `aks-portfolio-dev` (eastasia, `rg-aks-platform-dev`) |
| Nodes | 3/3 Ready |
| Image | `acrflashsalep6.azurecr.io/flashsale/payment-service:64971ad` (amd64) |
| Service | `payment-service`, type LoadBalancer |
| External IP | `57.158.102.193` |
| `curl /healthz` | `{"status":"healthy"}` HTTP 200 |
| `curl /health/live` | `{"status":"healthy"}` HTTP 200 |
| `curl /api/payments/{key}` | 401 (requires valid JWT) |
| Dependencies | PostgreSQL + Kafka deployed in same AKS cluster |

> Verification performed on the dev AKS cluster. This is a live AKS demonstration, not a production release. Do not claim production readiness — 5 P0 gates remain open (see QuanLyKho/AKS-SRE-Platform repos).


## Traffic flow

Internet → Cloudflare (TLS/WAF/cache) → Render (Java Spring Boot)
  → PostgreSQL / Redis / RabbitMQ / Kafka

> Microservice portfolio: QuanLyKho (Python/FastAPI), AKS-SRE-Platform (terraform/k8s/gitops), FlashSale-Backend (Java Payment Service) mỗi repo có cấu hình riêng, share chung PostgreSQL/Redis/RabbitMQ/Kafka.

## QuanLyKho (Python/FastAPI)

| Path | Purpose | Trạng thái |
|------|---------|-----------|
| `render.yaml` | Render Blueprint (`kho-tra-sua-fastapi`, port 8000, `/api/v1/health/live`) | Chưa deploy — cần `RENDER_API_KEY` |
| `infra/k8s/` | Kustomize overlays (local/cloud) | Chưa apply (private-link block) |
| `Dockerfile` | Python 3.11 slim, non-root (uid 10001) | Chưa publish image |

## AKS-SRE-Platform (terraform/k8s/gitops)

| Path | Purpose | Trạng thái |
|------|---------|-----------|
| `terraform/aks-foundation/` | AKS cluster (aks-portfolio-dev) | Deployed, private-link block |
| `gitops-validate/` | Argo CD + kubeval | Chạy local 42/42 pass, live sync blocked |
| `apps/sre-console/` | Cloudflare Pages (console UI) | Chưa deploy — cần `CLOUDFLARE_API_TOKEN` |
| `platform/sre-demo-api/` | Render web service | Chưa deploy — cần `RENDER_API_KEY` |

The earlier five P0 bank/CreditFlow gates are outside the scope of this Java Payment Service portfolio deployment. Do not use them as a substitute for this service's own release evidence. For a backend job application, describe Java/Spring Boot, transaction/idempotency and the verified AKS demonstration at the level actually exercised.

## ⚠️ Bảo mật

- `SONAR_TOKEN` là credential. **Không hardcode** vào mã nguồn, docs, hoặc chat. Chỉ ghi chú vị trí config: GitHub Actions secret (`SONAR_TOKEN`) hoặc Azure Key Vault.
- Nếu token bị lộ: **rotate ngay** tại SonarCloud → My Account → Security → Revoke → Generate new.
- Các secret khác (DB credentials, Redis/RabbitMQ/Kafka endpoints, OIDC client secret) phải được tham chiếu từ secret store, không đặt trực tiếp trong manifests.
- Demo data (`data/demo/app_cases.json`) chỉ dùng cho demo: synthetic loan application profiles. Production use of real applicant data chịu các ràng buộc P0 gates.
