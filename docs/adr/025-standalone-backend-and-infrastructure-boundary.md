# ADR 025: Standalone Backend Architecture & Infrastructure Boundary

## Status
Accepted

## Context
In earlier iterations of the multi-project portfolio, FlashSale / OpsBridge was occasionally coupled conceptually with other applications (e.g. QuanLyKho or retail finance) or assumed to be subordinate to the `AKS-SRE-Platform` repository for deployment coordination. This hindered the core technical value proposition of FlashSale as a standalone, deep backend and distributed systems demonstration project.

## Decision
1. **Product & Technical Specialization**: FlashSale / OpsBridge is maintained as an independent backend & distributed systems codebase (.NET 10, C# 13) focused on:
   - High-concurrency flash sale inventory reservations with pessimistic and atomic conditional updates.
   - Redis fast-fail cache reservation with Lua atomic scripting.
   - Transactional Outbox pattern with at-least-once messaging (RabbitMQ / Azure Service Bus).
   - Saga orchestration, compensation transactions, and idempotency guards.
   - 3-way discrepancy reconciliation (PO, Goods Receipt, Invoice).
2. **Infrastructure Independence**:
   - The project owns its entire persistence and broker definitions in `infrastructure/kubernetes/base/data/` (PostgreSQL, Redis, RabbitMQ, Kafka) and Kustomize overlays.
   - FlashSale owns its PostgreSQL schema (`FlashSaleDb`) and database instance; it shares zero database instances or tables with any other application.
   - Release manifests (`infrastructure/kubernetes/overlays/azure-cloud`) and Argo CD Application manifests (`infrastructure/argocd/flashsale-application.yaml`) are owned by this repository.
3. **Cross-System Interaction**:
   - Interactions with warehouse management systems (such as QuanLyKho) are mediated strictly via signed HMAC webhooks (`/opsbridge/webhooks/goods-receipt`), preserving complete domain and data isolation.

## Consequences
- **Positive**:
  - Independent build, test, and release cycle: `dotnet test` validates all domain logic, transaction invariants, outbox compensation, and concurrency rules standalone.
  - Zero coupling to platform repository deployment manifests or foreign database schemas.
  - High-value interview showcase for Backend & Distributed Systems engineering.
