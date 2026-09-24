# V2.2 Outbox/Inbox live verification (2026-09-24, kind `flashsale` ns)

Gateway: port-forward envoy data-plane svc 8088:80 + `Host: flashsale.local`.
Live publisher on kind: **Kafka** (`Events__Provider=Kafka`, `kafka:9092` — ArgoCD synced
Cline's cutover; the committed `outbox-recovery-smoke.sh` still stops RabbitMQ so it
tests the wrong broker until it gains a Kafka mode).

## 1. Normal drain — PASS
- `POST /api/outbox/enqueue` → `{"status":"enqueued",...,"id":1}` (HTTP 200)
- After ~10s: `GET /api/outbox/pending` → `{"count":0,"messages":[]}`
- DB: row id=1 `ProcessedAt` set, `RetryCount`=0.

## 2. Kafka broker-down (scale sts kafka → 0, ArgoCD auto-sync paused) — PASS, full cycle
- Enqueued id=11 while broker down → `pending` shows count=1 (visible, never hidden).
- Attempts fail slowly (rdkafka metadata/connect timeout dominates: RetryCount 0→2→4 over ~10 min;
  per-attempt ceiling is effectively message.timeout.ms, not app backoff — Cline may cap ProduceAsync with a linked CTS like the AI use case).
- After ~20 min outage: `DeadLetteredAt` set, `GET /api/outbox/stuck` → `count=1, deadLettered=1` with the message.
- Scaled kafka back to 1, `POST /api/outbox/requeue` → `{"revived":1}` → row drained
  (`ProcessedAt` set, `RetryCount`=0, DLQ cleared), `pending`=0, `stuck`=0.
- Kafka-side proof: `kafka-console-consumer --topic orders.events` shows the event
  (`EventType=kafka-down-probe`, `Source=outbox-dispatcher`). No message loss across a ~20 min outage.
- ArgoCD auto-sync restored afterwards (`selfHeal=true`, status Synced).

## 2b. (Superseded) RabbitMQ stop_app observation — old image, fixed since
- Row retried 5× in seconds with no backoff (pre-fix image). Cline's backoff/DLQ commit
  (entity `NextAttemptAt`/`DeadLetteredAt`, `BackoffFor`, stuck surfacing + requeue)
  supersedes it; images rebuilt + redeployed, verified in §2 above on the Kafka path.

## 3. Replay dedup — PASS
- `POST /api/inbox/consume` same MessageId (UUID) ×5, consumer `saga-test`:
  1st → `{"status":"processed",...,"processed":true}`,
  2nd–5th → `{"status":"deduplicated",...,"processed":false}`.
- DB: exactly 1 row in `InboxMessages` for that MessageId.
- Note: `MessageId` column is UUID — non-UUID input is rejected, not deduped.

## 4. Kafka status — CUTOVER DONE (via GitOps)
- Broker `kafka-0` (apache/kafka:3.8.0) Running; topic `orders.events` live (produce + consume verified).
- App on kind wires `Events__Provider=Kafka` → `kafka:9092` (ArgoCD-synced). Saga smoke 21/21 still green on this path (rerun recommended after any env change).
