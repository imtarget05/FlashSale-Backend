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
