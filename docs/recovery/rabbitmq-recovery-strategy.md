# RabbitMQ Recovery Strategy (P01) — Phase 3A design doc

## Current state (honest)
- Queue `orders` is durable; `rabbitmq-data` named volume persists broker
  state across container restart (verified locally).
- RabbitMQ is **NOT the business source of truth**. Messages in the queue are
  transient delivery state: an order that is 202-accepted but not yet
  Postgres-persisted lives only here.
- No definitions export exists yet. No message-store snapshot should ever be
  taken from a running broker (officially warned as inconsistent).

## Phase 3B plan
1. Export broker definitions (users/vhosts/queues/exchanges/bindings/policies)
   via management API → off-host blob storage alongside Postgres backups.
2. Broker rebuild drill: empty broker + definitions import → app functional.
3. Re-evaluate **Transactional Outbox** (Order + OutboxEvent in one Postgres
   tx; publisher drains outbox → RabbitMQ) IF the business decides that
   losing accepted-but-unpersisted orders during total broker loss is
   unacceptable. Until that requirement exists, outbox is deliberately NOT
   implemented.
