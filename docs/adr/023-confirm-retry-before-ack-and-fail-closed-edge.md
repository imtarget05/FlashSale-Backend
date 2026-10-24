# ADR 023 — Confirm retry before ACK; fail closed at the OpsBridge web edge

Status: Implemented locally on 2026-10-24; cloud runtime not verified.

## Business failure

An order worker could receive a transient processing error, call
`EnqueueAsync` for a retry, ignore its `false` result, then ACK the original
delivery. If the broker rejected the retry, the order disappeared. Separately,
the Pages Function returned synthetic purchase orders, invoices and replay
results when its API backend was absent or an outage occurred with
`?demo=true`. An operator could mistake a failed backend for real state.

## Decision

- The RabbitMQ queue uses publisher confirms and mandatory routing. A retry is
  successful only after broker confirmation; an unroutable publish returns
  `false`.
- A failed retry publish throws before ACK. The host fails, its broker
  connection closes and the original unacknowledged delivery is redelivered.
  The idempotent order processor handles duplicates.
- The Pages Function is a BFF only. It has no synthetic business records.
  Missing/unreachable upstream returns 503 and all API responses are
  `private, no-store`. It preserves an `/opsbridge` base path when configured
  to call the Platform Worker.

## Alternatives and trade-offs

ACK then retry was rejected because it loses a delivery when enqueue fails.
Immediate `BasicReject(requeue=true)` was deferred because the transport port
has no abandon operation and an in-process queue needs different settlement
semantics; throwing stops the worker and may temporarily reduce availability,
but preserves the delivery. Persistent messages without confirms were rejected:
the client can write to a socket while the broker has not accepted the message.

## Evidence and remaining gate

`OrderProcessorHostTests.FailedRetryPublish_DoesNotAcknowledgeOriginalDelivery`
failed at compile time before the extracted settlement method, then passed.
`ResilienceTests.RabbitMq_MandatoryPublishToDeletedQueue_IsRejectedByBroker`
passed against Testcontainers RabbitMQ. The Pages contract tests failed on
synthetic 200 responses and a dropped `/opsbridge` prefix, then passed after
the change. Local tests do not prove a worker crash/redelivery on AKS; that
drill must record broker queue state, persisted order and stock before release.
