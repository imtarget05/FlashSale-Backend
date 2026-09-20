# Project Roadmap

This project evolves through an evidence-driven engineering workflow. Technologies are introduced only when justified by observed limitations or explicit learning objectives.

- [x] **Phase 1**: Naive synchronous API + PostgreSQL *(baseline verified — single request works)*
- [x] **Phase 2**: Concurrency experiment (reproduce overselling) *(50/50 accepted, stock 10→9 — evidence in docs/benchmarks/)*
- [x] **Phase 3**: Root-cause analysis and database-level solutions (atomic conditional UPDATE) *(10 accepted / 40 rejected / stock exactly 0)*
- [x] **Phase 4**: Evaluate Redis (justified by Phase 4 measurement: 90% wasted DB hits, p95 574 ms → ADR-003)
- [x] **Phase 5**: Asynchronous processing (queue + worker, idempotency, DLQ → ADR-004; p95 29 ms)
- [ ] **Phase 6**: Containerization (Docker Compose refinement)
- [ ] **Phase 7**: Azure deployment (Container Apps)
- [ ] **Phase 8**: Terraform (Infrastructure as Code)
- [x] **Phase 9**: CI/CD (GitHub Actions) *(ci.yml: build + concurrency harness với services postgres/redis; deploy.yml: build/push API + Worker, deploy Container Apps)*
- [ ] **Phase 10**: Observability (OpenTelemetry, App Insights) *(Log Analytics + Container Insights đã provision; wiring OTel SDK còn lại)*
- [x] **Phase 11**: Failure experiments (Chaos engineering & Postmortems) *(postmortem overselling + drift-DLQ runbook)*
- [x] **Phase 12**: AKS / GitOps (Advanced platform-engineering phase) *(kustomize base/overlays + ArgoCD/KEDA trong 03-AKS-SRE-Platform)*
