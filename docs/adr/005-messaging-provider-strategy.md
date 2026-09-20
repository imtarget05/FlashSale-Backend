# ADR-005: Messaging Provider Strategy (RabbitMQ Local + Service Bus Azure)

- **Status**: Accepted
- **Date**: 2026-09-20
- **Deciders**: Project owner + engineering agent

## Context
The audit found a contract mismatch: the application spoke RabbitMQ/InMemory
while Terraform provisioned Azure Service Bus (`sb-flashsale-*`, queue `orders`).
The Azure path therefore could not work as documented, and older docs claimed a
`ServiceBusOrderQueue` that did not exist in code.

## Problem
One order pipeline must run in two environments without forking business logic:
local development (reproducible, no cloud dependency) and Azure production
(managed messaging, scales with Container Apps / KEDA).

## Options
1. **RabbitMQ everywhere** — self-host on Azure (Container Apps or AKS). Full
   control, but we operate clustering, persistence, upgrades, and monitoring.
2. **Service Bus everywhere** — one managed broker, but local development then
   requires an Azure connection or an emulator, hurting reproducibility and cost.
3. **RabbitMQ local + Service Bus on Azure (chosen)** — environment-specific
   adapters behind one application port.

## Decision
- Keep the existing ports `IOrderQueueProducer` / `IOrderQueueConsumer` and
  `QueueEntry` (JSON `OrderMessage`, at-least-once, `Complete`/`DeadLetter`).
- Add `MessagingProvider` (`InMemory`/`RabbitMQ`/`ServiceBus`) +
  `MessagingProviderSelector.Resolve(explicit, rabbitConn, serviceBusConn)`:
  explicit `Messaging:Provider` wins, else auto-detect (ServiceBus > RabbitMQ >
  InMemory); unknown values throw fail-fast.
- Add `ServiceBusOrderQueue` (PeekLock, prefetch 10, `MessageId` =
  IdempotencyKey, 2s receive poll returning null when idle, unreadable payload
  dead-lettered). Keep `RabbitMQOrderQueue` (durable `orders`, prefetch 10)
  and `InMemoryOrderQueue` (bounded 5,000, 503 backpressure) unchanged.
- Add `AddOrderQueue(IConfiguration)` composition helper; both `Order.Api`
  and `Order.Worker` call it so producer and consumer always resolve to the
  SAME singleton instance.
- Terraform already matches: namespace `sb-flashsale-*` (Standard),
  queue `orders` (`lock_duration PT1M`, `max_delivery_count 4` =
  `OrderProcessor.MaxAttempts`), connection injected as
  `ConnectionStrings__ServiceBus` secret `sb-connection`.

## Trade-offs
- Positive: business logic (`OrderProcessor`, Redis reservation, atomic DB
  update, idempotency) never knows the broker; local stays offline-capable;
  Azure gets a managed, KEDA-scalable queue.
- Negative: two broker code paths to maintain; Service Bus adapter is
  compile-tested + unit-selected only — end-to-end Azure verification still
  pending (no `terraform apply`, no live send/receive yet).

## Consequences
- `Messaging:Provider` / `Messaging:QueueName` + `ConnectionStrings:RabbitMQ`
  / `ConnectionStrings:ServiceBus` are the contract between app and infra.
- `OrderProcessorHost` retry (4 attempts, exp backoff) + DLQ semantics are
  unchanged across providers; Service Bus native DLQ is used via `DeadLetter`.

## Testing strategy
- `MessagingProviderSelectorTests` (7 cases): explicit wins, auto-detect
  priority, InMemory fallback, unknown fail-fast — no live broker required.
- Existing 7 use-case/architecture tests unchanged and green.
- Deferred: Testcontainers integration (real Postgres/Redis/RabbitMQ) and a
  live Service Bus round-trip after first Azure deployment.
