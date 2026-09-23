# V2.2 Outbox/Inbox live verification (2026-09-24, kind `flashsale` ns)

Gateway: `kubectl port-forward -n envoy-gateway-system svc/envoy-... 8088:80` + `Host: flashsale.local`.
Images rebuilt from tree + `kind load` + rollout restart (order-api/worker/payment all 0 restarts).

## 1. Normal drain — PASS
- `POST /api/outbox/enqueue` → `{"status":"enqueued",...,"id":1}` (HTTP 200)
- After ~10s: `GET /api/outbox/pending` → `{"count":0,"messages":[]}`
- DB: row id=1 `ProcessedAt` set, `RetryCount`=0.

## 2. Broker-down — MECHANICS PROVEN, RETRY GAP FOUND
- `rabbitmqctl stop_app` on rabbitmq-0 (ArgoCD reverts `scale sts --replicas=0`, so in-container stop is the way).
- Enqueued id=3 while broker down → dispatcher attempted, `MarkFailedAsync`, `RetryCount`→5 within seconds (no backoff), row stuck: `ProcessedAt` NULL, `pending`=0 hides it.
- `rabbitmqctl start_app` → broker back, but stuck rows do NOT resume (filter `RetryCount < 5`).
- **Gap for Cline**: `src/FlashSale.Infrastructure/Persistence/OutboxRepository.cs:21`
  (`RetryCount < 5` with no backoff, no DLQ, no visibility — `pending`=0 lies).
  Suggested: exponential backoff + DLQ table or `ErrorAt`-based requeue + stuck-alert metric.

## 3. Replay dedup — PASS
- `POST /api/inbox/consume` same MessageId (UUID) ×5, consumer `saga-test`:
  1st → `{"status":"processed",...,"processed":true}`,
  2nd–5th → `{"status":"deduplicated",...,"processed":false}`.
- DB: exactly 1 row in `InboxMessages` for that MessageId.
- Note: `MessageId` column is UUID — non-UUID input returns 400/empty, not deduped.

## 4. Kafka status
- Broker `kafka:0` (apache/kafka:3.8.0) Running; topic `orders.events` exists
  (`/opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list`).
- App on kind still wires `Messaging__Provider=RabbitMQ`; `KafkaDomainEventPublisher`
  (Confluent.Kafka, idempotent) exists in code but is NOT the live path.
- Cutover = env change + rollout restart (reversible), but it moves the live order
  path — recommend Cline does it with a saga-smoke regression right after.
