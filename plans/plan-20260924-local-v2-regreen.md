P01→P03 Completion Implementation Plan
For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.
Goal: Đưa P01 LOCAL v2 tới final gate PASS rồi gỡ kẹt P03 platform để P02 deploy 7D xong.
Architecture: Làm theo thứ tự phụ thuộc: P01 regreen local trước (tree sạch + trap an toàn + topic idempotent + 4 smoke xanh), sau đó P03 platform (local v2 + quota Azure thủ công), cuối cùng P02 GitOps 7D. Mỗi bước có evidence log.
Tech Stack: kind, kubectl, ArgoCD, Kafka KRaft emptyDir, Postgres, Redis, .NET 10, Node Express, Kustomize, Tempo+Loki+Prometheus, KEDA.
Spec: FlashSale-Backend/plans/plan-20260923-2349-local-v2-platform.md, 3 file tasks/current.md (P01 V2 DONE-chưa-gate, P02 6B PASS-blocked-7D, P03 v1.1 PASS-v2-NOT-started)
Global Constraints
- local-only trước, Docker VM <=7.75 GiB
- Không AI/LLM, không chạy ai-assistant/content-support-smoke
- Không terraform apply khi chưa có approval + quota 16/16 live
- Không bao giờ ghi automated:null vào Argo Application
- Mỗi PASS phải có log docs/evidence/ tương ứng
File Structure
- P01: scripts/outbox-recovery-smoke.sh:80-101 (trap), infrastructure/kubernetes/overlays/local/*kafka* (Job topic), scripts/saga-orchestration-smoke.sh, scripts/demo-local-platform-v2.sh
- P03: local/kind/cluster.yaml, platform/argocd/, platform/observability/, docs/evidence/local-platform/, docs/evidence/phase7b/quota-7b0-gate.md
- P02: infrastructure/kubernetes/overlays/prod/, infrastructure/kubernetes/base/hpa.yaml, scripts/validate-manifests.sh
- Quyết định ghi tasks/notes/<plan-stem>.notes.md, không nhồi chat
CHUNK 1 — P01 Safety (duyệt trước, 4 task)
Task 1: Tree P01 sạch
Files: Modify: FlashSale-Backend/ worktree (30 files D/M AI-deletion)
- Step 1: Chạy git status --short | wc -l Expected: 30 đúng như đã đo
- Step 2: Theo lựa chọn bạn đã hỏi (A commit / B worktree / C để nguyên) — thực hiện đúng 1 lệnh, ví dụ A:
git add -A && git commit -m "chore: drop AI scope triệt để (scripts+src+docs+ollama)"
- Step 3: Verify git status --short | wc -l Expected: 0 (hoặc worktree mới hiện trong git worktree list)
Task 2: Trap không ghi automated:null
Files: Modify: FlashSale-Backend/scripts/outbox-recovery-smoke.sh:80-92
Interfaces: Consumes: ORIGINAL_AUTOMATED json; Produces: restore_runtime() an toàn
- Step 1: Viết test chứng minh lỗi
ORIGINAL_AUTOMATED="None"; case "$ORIGINAL_AUTOMATED" in \{*) echo "patch";; *) echo "skip-correct";; esac
Run: bash -c "<trên>" Expected: skip-correct (code cũ sẽ patch null = BUG)
- Step 2: Fix minimal
restore_runtime() {
  kubectl scale statefulset/kafka -n "$KAFKA_NS" --replicas=1 >/dev/null 2>&1 || true
  case "$ORIGINAL_AUTOMATED" in \{*) kubectl patch application flashsale -n argocd --type merge -p "{\"spec\":{\"syncPolicy\":{\"automated\":$ORIGINAL_AUTOMATED}}}" >/dev/null 2>&1 || true;; *) echo "restore: skip autosync (was None)";; esac
}
- Step 3: Run bash -n scripts/outbox-recovery-smoke.sh Expected: syntax OK
Task 3: Job/order-migrate=None không phải sync-fail
Files: Read-only: kubectl, argocd
- Step 1: Run kubectl -n flashsale get job order-migrate -o jsonpath='{.status.conditions[*].type}' Expected: chứa Success/Complete, không Failed
- Step 2: Run kubectl get application flashsale -n argocd -o jsonpath='{.status.sync.status}/{.status.health.status}' Expected: Synced/Healthy — không ép sync mù
Task 4: Kafka topic Job idempotent
Files: Modify: FlashSale-Backend/infrastructure/kubernetes/overlays/local/*kafka*job*.yaml
- Step 1: Run kubectl -n default exec kafka-0 -- /opt/kafka/bin/kafka-topics.sh --list --bootstrap-server localhost:9092 Expected: thấy orders.events hoặc xác nhận mất sau recreate
- Step 2: Job guard
/opt/kafka/bin/kafka-topics.sh --describe --topic orders.events --bootstrap-server kafka:9092 || /opt/kafka/bin/kafka-topics.sh --create --topic orders.events --partitions 3 --replication-factor 1 --bootstrap-server kafka:9092
- Step 3: Run kubectl kustomize infrastructure/kubernetes/overlays/local | kubectl apply --dry-run=client -f - Expected: render OK
CHUNK 2-4 outline (chi tiết sau khi duyệt Chunk 1)
- Chunk 2 P01 regreen: Saga 21/21 → Outbox với trap mới → demo script → Tempo trace order-api→payment-service + KEDA lag ACTIVE. Mỗi task có log docs/evidence/v2/.
- Chunk 3 P03 platform: kind footprint <=7.75 GiB check → L10 observability còn thiếu (Alertmanager/app /metrics) ghi [KNOWN-LIMIT] → quota 7B bạn làm portal thủ công, tôi chỉ verify az quota show → không apply khi chưa 16/16.
- Chunk 4 P02 7D: validate-manifests.sh 9/9 → HPA + readOnlyFS giữ → Gateway HTTPRoute prod wire shared Gateway → GitOps deploy + rollback git revert proof.
Self-review: đủ 3 spec, không placeholder (code Task 2/4 chạy được), type/khớp tên giữ nguyên (KAFKA_NS, orders.events).
File plan sẽ lưu FlashSale-Backend/plans/plan-20260924-local-v2-regreen.md sau duyệt. Execution: 1. Subagent-Driven (khuyến nghị) 2. Inline. 
