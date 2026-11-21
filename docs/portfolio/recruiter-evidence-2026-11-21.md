# Recruiter evidence — FlashSale-Backend

Public baseline: [`aa5ac1c827145e2af8ff2117960e5836389e6b9c`](https://github.com/imtarget05/FlashSale-Backend/commit/aa5ac1c827145e2af8ff2117960e5836389e6b9c). The [FlashSale Java CI run on 2026-11-19](https://github.com/imtarget05/FlashSale-Backend/actions/runs/44504582438) passed all five configured jobs. The integration job's Maven summary reports **175 tests run, 0 failures, 0 errors, 0 skipped; BUILD SUCCESS**. This is evidence for the public commit, not for changes in another uncommitted checkout.

Local reproduction on 2026-11-21: clean clone at the same SHA, macOS, Java 17, Docker 29.6.1, `cd src/payment-service-java && mvn -B -q verify` exited 0. The 38 Surefire XML reports total **175 tests, 0 failures, 0 errors, 0 skipped**. Testcontainers used the local Docker daemon. This run did not benchmark production latency.

The evidence branch adds `responseLostAfterCommit_retryReturnsPersistedPaymentWithoutNewOutboxEvent` to model a client that discards a successful server response and retries. Its targeted Testcontainers run passed (1 test, 0 failures/errors/skips). This models response loss after a committed request; it does not inject an actual process crash between DB commit and socket write.

The [local k6 payment-path benchmark](../benchmarks/payment-path-k6-2026-11-21/README.md) now reports throughput, p95/p99, error rate, hardware and raw summaries for three fixed VU levels. The [process-crash drill](../evidence/post-commit-response-loss-2026-11-21/README.md) kills the demo API after commit while withholding the response from the client, then verifies one payment and one outbox row after retry. Both results are local and have explicit boundaries.

A stronger [pre-response JVM crash drill](../evidence/pre-response-jvm-crash-2026-11-21/README.md) now halts the test-classpath JVM at Spring MVC's pre-body-write hook, after the DB transaction commits. It verifies the persisted payment/outbox before restart and unchanged counts after retry. The hook is absent from the production JAR.

| Claim | Where to inspect | Limit |
| --- | --- | --- |
| Transaction and idempotency behavior | `src/payment-service-java/src/test/java/com/flashsale/payment/integration/` and the CI integration job | A green suite does not by itself prove crash recovery at every commit/response boundary |
| Outbox relay and broker failure handling | `PaymentOutboxRelayTest`, `BrokerCacheFailureDrillTest`, `RabbitMqOutageIntegrationTest` | At-least-once delivery permits duplicates; do not claim end-to-end exactly once |
| Security checks | `AuthSecurityIntegrationTest` covers expired JWT; `ProductionOidcSecurityTest` covers expired/missing-audience tokens; `PaymentCacheRedisTest` covers a BOLA ownership denial | These cases do not establish exhaustive access-control or rate-limit coverage |
| Performance | [Existing Apache Bench report](../benchmarks/t10-load-test.md) | It measured `/healthz` through `kubectl port-forward` on kind, not payment throughput; p95 exceeded the stated 500 ms target in that setup. The contribution of each latency source was not isolated |

## Two-minute demo

1. Show the public SHA and integration CI summary.
2. Walk through a payment request with an idempotency key, the PostgreSQL transaction, and the outbox row written in that transaction.
3. Show duplicate/retry and broker-outage tests. Explain why the consumer must tolerate at-least-once delivery.
4. State the benchmark environment and its limitation before quoting latency numbers.
