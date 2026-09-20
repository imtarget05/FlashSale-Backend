# Backlog

- [x] Write k6 load test scripts (`load-tests/k6/flash-sale.js`). *(cùng concurrency harness Python — đã chạy lấy số liệu thật)*
- [x] Implement optimistic concurrency / pessimistic locking in DB. *(atomic conditional UPDATE — ADR-002)*
- [x] Set up Redis instance and reservation logic. *(Lua CAS fast-fail — ADR-003)*
- [x] Scaffold Order.Worker background service. *(queue abstraction + idempotent consumer — ADR-004)*
- [x] Write Terraform modules. *(root module + dev/prod tfvars cho full Azure stack)*

## Việc tiếp theo (khi đã có Azure subscription)
- [ ] Wire OpenTelemetry SDK → Azure Monitor (Phase 10).
- [ ] Key Vault CSI driver thay secret Kubernetes.
- [ ] Chaos experiment: kill worker giữa lúc drain để chứng minh at-least-once.
- [ ] Sử dụng ArgoCD Rollouts (canary) thay vì immutable revision.
