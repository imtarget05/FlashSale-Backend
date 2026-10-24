# Explain the queue-rejection compensation decision

**Business situation:** Fifty buyers can contest ten units. If the queue is
full after a Redis reservation, returning 503 is only safe when the reservation
is released and the downstream event stream records that release once.

**Failure found:** The old repair path always returned `false` from its
supposed durable marker check. Repeating a repair created a fresh random
`inventory.released` event each time. A downstream inventory projection could
count that as repeated stock return.

**Decision:** Use one deterministic event ID per reservation attempt, derived
from the HTTP key and its persisted correlation ID. A 503 permits another
reservation with the same HTTP key; that attempt needs its own balancing
release. PostgreSQL's unique outbox index arbitrates concurrent repairs of one
attempt. Check historical releases by aggregate key and correlation ID to cover
rows written before the ID change.
The outbox dispatcher may publish at least once; its consumers still need
event-ID idempotency.

**Evidence to show:** the RED regressions, seven focused unit tests, and six
Testcontainers drills (H through M) in
`docs/evidence/2026-10-23-queue-rejection-compensation.md`. The tests inspect
actual PostgreSQL rows and RabbitMQ deliveries. Do not present this as a
production incident or customer SLA.

**Trade-off:** The compatibility read adds one PostgreSQL query on the
queue-rejection path. A release event already dead-lettered is considered
durable, so operations must requeue that row rather than create another one.
The query has a composite index and uses a fresh DbContext to inspect only
committed rows after a failed write. A true storage outage can prevent the
failure marker as well; the durable reserve row remains the recovery anchor.
The deployment must drain old random-ID writers before new instances accept
requests, because overlapping old and new versions have no shared unique
identity for this release.
