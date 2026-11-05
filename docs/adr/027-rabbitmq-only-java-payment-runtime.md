# ADR-027: RabbitMQ-only Java payment runtime with transactional outbox

## Status

Accepted; locally verified. Hosted CI and cloud runtime are not verified.

## Context

The Java Payment Service had both RabbitMQ and Kafka consumers configured for
the same order-created flow. Running both without a routing contract could
process one order twice. The RabbitMQ path already had a Testcontainers
integration environment, but its JSON converter rejected the application's
event DTO package, so valid messages did not reach the consumer.

## Decision

- RabbitMQ is the only broker in the Java runtime. Kafka dependencies,
  configuration, and consumers are removed; Kafka is not claimed as a
  demonstrated skill for this service.
- The JSON converter trusts only the service's event and DTO packages.
  Incoming order messages are parsed from their raw body; malformed messages
  are negatively acknowledged without requeue and routed to the DLQ.
- PostgreSQL payment and outbox rows are committed in the same transaction.
  An outbox relay publishes with correlated RabbitMQ publisher confirms and
  mandatory routing; only a confirmed, routed message is marked published.
- Failed or timed-out publishes remain pending with capped exponential retry
  delay. A process crash after broker confirmation but before the database
  commit can result in a duplicate publish; consumers must remain idempotent.
- Order deliveries are acknowledged after payment and outbox persistence
  commit. Malformed input or a processing failure is negatively acknowledged
  without requeue and routed to the configured dead-letter queue.
- The optional `mongodb` profile consumes confirmed payment events into an
  idempotent audit projection. MongoDB remains a read model, not the payment
  system of record; the projection excludes transaction identifiers.

## Alternatives

- Keep RabbitMQ and Kafka consumers active: rejected because the same order
  could enter both paths without an explicit routing and deduplication
  contract.
- Keep Kafka in a separate profile: rejected for this demo because it adds
  configuration and maintenance cost without a tested Java Kafka journey.
- Trust every package in the message converter: rejected; the converter uses a
  narrow package allowlist.

## Evidence

Command:

```text
mvn -B -f src/payment-service-java/pom.xml -Dtest=OrderEventConsumerTest,PaymentServiceIntegrationTest test
```

Result: the full Java test suite passed locally with 76 tests and no failures,
errors, or skips. Testcontainers verified valid order → persisted payment and
confirmed completion event; malformed order → DLQ; duplicate order delivery →
one payment and one outbox event; and Mongo audit projection. PostgreSQL
concurrency tests verified same-payload replay and different-payload 409
under concurrent requests. Hosted CI and cloud deployment remain unverified.
