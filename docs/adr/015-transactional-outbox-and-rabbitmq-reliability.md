# ADR 015: Transactional Outbox & Reliable Webhook Processing with RabbitMQ

## Status
Accepted

## Context
OpsBridge ingests asynchronous, high-frequency integration events (such as `GoodsReceiptConfirmed v1`) from external systems (FactoryFlow, CSV, Webhooks). 
If the API directly published messages to RabbitMQ upon receiving an HTTP webhook, network partitions, RabbitMQ restart, or API process crashes could cause either:
1. Message lost if PostgreSQL transaction fails after message is published to the broker (phantom event).
2. Event lost if PostgreSQL transaction succeeds but RabbitMQ publish fails (silent drop).
3. Duplication and race conditions if webhooks are retried concurrently by callers.

## Decision
1. **Atomic Dual-Write Prevention (Transactional Outbox)**:
   - When an event arrives via signed webhook, the API inserts an `InboxMessage`, `IngestedEvent`, and `OutboxMessage` in a **single atomic PostgreSQL transaction** using `IUnitOfWork`.
   - The HTTP response `202 Accepted` is returned strictly after the database commit.
   - If RabbitMQ is down, webhooks succeed without data loss; outbox messages remain pending in PostgreSQL.
2. **Asynchronous Outbox Publisher with Publisher Confirms**:
   - `OutboxPublisherHostedService` runs in the background, polling pending outbox messages in batches.
   - Published via RabbitMQ 7.x with `PublisherConfirms` enabled (`publisherConfirmationsEnabled: true`). The message status is updated to `Dispatched` only upon receiving broker confirmation.
3. **At-Least-Once Delivery & Idempotent Consumer**:
   - Consumers use manual acknowledgements (`autoAck: false`).
   - Consumer verifies `InboxMessage` existence before processing. Re-delivered messages are acknowledged immediately without side-effects.
   - Failed messages are retried with exponential backoff and jitter up to 3 times before being routed to Dead-Letter Queue (DLQ) and persisted to `DeadLetterEvents`.
4. **Authorized Manual Replay**:
   - DLQ events can be replayed via `POST /api/v1/events/replay/{id}` by users with `Operator` or `Admin` role.
   - Audit trail records who replayed the event, when, and the resolution status.

## Alternatives Considered
1. **Direct Publish from HTTP Handler**: Rejected because it causes dual-write inconsistencies when broker or DB fails.
2. **Distributed Transactions (2PC / XA)**: Rejected due to high latency, poor scalability, and lack of support across modern cloud brokers.
3. **Change Data Capture (Debezium)**: Considered for high volume, but adds operational overhead (Kafka Connect) for initial stages. Polling outbox table with batching and indexing is simple, testable, and robust.

## Consequences & Trade-offs
- **Pros**:
  - Zero message loss even during RabbitMQ downtime or API crash.
  - Safe concurrent replay protection.
  - Full observability and dead-letter auditability.
- **Trade-offs**:
  - Slight polling delay (default 1000ms, configurable) between HTTP ingestion and consumer execution.
  - Requires periodic cleanup / archiving of dispatched outbox records.
