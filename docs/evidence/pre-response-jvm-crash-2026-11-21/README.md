# JVM crash after commit, before response body — 2026-11-21

`python3 scripts/pre_response_jvm_crash_drill.py` started a disposable PostgreSQL 15, RabbitMQ 3 and Redis 7 stack, then launched the demo Spring application from the compiled test classpath. `CrashBeforeResponseAdvice` is registered only in this test launcher. It calls `Runtime.halt(87)` at Spring MVC's response-body hook after `PaymentService.processPayment` returns and before the HTTP message converter writes the body. The target request uses a generated idempotency key; no production endpoint or JAR contains this hook.

The [raw result](result.json) records a lost client response, JVM exit code 87, exactly one `payment_records` row and one `payment_outbox` row after the crash, and a 200 retry with the same transaction ID and no additional rows. The runner checked the database while the JVM was stopped, then restarted the app without the fault profile. It tore down the disposable Compose stack afterward.

Scope: this proves the committed-payment recovery path for a crash in the post-transaction, pre-body-write interval in a local demo profile. It does not simulate a crash inside PostgreSQL's commit itself, a real payment provider, or exactly-once broker delivery. The production `payment-service-1.0.0.jar` was inspected and contains neither `CrashBeforeResponseAdvice` nor `CrashDrillApplication`.
