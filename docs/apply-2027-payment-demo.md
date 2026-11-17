# Backend Java application demo: payment consistency

Main evidence: [`87b79a6a652176e64a1c1d5d5664875778e8db68`](https://github.com/imtarget05/FlashSale-Backend/tree/87b79a6a652176e64a1c1d5d5664875778e8db68), [Java CI run 44054310812](https://github.com/imtarget05/FlashSale-Backend/actions/runs/44054310812). That run passed unit and Testcontainers integration jobs, built the image, scanned it, and ran Sonar. The successful deploy workflow skipped deployment jobs, so this is **database-backed test evidence**, not a live deployment claim.

## Reproduce the five-minute story

With JDK 17, Maven, and Docker running, from the repository root:

```bash
cd src/payment-service-java
mvn -B test -Dspring.flyway.enabled=true
```

Inspect `target/surefire-reports/` for the test cases below. The root README also gives a Compose-based HTTP walkthrough, but the automated tests are the cited evidence.

| Scenario | Test and assertion |
| --- | --- |
| Duplicate request | `PaymentServiceIntegrationTest.duplicateOrderEvent_persistsAndPublishesOnlyOnce` checks one payment and one completion event for a duplicate order event. |
| Concurrent same key | `concurrentSamePayload_requestsCreateOnePaymentAndBothSucceed`: two concurrent HTTP requests return 200; PostgreSQL has one payment and one outbox row. |
| Concurrent changed payload | `concurrentDifferentPayload_requestsReturnConflictAndCreateOnePayment`: one HTTP 200, one 409, one payment row. |
| Broker outage between commit and publish | `RabbitMqOutageIntegrationTest.pendingOutboxEventRetriesAndPublishesAfterRabbitMqRecovers`: outbox stays pending with an error during RabbitMQ outage, then publishes after recovery. |
| Audit projection | `paymentOutboxEvent_isProjectedToMongoAndReadableByStaff`: the event is projected to MongoDB and the staff audit API returns it. |

## Explain the tradeoffs

`PaymentService` writes the payment and outbox record in one PostgreSQL transaction. PostgreSQL's unique idempotency key decides the winner when requests race; an identical retry replays the result and a changed payload returns a conflict. The relay reads committed outbox rows with `FOR UPDATE SKIP LOCKED` and publishes with confirms, so broker downtime delays delivery without losing the persisted intent. Delivery can be retried; consumers still need deduplication. The audit projection is eventually consistent and does not decide whether the payment committed.

The payment provider in this demo is deterministic and simulated; no real money or bank rail is involved. Redis failure falls back to PostgreSQL for lookups, but the rate limiter is process-local. No measured multi-replica throughput or active AKS deployment is claimed. For ledger-to-partner reconciliation, use the separate [retail-finance-core-platform walkthrough](https://github.com/imtarget05/retail-finance-core-platform/blob/main/docs/apply-2027-backend-demo.md); these are two different bounded contexts.
