# Azure Flash-Sale Order Reliability Platform

## 📖 Overview
A portfolio-grade, cloud-native backend that sells limited inventory (e.g. 100 units) to
a hype-driven crowd without overselling or crushing the database. Built evidence-first:
every architecture decision traces to a measured failure (see `docs/benchmarks/`,
`docs/adr/`, `docs/incidents/`).

## 🚀 The Journey (measured, not claimed)
| Phase | Design | Result (same 50-buyer harness) |
|---|---|---|
| 1 | Naive sync API + PostgreSQL | works for 1 request |
| 2 | + concurrency | **oversold 5x** (50/50 accepted, stock 10→9) |
| 3 | atomic conditional UPDATE | 10 accepted / 40×409, stock exactly 0, p95 245 ms |
| 4 | + 200 concurrent load | still correct, p95 574 ms, 90% wasted DB hits → justified Redis |
| 5 | Redis fast-fail + async worker | **10 accepted / 40×409, p95 29 ms**, idempotent |

Docs: `docs/architecture/current-architecture.md`, `docs/adr/001..004`,
`docs/benchmarks/`, `docs/incidents/001`, `docs/interview-notes/`.

## 🛠️ Technology Stack
- **Backend**: .NET 10 minimal APIs (`Order.Api`, `Order.Worker`, `FlashSale.Domain/Application/Infrastructure`)
- **Data**: PostgreSQL (source of truth) · **Cache/reservation**: Redis (Lua CAS)
- **Messaging**: queue abstraction (`IOrderQueueProducer/Consumer`) — InMemory (tests) / RabbitMQ (local) / Azure Service Bus (Azure, ADR-005)
- **Compute**: Docker → Azure Container Apps → AKS
- **IaC**: Terraform (`infrastructure/terraform/`) · **CI/CD**: GitHub Actions
- **Testing**: Python concurrency harness + k6 scripts (`load-tests/`)

## 🧪 Run locally
```bash
docker compose up -d postgres redis
dotnet run --project src/Order.Api --urls http://localhost:5065
# baseline:      python3 load-tests/concurrency/oversell_demo.py --stock 10 --requests 50
# higher load:   python3 load-tests/concurrency/oversell_demo.py --stock 20 --requests 200
# k6 (optional): k6 run load-tests/k6/flash-sale.js
```
Idempotency demo:
```bash
KEY=$(uuidgen)
curl -i -X POST localhost:5065/api/orders -H "Content-Type: application/json" \
     -H "Idempotency-Key: $KEY" -d '{"productId":1,"quantity":1}'   # 202
curl -i -X POST localhost:5065/api/orders -H "Content-Type: application/json" \
     -H "Idempotency-Key: $KEY" -d '{"productId":1,"quantity":1}'   # 409 duplicate
```

## 🚢 Deploy to Azure (Phase 7–9)
1. `cd infrastructure/terraform && terraform init && terraform apply` (provisions ACR,
   PostgreSQL, Redis, Service Bus, Container Apps, Log Analytics).
2. GitHub Actions `deploy.yml` builds + pushes images and updates Container Apps.
3. For the AKS/GitOps topology see the sibling repo `AKS-SRE-Platform`
   (ArgoCD watches `imtarget05/FlashSale-Backend@main` → `infrastructure/kubernetes/overlays/prod`).

## 📁 Repository Structure (Clean Architecture)

```
src/
  FlashSale.Domain/          Entities + value objects + domain exceptions (no dependencies)
  FlashSale.Application/     Use cases (OrderProcessor) + ports (repositories, queue, reservation)
  FlashSale.Infrastructure/  Adapters: EF Core/PostgreSQL, Redis, In-Memory + Service Bus queue, worker host
  Order.Api/                 Presentation + composition root (minimal API)
  Order.Worker/              Composition root for the async worker (Service Bus in Azure)
tests/
  UnitTests/                 Use-case tests with fakes + architecture dependency-rule guards
load-tests/                  concurrency harness (Python) + k6 scripts
infrastructure/              terraform (Azure stack) · kubernetes (kustomize base/prod overlays)
docs/                        requirements · architecture · adr · benchmarks · incidents · interview-notes
plans/ tasks/                roadmap, current plan, task tracking (AGENTS.md workflow)
```

Dependency direction: `Domain ← Application ← Infrastructure ← Api/Worker`
(enforced automatically by `tests/UnitTests/ArchitectureTests.cs`).

Development guidelines: `AGENTS.md`.
