# Backlog

- [x] Write k6 load test scripts (`load-tests/k6/flash-sale.js`). *(cùng concurrency harness Python — đã chạy lấy số liệu thật)*
- [x] Implement optimistic concurrency / pessimistic locking in DB. *(atomic conditional UPDATE — ADR-002)*
- [x] Set up Redis instance and reservation logic. *(Lua CAS fast-fail — ADR-003)*
- [x] Scaffold Order.Worker background service. *(queue abstraction + idempotent consumer — ADR-004)*
- [x] Write Terraform modules. *(root module + dev/prod tfvars cho full Azure stack)*

## Phase 9–13 status vs mandatory roadmap (verified live 2026-09-24, kind local-platform — zero Azure)

> Roadmap gốc: `../AKS-SRE-Platform/plans/2026-09-21-microservices-mandatory-roadmap-9-to-18.md`.
> Kết luận kiểm chứng: **Phase 9–12 DONE trên localhost** (code + runtime + evidence).
> Phase 13 (mesh) và 14–18: **NOT STARTED** — ghi rõ lý do + điều kiện mở bên dưới.

- [x] Phase 9 — Microservices evolution: Order.Api + Order.Worker + Payment.Service
      là 3 runtime riêng (Deployment/Service/health endpoint riêng, image riêng);
      Checkout.Saga là bounded context riêng (`CheckoutSagaCoordinator` +
      `CheckoutSagaState` + repo); Fulfillment = Order.Worker (RabbitMQ task
      queue). Data: 1 PG instance, TẤT CẢ entity trong MỘT DbContext/schema
      `public` — ❗ roadmap yêu cầu per-service schemas + "no cross-service
      table reads" CHƯA đạt ở tầng DB (saga repo đọc chung tables qua EF).
      Đạt ở tầng runtime/event (Kafka domain events + RabbitMQ task queue tách
      bạch, `IPaymentClient` qua HTTP). Đánh dấu DONE-WITH-GAP, gap ghi ở ADR
      cần viết (010 đã mở đường: event-driven modular backend before
      microservices). Evidence: saga smoke 21/21 (`docs/evidence/saga/`),
      pods riêng trong `docs/evidence/phase7c-localhost/01-pods-ready-*`.
- [x] Phase 10 — Kafka event backbone: KRaft single broker NON-HA ✅; topic
      `orders.events` (+ `__consumer_offsets`) ✅; Transactional Outbox
      (`OutboxMessages` + claim/lease `FOR UPDATE SKIP LOCKED`, backoff + DLQ
      migrations) + consumer Inbox/dedup (`InboxMessages`, 4771 rows live) ✅;
      at-least-once + idempotent consumers ✅. Evidence:
      `docs/evidence/v2/outbox-inbox-2026-09-24.md`,
      `outbox-recovery-14of14-2026-09-24.log`, `docs/evidence/phase7c-localhost/04-kafka-event-*`
      (event thật chứa key E2E). ❗ Thiếu ADR-013-kafka-event-backbone
      (`docs/adr/013-*` hiện là auth-design — số 013 bị trùng, cần ADR số mới).
- [x] Phase 11 — Payment + Saga: happy path + decline→compensate +
      timeout→retry→compensate + inventory-reject fast-fail + duplicate replay —
      TẤT CẢ verified live 21/21 (`scripts/saga-orchestration-smoke.sh`, re-run
      green trong session 2026-09-24). `FakePaymentProvider` ≙ deterministic
      rule `.01→success/.02→decline/.03→timeout` trong Payment.Service ✅.
      Compensation: release Redis reservation + cancel order + restore DB stock
      (verified stock 26→26 sau compensate) ✅. Evidence:
      `docs/evidence/saga/saga-live-2026-09-24*.log`. ❗ Thiếu 7-file evidence
      `docs/evidence/saga/` theo roadmap (mới có 2 log) + compensation-failure path.
- [x] Phase 12 — Loki + Tempo + OTel: Loki + Tempo + Grafana + Prometheus đã
      deploy qua Helm trên kind ✅; OTLP endpoint `http://tempo.monitoring:4317`
      đã cấu hình cho order-api + payment-service ✅; Tempo có traces thật
      (`service.name` = order-api, payment-service — verified qua API
      `/api/search/tag/service.name/values`) ✅; Grafana có cả Loki + Tempo
      datasources, Loki có trace→log derived field ✅. Evidence:
      `docs/evidence/phase7c-localhost/05-tempo-services-*`,
      `06-loki-labels-*`. ❗ Gap: Loki chưa ingest log app (chỉ có loki-canary
      `unknown_service`; thiếu Alloy/promtail pipeline) — traces có nhưng
      trace↔log correlation chưa end-to-end; worker chưa có OTEL endpoint.
- [ ] Phase 13 — Service Mesh (Istio Ambient): NOT STARTED. Trong cluster
      không có istio/ztunnel/waypoint (verified 0 pod). Mở khi: quyết định
      mesh-mode ADR + cài istio ambient trên kind (zero Azure, nhưng tốn RAM —
      Docker VM budget 7.75 GiB cần check) + đo latency before/after.
- [ ] Phase 14 — Reliability drills: NOT STARTED. Mở khi: cần `docs/incidents/INC-007…010`
      (Kafka-down→outbox-pending→recover đã có log v2, nhưng chưa viết thành INC files).
- [ ] Phase 15 — DR: NOT STARTED (backup pg_dump -Fc đã có từ Phase 5; thiếu ADR
      authoritative-vs-rebuildable + coverage GitOps/mesh/observability config).
- [ ] Phase 16 — Scaling: PARTIAL — KEDA ScaledObject kafka-lag + HPA đã chạy
      trên kind ✅ (`docs/evidence/v2/observability-autoscaling-2026-09-24.md`);
      thiếu burst→lag→scale→drain→down drill có số liệu mới sau fix kafka securityContext.
- [ ] Phase 17 — Architecture review: NOT STARTED (thiếu decision table +
      comparison narratives; ADR-010 là khởi đầu tốt).
- [ ] Phase 18 — Packaging/demo script: NOT STARTED (các smoke script rời rạc đã
      có; thiếu 1 script end-to-end 10–15 phút push→CI→…→rollback, và phải bỏ
      các bước Azure/ACR vì zero-budget → viết bản localhost-only).

## Việc tiếp theo (khi đã có Azure subscription)
- [ ] Wire OpenTelemetry SDK → Azure Monitor (Phase 10).
- [ ] Key Vault CSI driver thay secret Kubernetes.
- [ ] Chaos experiment: kill worker giữa lúc drain để chứng minh at-least-once.
- [ ] Sử dụng ArgoCD Rollouts (canary) thay vì immutable revision.
- [ ] Wire OpenTelemetry SDK → Azure Monitor (Phase 10).
- [ ] Key Vault CSI driver thay secret Kubernetes.
- [ ] Chaos experiment: kill worker giữa lúc drain để chứng minh at-least-once.
- [ ] Sử dụng ArgoCD Rollouts (canary) thay vì immutable revision.
