# Interview Notes: OpsBridge Event Ingestion, Transactional Outbox & Reliability (Phase G1–G2)

These notes summarize the architectural decisions, trade-offs, and failure handling strategies implemented in OpsBridge for engineering interviews.

---

### 1. The Core Distributed Problem: Dual-Write & Non-Atomic Operations
**Question**: *"Why didn't you publish the event directly to RabbitMQ in the Webhook API endpoint before returning 202 Accepted?"*

**Answer**:
- Publishing directly to RabbitMQ while writing to PostgreSQL is a classic **dual-write problem**.
- If we publish to RabbitMQ first and the database transaction fails (e.g. database disk full, constraint violation), consumers will process a phantom event that doesn't exist in our source of truth.
- If we commit to PostgreSQL first and publish to RabbitMQ second, an API process crash or network partition before publishing causes silent data loss. The event stays in the database but is never distributed or reconciled.
- **Solution**: We implemented the **Transactional Outbox Pattern**. In one single PostgreSQL ACID transaction (`IUnitOfWork`), we insert:
  1. `InboxMessage` (records the `EventId`, `Source`, and SHA256 `PayloadHash` with a database unique index).
  2. `IngestedEvent` (the raw payload and metadata).
  3. `OutboxMessage` (pending event to be dispatched).
- Once PostgreSQL commits, the webhook caller receives `202 Accepted`. Even if RabbitMQ is completely offline, zero data is lost.

---

### 2. Publisher Confirms vs. Fire-and-Forget
**Question**: *"How do you guarantee RabbitMQ actually received the message from your Outbox worker?"*

**Answer**:
- Default AMQP publishing is asynchronous fire-and-forget; the network socket might accept bytes, but the broker might drop the message if disk is full or queues are unroutable.
- In RabbitMQ.Client 7.x, we explicitly enable **Publisher Confirms** (`CreateChannelOptions(publisherConfirmationsEnabled: true)`).
- When `OutboxPublisherHostedService` sends the batch, it waits for the broker's confirm ACK before updating `OutboxMessage.Status = Dispatched`.
- If the publisher crashes before recording the confirmation, the message remains pending and is re-published upon restart. The consumer handles duplicate delivery idempotently.

---

### 3. Consumer Idempotency & Manual ACKs
**Question**: *"What happens if your background worker crashes after persisting business effects but before acknowledging the message to RabbitMQ?"*

**Answer**:
- We set `autoAck: false` (manual acknowledgements).
- If the worker process crashes before calling `BasicAckAsync`, RabbitMQ notices the channel disconnection and redelivers the unacknowledged message to another worker instance (**at-least-once delivery**).
- To prevent duplicate business execution, the worker checks `InboxMessage` before processing:
  - If already marked as processed, the worker skips business logic and immediately calls `BasicAckAsync` to drain the duplicate from the queue.
  - If a concurrent duplicate arrives, the database unique constraint (`23505`) ensures only one thread succeeds.

---

### 4. Poison Messages & Dead-Letter Queue (DLQ)
**Question**: *"How do you handle malformed or permanently failing messages without blocking the queue?"*

**Answer**:
- When consumer processing throws an unhandled exception, we apply **exponential backoff with jitter** (`2^retry * BaseDelay + Jitter`) up to 3 retries.
- If the retry limit is exceeded:
  1. The event is persisted to the `DeadLetterEvents` table with error stack trace, attempt count, and original payload.
  2. The message is published to the Dead-Letter Exchange (`opsbridge.events.dlq.exchange`) and routed to `opsbridge.events.dlq`.
  3. The consumer rejects the poison message on the main queue (`BasicRejectAsync(requeue: false)`), unblocking the queue for healthy traffic.

---

### 5. Official Webhook Security (Svix Standard)
**Question**: *"How do you verify webhooks securely against replay and tampering?"*

**Answer**:
- We verify signatures adhering to the official **Standard Webhooks / Svix specification**:
  - Decode base64 secret (`whsec_<base64>`).
  - Form canonical payload: `${svix_id}.${svix_timestamp}.${raw_body_bytes}`.
  - Compute HMAC-SHA256 and format as `v1,<base64>`.
  - Compare against candidate signatures using constant-time byte comparison (`CryptographicOperations.FixedTimeEquals`) to prevent timing side-channel attacks.
  - Validate timestamp freshness within $\pm 300$ seconds, while relying on the unique `EventId` and SHA256 `PayloadHash` unique constraint in PostgreSQL to prevent replay within the validity window.

---

### 6. Verifiable Test Evidence (No Fakes)
**Question**: *"How did you verify these distributed failure modes in CI/CD?"*

**Answer**:
- We executed 8 failure drills using real **Testcontainers** (`postgres:15-alpine` and `rabbitmq:3-management-alpine`):
  1. *RabbitMQ unavailable at webhook arrival*: Webhook accepted (202), outbox stays pending.
  2. *API crash after DB commit*: Outbox publisher drains pending rows after restart.
  3. *Publisher crash before confirm*: Re-published event deduplicated by consumer.
  4. *Worker crash before ACK*: Redelivered message deduplicated via consumer inbox.
  5. *Concurrent duplicate webhooks*: Unique constraint catches race, returns 200 Duplicate without duplicate outbox rows.
  6. *Poison message*: Routed to DLQ after 3 retries.
  7. *Duplicate manual replay*: Concurrency guarded, duplicate in-flight replay rejected.
  8. *Unauthorized replay*: Protected by RBAC policy requiring Operator or Admin role.
- All 28 OpsBridge tests passed with real databases and brokers.
