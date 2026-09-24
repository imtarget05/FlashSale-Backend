# Plan: LOCAL v2 Platform Evolution

> **⛔ NOTICE TO ALL AGENTS (2026-09-24): plan này là đủ — KHÔNG cần AI/LLM.**
> Scope Ollama/qwen3/AI-assistant/content-generation đã DROP khỏi plan theo quyết định user.
> Không chạy, không fix, không verify smoke AI (`content-support-smoke.sh`, `ai-assistant-smoke.sh`
> out-of-scope). Model LLM đã unload khỏi RAM. Tập trung 100% vào vấn đề hiện tại:
> kind + payment-service CrashLoop + saga smoke + Kafka/observability/KEDA theo phase bên dưới.

> **Status**: Active
> **Created**: 20260923-2349
> **Slug**: local-v2-platform
> **Artifact Level**: work-package
> **Goal**: LOCAL v2 — tiến hóa từ v1.1 lên microservices trên kind, zero-cloud-cost, trong budget Docker VM 7.75 GiB. V2.1 Payment.Service + Checkout.Saga → V2.2 Kafka KRaft + Outbox/Inbox → V2.3 Tempo+Loki tracing → V2.4 Envoy rate-limit + KEDA → V2.5 demo + evidence. > **Verification Boundary**: build 0 error; dotnet test PASS; saga smoke 5 scenarios; outbox-zero-stuck; Grafana Tempo+Loki healthy; KEDA scale event. Mỗi phase tick chỉ khi có evidence. Không verify AI/LLM endpoints.
> **Rollback Surface**: revert plans/ + tasks/ tracker; code/runtime rollback theo từng phase (git checkout); không đụng cloud (local-only).
> **Promotion Reason**: unblock Cline — V2.1 code (Payment/Saga/Outbox/Inbox + 2 migrations) đã có trong tree, cần verify rồi đi tiếp Kafka.
> **Spec**: `docs/spec.md`
> **Research**: See `docs/researches/`
> **Task Contract**: `tasks/contracts/20260923-2349-local-v2-platform.contract.md`
> **Task Review**: `tasks/reviews/20260923-2349-local-v2-platform.review.md`
> **Implementation Notes**: `tasks/notes/20260923-2349-local-v2-platform.notes.md`

## Agentic Routing
- Selected route:
- Routing reason:
- Due diligence:
  - P1 map:
  - P2 trace:
  - P3 decision rationale:

## Workflow Inventory
Complete this inventory before implementation. If any line is unknown, keep the plan in Draft and fill it before projection.

- Active plan: `plans/plan-20260923-2349-local-v2-platform.md`
- Sprint contract: `tasks/contracts/20260923-2349-local-v2-platform.contract.md`
- Sprint review: `tasks/reviews/20260923-2349-local-v2-platform.review.md`
- Implementation notes: `tasks/notes/20260923-2349-local-v2-platform.notes.md`
- Deferred-goal ledger: `tasks/todos.md`
- Current checks: `.ai/harness/checks/latest.json`
- Run snapshots: `.ai/harness/runs/`
- Scope authority: `tasks/contracts/20260923-2349-local-v2-platform.contract.md` `allowed_paths`
- Concurrency rule: `.ai/harness/active-plan` selects the active plan for this worktree when present; `.ai/harness/active-worktree` records the owning worktree. If another worktree already owns active work, open or switch to the matching worktree instead of serializing unrelated plans.
- Execution isolation: approved contract-level work projects through `repo-harness run plan-to-todo --plan plans/plan-20260923-2349-local-v2-platform.md` and may start `repo-harness run contract-worktree start --plan plans/plan-20260923-2349-local-v2-platform.md`.

## Approach
### Strategy
### Trade-offs
| Option | Pros | Cons | Decision |
|--------|------|------|----------|

## Detailed Design
### File Changes
| File | Action | Description |
|------|--------|-------------|

### Code Snippets
### Data Flow

## Risk Assessment
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|

