# Master Portfolio Asset Registry — Multi-Repo Engineering Platform

> **Registry Date**: 2026-10-22  
> **Audited Scope**: 5 Full-Scale Repositories (`OpsBridge`, `retail-finance-core-platform`, `QuanLyKho`, `AKS-SRE-Platform`, `SmartDocumentChatbot`)  
> **Requirement Reference**: Closes **G17** & **G18** from the Gap Register.

---

## 1. Portfolio Asset Catalog

| Repository | Tech Stack | Architectural Style | Core Business Mission | Primary Engineering Invariant | Test Evidence & CI Status |
|---|---|---|---|---|---|
| **OpsBridge** | .NET 10, C# 13, EF Core, Redis, RabbitMQ, PostgreSQL | Clean Architecture (DDD), Transactional Outbox | Flash-sale order placement with extreme concurrency & 3-way supplier reconciliation | **Zero Oversell**: Redis Lua CAS admission + Authoritative conditional SQL (`Stock >= @qty`) | **310 PASS / 0 FAIL** (`dotnet test AzureFlashSale.slnx`); 50/10 concurrency harness verified |
| **retail-finance-core** | Java 17, Spring Boot 3, MySQL 8, Testcontainers, Python | Layered Architecture (Ports & Adapters) | Core banking general ledger & automated transaction reconciliation | **Double-Entry Balance**: Total Debits = Total Credits; Account version optimistic lock | **26 PASS / 0 FAIL** (`mvn test`); GitHub Actions run 39630546625 green |
| **QuanLyKho** | Python 3.11, FastAPI, SQLAlchemy 2.0, PostgreSQL 16 | Modular Monolith (Clean) | Multi-branch warehouse ingredient management & cash simulation | **Two-Man Rule (Maker-Checker)**: `maker_id != checker_id`; FEFO batch expiration ordering | **387 PASS, 8 SKIP, 0 FAIL** (`pytest backend/tests`); keyset pagination verified |
| **AKS-SRE-Platform** | Kubernetes (kind / AKS), Terraform, ArgoCD, Prometheus | Cloud Native SRE Platform, GitOps, Zero-Trust | High-reliability Kubernetes runtime, GitOps delivery, observability & disaster recovery | **Zero-Downtime Resilience**: PDB drain tolerance, Calico network isolation, 0 cloud leak | **47 PASS / 0 FAIL** (`python tests/test_gates.py`); 5 production SRE runbooks verified |
| **SmartDocumentChatbot** | Next.js 14, FastAPI, LangChain/LlamaIndex, PostgreSQL | Service-Oriented (Microservices) | Document intelligence, corrective RAG (CRAG) & agent query orchestration | **Multi-Tenant Isolation**: Owner-filtered vector retrieval; Resilience4j circuit-breaker fallback | Verified test suite; ADRs 0001–0010 enforced; DB-backed durable ingestion queue |

---

## 2. Invariants & Concurrency Enforcement Matrix

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│                                INVARIANT ENFORCEMENT ACROSS 5 REPOS                                    │
├────────────────────────┬─────────────────────────────┬─────────────────────────────────────────────────┤
│ Repository             │ Enforced Invariant          │ Concrete Code Symbol Anchor                     │
├────────────────────────┼─────────────────────────────┼─────────────────────────────────────────────────┤
│ OpsBridge              │ Zero Overselling            │ FlashSale.Application/Orders/SubmitOrderUseCase │
│                        │ Dual-Write Elimination      │ FlashSale.Infrastructure/OutboxEventWriter      │
│                        │ Idempotent Ingestion        │ OpsBridge.Infrastructure/EfInboxRepository      │
│                        │ Optimistic Concurrency      │ FlashSale.Domain/Saga/CheckoutSagaState.Version │
├────────────────────────┼─────────────────────────────┼─────────────────────────────────────────────────┤
│ retail-finance-core    │ Ledger Debit/Credit Parity  │ com.mtaretail.domain.Transaction                │
│                        │ Optimistic Account Locking  │ com.mtaretail.domain.Account (@Version)         │
├────────────────────────┼─────────────────────────────┼─────────────────────────────────────────────────┤
│ QuanLyKho              │ Maker-Checker Separation    │ backend/app/api/routes/inventory.py             │
│                        │ Branch Scope Isolation (B2) │ backend/app/core/security._require_location     │
│                        │ FEFO Lot Consumption        │ backend/app/api/routes/inventory.py (FEFO sort) │
├────────────────────────┼─────────────────────────────┼─────────────────────────────────────────────────┤
│ AKS-SRE-Platform       │ Zero Ingress Traffic Drop   │ kubernetes/base/api/deployment.yaml (PDB)       │
│                        │ Zero-Trust Network Boundary │ kubernetes/base/network-policy.yaml             │
├────────────────────────┼─────────────────────────────┼─────────────────────────────────────────────────┤
│ SmartDocumentChatbot   │ Strict Tenant Data Filter   │ backend/app/services/vector_store.py            │
│                        │ Agent Circuit Breaker       │ backend/src/main/java/.../AgentClient.java      │
└────────────────────────┴─────────────────────────────┴─────────────────────────────────────────────────┘
```

---

## 3. Repository Directory Structure & Clean Architecture Verification

### 3.1 OpsBridge (Clean Architecture Dependency Rule)
```text
src/
├── FlashSale.Domain/         # BCL only; ZERO external package references
├── FlashSale.Application/    # Use cases & ports; depends only on Domain
├── FlashSale.Infrastructure/ # EF Core, Redis, RabbitMQ adapters; implements Application ports
├── Order.Api/                # Thin presentation & composition root
├── Order.Worker/             # Background consumer host composition root
└── tests/                    # ArchitectureTests, UnitTests, ContractTests, IntegrationTests
```

### 3.2 retail-finance-core-platform (Java Spring Boot)
```text
backend/
├── src/main/java/com/mtaretail/
│   ├── domain/               # Core accounting entities (Account, Transaction, Entry)
│   ├── repository/           # Spring Data JPA repositories with optimistic locking
│   ├── service/              # Transactional ledger posting & validation services
│   └── web/                  # REST controllers
└── reconciliation-service/   # Python async reconciliation worker with outbox polling
```

### 3.3 QuanLyKho (Modular Python Monolith)
```text
backend/
├── app/
│   ├── api/routes/           # Modular FastAPI routes (inventory, reconciliations, iam, ai)
│   ├── models/               # SQLAlchemy 2.0 mapped models (inv_items, inv_balances, outbox)
│   ├── schemas/              # Pydantic v2 validation models
│   ├── core/                 # Security guards, location scoping, exceptions
│   └── db/                   # Engine, sessionmaker, base
└── alembic/versions/         # Idempotent DB migrations (001 to 027)
```

### 3.4 AKS-SRE-Platform (GitOps & Infrastructure)
```text
├── terraform/                # aks-foundation, acr_attachment, aks_cluster modules
├── kubernetes/               # base & overlays (order-api, payment, worker, monitoring)
├── platform/argocd/          # ArgoCD GitOps application manifests
└── docs/runbooks/            # 5 Standard SRE production runbooks with SLA targets
```

### 3.5 SmartDocumentChatbot (RAG & Agent Platform)
```text
├── frontend/                 # Next.js 14 App Router, Tailwind CSS, TypeScript
├── backend/                  # FastAPI LangChain/LlamaIndex RAG pipeline
└── docs/adr/                 # ADRs 0001–0010 (Security boundaries, queue durability, fallback)
```
