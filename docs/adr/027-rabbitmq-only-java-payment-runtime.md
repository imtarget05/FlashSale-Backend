# ADR-027: RabbitMQ-only Java payment runtime

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
- The consumer acknowledges a delivery only after payment processing and the
  corresponding publish call return successfully. Processing or publish
  exceptions result in a negative acknowledgement.
- The publish call is not a transaction with PostgreSQL and does not prove
  broker acceptance. Publisher confirms and a transactional outbox remain
  follow-up work under F5; no DB/event atomicity claim is made.

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

Result: 14 tests passed, with 0 failures, errors, or skips. Testcontainers
verified valid order → persisted payment and completion event; malformed
message → DLQ; duplicate delivery → one payment and one completion event. A
unit test verified that a publish exception nacks rather than acknowledges
the delivery. Hosted CI and cloud deployment remain unverified.
