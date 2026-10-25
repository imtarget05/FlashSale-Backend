# GitOps Release Contract & Independent Deployment

## Overview
FlashSale-Backend is an independently releasable and deployable system. It owns its:
1. Application domain & use-cases (`FlashSale.Domain`, `FlashSale.Application`)
2. Infrastructure adapters (`FlashSale.Infrastructure`, PostgreSQL, Redis, RabbitMQ, Kafka)
3. Container build definitions (multi-stage non-root Dockerfiles)
4. Kubernetes manifests (`infrastructure/kubernetes/base/` and overlays `local/`, `azure-cloud/`, `prod/`)
5. Argo CD release manifest (`infrastructure/argocd/flashsale-application.yaml`)

## Clean Separation from Platform & Other Workloads
- **Platform Agnostic**: The application renders cleanly with Kustomize (`kubectl kustomize infrastructure/kubernetes/overlays/azure-cloud`). It can be hosted on AKS, local Kind, EKS, or any standard Kubernetes cluster.
- **Data Boundary**: FlashSale owns `FlashSaleDb` PostgreSQL database, Redis stock cache, and RabbitMQ/Kafka messaging within the `flashsale` namespace. No external services (such as QuanLyKho or retail-finance) share this database instance.
- **Contract Boundary**: Cross-system events (such as goods receipts or ledger postings) are exchanged via asynchronous signed webhooks and outbox events, never through shared database schemas or cross-namespace direct connections.
- **GitOps Promotion**: Release manifests in `infrastructure/kubernetes/overlays/azure-cloud/` pin versioned container images. Argo CD reconciles the state directly from the `FlashSale-Backend` repository without requiring coordinated changes to the platform repository.
