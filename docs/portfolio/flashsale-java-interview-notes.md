# FlashSale Java payment service: interview notes

These answers describe only the Java payment service in this repository. They
are implementation evidence, not claims of banking production experience or
integration with a real payment provider.

## 1. How does the service keep a payment and its event consistent?

`PaymentService.processPayment` writes the payment row and its outbox row in
one PostgreSQL transaction. A scheduled relay claims due rows with
`FOR UPDATE SKIP LOCKED`, publishes to RabbitMQ, and marks a row published only
after a positive correlated publisher confirm and successful mandatory
routing. A crash after broker confirmation but before the database commit can
publish the event again, so the delivery guarantee is at-least-once rather
than exactly-once. Consumers must be idempotent.

Evidence: [PaymentService](../../src/payment-service-java/src/main/java/com/flashsale/payment/service/PaymentService.java),
[PaymentOutboxRepository](../../src/payment-service-java/src/main/java/com/flashsale/payment/outbox/PaymentOutboxRepository.java),
[PaymentEventPublisher](../../src/payment-service-java/src/main/java/com/flashsale/payment/event/PaymentEventPublisher.java),
[ADR-027](../adr/027-rabbitmq-only-java-payment-runtime.md).

## 2. What happens when two requests race on the same idempotency key?

The database uniqueness constraint is the final authority. The service uses
PostgreSQL `INSERT ... ON CONFLICT DO NOTHING`; a request that loses the race
reads the committed winner and compares payment ID, order ID, amount, and
currency. Matching input replays the stored result; different input returns
409. This avoids querying inside a PostgreSQL transaction already aborted by a
unique-key exception.

Evidence: [PaymentServiceIntegrationTest](../../src/payment-service-java/src/test/java/com/flashsale/payment/integration/PaymentServiceIntegrationTest.java)
tests both concurrent same-payload success and different-payload conflict.

## 3. What is Redis responsible for, and what is it not responsible for?

Payment lookup uses Spring Cache by idempotency key, and processing evicts the
corresponding cached value. PostgreSQL remains the source of truth. This
payment service does not use Redis as a distributed lock, inventory
reservation system, or proof of oversell prevention.

Evidence: [PaymentService](../../src/payment-service-java/src/main/java/com/flashsale/payment/service/PaymentService.java)
and [CacheConfig](../../src/payment-service-java/src/main/java/com/flashsale/payment/config/CacheConfig.java).

## 4. How are RabbitMQ failures and duplicate deliveries handled?

The order consumer uses manual acknowledgements. It acknowledges after
payment/outbox processing succeeds; malformed or failed processing is nacked
without requeue and routed to the configured dead-letter path. The outbox
publisher waits for broker confirmation and checks mandatory returns; failed
publishes remain retryable. Duplicate order deliveries are handled through the
same database-backed idempotency key.

Evidence: [OrderEventConsumer](../../src/payment-service-java/src/main/java/com/flashsale/payment/event/OrderEventConsumer.java),
[PaymentOutboxRelay](../../src/payment-service-java/src/main/java/com/flashsale/payment/outbox/PaymentOutboxRelay.java),
and [PaymentServiceIntegrationTest](../../src/payment-service-java/src/test/java/com/flashsale/payment/integration/PaymentServiceIntegrationTest.java).

## 5. What does the authentication evidence establish?

REST endpoints are stateless and role-protected: payment creation and audit
access require Staff or Admin; payment lookup permits Customer, Staff, or
Admin. In-memory demo identities exist only in the `demo` profile, require a
configured password of at least 16 characters, and are disabled when
`production` or `prod` is active. The optional gRPC listener is off by default;
enabling it requires readable TLS files and JWT authentication for non-health
RPCs. This is not evidence of bank SSO, production identity management, or a
real payment-provider integration.

Evidence: [SecurityConfig](../../src/payment-service-java/src/main/java/com/flashsale/payment/security/SecurityConfig.java),
[DemoUserDetailsConfig](../../src/payment-service-java/src/main/java/com/flashsale/payment/security/DemoUserDetailsConfig.java),
[PaymentGrpcServerConfig](../../src/payment-service-java/src/main/java/com/flashsale/payment/grpc/PaymentGrpcServerConfig.java),
and [the repository-only Sacombank evidence map](./sacombank-jd-evidence.md).

## 6. How would this flow differ on Kafka or ActiveMQ?

This service runs on RabbitMQ; the following is **interview preparation**, not
a claim that Kafka or ActiveMQ was implemented here.

| Broker | Consumer progress and failure handling | What the payment service would still need |
| --- | --- | --- |
| RabbitMQ (current) | Manual acknowledgements after processing; publisher confirms, mandatory routing and a dead-letter path are configured. | Keep the database outbox and idempotent consumer because a publish/ack crash window still permits duplicates. |
| Kafka | A consumer group tracks offsets per partition; committing an offset is separate from committing the payment database transaction. Replaying from an earlier offset is possible. | Choose an event key for order-sensitive partitioning, commit offsets after the database work, and keep deduplication. Define how poison records are retried or sent to a separate topic. |
| ActiveMQ Classic/JMS | A transacted session rollback or client recovery can redeliver; broker redelivery policy and DLQ handling are configured separately. | Keep the same idempotency invariant, test redelivery and poison messages, and verify when the JMS acknowledgement/transaction occurs relative to the database commit. |

Why RabbitMQ here: the repo already has a tested queue/routing contract and a
publisher-confirmed outbox relay. Introducing another broker for the same
order event would add operational paths without a demonstrated requirement.

Practice question: *If the consumer commits its Kafka offset before the
payment transaction commits, what can happen after a crash?* The record can be
skipped on restart while the payment was never committed. Commit the database
work first, then the offset; accept that a crash between those commits causes
redelivery, and make processing idempotent.

References: [Kafka consumer offset tracking](https://kafka.apache.org/40/implementation/distribution/),
[ActiveMQ redelivery and DLQ](https://activemq.apache.org/components/classic/documentation/message-redelivery-and-dlq-handling),
and [RabbitMQ acknowledgements](https://www.rabbitmq.com/docs/reliability).

## 7. What do JWT, RSA and SHA mean in this service?

The Java code signs and verifies JWTs with a configured **HMAC shared key**;
it does not use RSA or an external identity provider. A JWS can instead use an
asymmetric signature such as RSA: the issuer keeps the private signing key and
the service verifies with a public key. That would need explicit issuer,
audience, algorithm, key selection and rotation rules plus negative tests;
changing the algorithm alone would not make this service production-ready.

SHA-256 is a hash function, not encryption. A raw fast SHA-256 digest is not
appropriate password storage; the demo identities use BCrypt. A payment
record should not gain ad-hoc RSA encryption or hashing merely to tick a JD
keyword. Select a cryptographic control only for a defined asset and threat,
then test key handling, failure and rotation.

Practice question: *Can the current JWT be verified by an independent service
without sharing the signing secret?* No. HMAC verification requires the same
secret. An asymmetric JWS design can separate signing and verification keys,
but that design has not been implemented or tested in this repository.

Evidence and references: [JwtUtil](../../src/payment-service-java/src/main/java/com/flashsale/payment/security/JwtUtil.java),
[DemoUserDetailsConfig](../../src/payment-service-java/src/main/java/com/flashsale/payment/security/DemoUserDetailsConfig.java),
[RFC 7515 (JWS)](https://www.rfc-editor.org/rfc/rfc7515.html),
and [OWASP Password Storage guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).
