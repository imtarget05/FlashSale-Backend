# ADR-004: Asynchronous Order Fulfillment with At-Least-Once Semantics

- **Status**: Accepted
- **Date**: 2026-09-19
- **Deciders**: Project owner + engineering agent

## Context
Phase 5's benchmark proved the reservation tier fixes the rejection cost, but the HTTP
contract changes: the API now *accepts* an order (202) and fulfillment happens out of
band. We need a messaging design that survives worker crashes, duplicate deliveries,
and extended outages — and a story we can defend in an interview.

## Decision
- **Queue abstraction**: `IOrderQueueProducer` / `IOrderQueueConsumer` in
  `FlashSale.Shared.Messaging`. Two implementations share the identical contract:
  - `InMemoryOrderQueue` — bounded channel (5,000), dev/test only. `TryWrite` false
    when full → API answers **503** (backpressure), never silently drops.
  - `ServiceBusOrderQueue` — Azure Service Bus (peek-lock), production path.
- **Processing contract** (`OrderProcessor.ProcessAsync`):
  - Idempotent: unique index on `Orders.IdempotencyKey` — redelivery is a no-op.
  - Authoritative: repeats the atomic conditional UPDATE (ADR-002) in a transaction.
  - Drift → `StockDriftException` → dead-letter, never retried blindly.
- **At-least-once delivery is assumed** (never exactly-once processing). The consumer is
  idempotent, so duplicates are harmless. Retries use exponential backoff
  (200 ms × 2^n, max 4 attempts); exhausted messages go to the DLQ
  (`logs/dlq.log` locally, Service Bus DLQ in Azure).
- **HTTP semantics**: `202 Accepted {status: processing}` + `GET /api/orders/{key}` for
  status polling. Clients get honest, fast feedback.

## Consequences
- **Positive**: winners get guaranteed processing; the DB is shielded from hopeless
  traffic; the worker scales independently (KEDA on queue depth in AKS — Project 03).
- **Negative**: eventual consistency between "accepted" and "persisted" (seconds);
  requires idempotency discipline everywhere; DLQ needs an operator runbook.
- **Rejection of simpler alternatives**: synchronous-only would re-expose the DB to
  winner work plus retries; fire-and-forget without idempotency would risk lost or
  duplicated orders under at-least-once delivery.