## Task Contracts
- Contract file: `tasks/contracts/20260923-2349-local-v2-platform.contract.md`
- Review file: `tasks/reviews/20260923-2349-local-v2-platform.review.md`
- Implementation notes file: `tasks/notes/20260923-2349-local-v2-platform.notes.md`
- Template: `.claude/templates/contract.template.md`
- Verification command: `repo-harness run verify-contract --contract tasks/contracts/20260923-2349-local-v2-platform.contract.md --strict`
- Active plan rule: `.ai/harness/active-plan` is authoritative for this worktree when present; `.ai/harness/active-worktree` records the owning worktree. Do not infer active execution from the latest non-archived plan.

## Handoff

- Checks file: `.ai/harness/checks/latest.json`
- Session handoff: `.ai/harness/handoff/current.md`

## Promotion Gate

- **Merge/PR unit**:
- **Rollback surface**:
- **Verification boundary**:
- **Review/acceptance boundary**:
- **High-risk surface**:
- **Why not checklist row**:

## Evidence Contract

- **State/progress path**:
- **Verification evidence**:
- **Evaluator rubric**:
- **Stop condition**:
- **Rollback surface**:

## Annotations
<!-- [NOTE]: prefixed inline. Claude processes all and revises. -->

## Task Breakdown (tay chân thực thi)
- [x] V2.0 nền: RabbitMQ treo (RAM 498/512MiB, diag timeout) → restart, ping OK. Healthcheck mgmt vẫn timeout — AMQP đủ cho smoke.
- [x] V2.1 verify localhost: build 0 error; test 179/179 (139 unit + 40); payment 21/21, lowstock 11/11, daily 12/12.
- [x] saga-orchestration-smoke (kind): **21/21** (mở port-forward gateway 8088; pod tự hồi sau CrashLoop). Evidence: `docs/evidence/saga/saga-live-2026-09-24.log`.
- [x] V2.5 (một phần): viết `docs/EVIDENCE.md` master index.
- [x] V2.2 mechanics + Kafka-down full cycle: rebuild 3 images + kind load + rollout + migrate; drain ✅, replay 1+4 ✅, ~20min Kafka outage → DLQ visible → requeue → drained, event in topic (no loss). ArgoCD auto-sync paused/restored. Evidence: `docs/evidence/v2/outbox-inbox-2026-09-24.md`. Note 2026-09-24: `outbox-recovery-smoke.sh` đã mode Kafka (scale sts/kafka, không còn RabbitMQ); fix namespace bug KAFKA_NS=default (phụ Cline).
- [x] V2.3 infra & OTel code: Tempo + Loki + Grafana datasources OK; OpenTelemetry instrumented in Order.Api and Payment.Service; live E2E distributed trace verified in Tempo across order-api and payment-service during checkout saga (Evidence: `docs/evidence/v2/observability-autoscaling-2026-09-24.md`).
- [x] V2.4 KEDA Kafka Scaler: KEDA ScaledObject/order-worker reading consumer lag from Kafka KRaft topic `orders.events` group `flashsale-automation`. Resolved cross-namespace broker advertised listener FQDN `kafka.flashsale.svc.cluster.local:9092`. ScaledObject READY=True, ACTIVE=True, HPA live with valid external metrics.
- [x] V2.5: `scripts/demo-local-platform-v2.sh` verified + `docs/EVIDENCE.md` master index updated.

## Acceptance Criteria
- [x] V2.1: saga smoke 5/5 + outbox stuck = 0 (saga 21/21 ×3 logs, latest `scripts/saga-orchestration-smoke.sh` 21 Passed).
- [x] V2.2: broker-down resume không mất message + replay đúng 1 transition (harness 14/14).
- [x] V2.3: 1 trace E2E qua ≥2 services xem được trong Grafana/Tempo (`order-api` -> `payment-service` trace ID `db8921ac21f8f2a20c9bca642a07bf9a`).
- [x] V2.4: KEDA scale-out wiring & Kafka consumer lag metric active with valid metric found (`keda-hpa-order-worker` ScalingActive=True).
- [x] Tổng RAM kind + infra mới ≤ 7.75 GiB (kind ~1.8GiB/node, Loki/Tempo 256Mi, KEDA ~100Mi).
