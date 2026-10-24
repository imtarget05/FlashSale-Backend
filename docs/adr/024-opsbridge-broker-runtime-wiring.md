# ADR 024 — Wire OpsBridge outbox and consumer into the API runtime

Status: Implemented and locally verified on 2026-10-24; AKS runtime blocked.

## Business failure

The outbox publisher and GoodsReceipt consumer classes existed and passed
direct tests, but `OpsBridge.Api` did not register either as hosted services.
A webhook could commit an event to PostgreSQL while no running component
published or consumed it. A green class test was therefore not a release proof.

## Decision

- `OpsBridge:Messaging:Enabled=true` registers the outbox publisher, an
  asynchronously initialized RabbitMQ publisher and a supervised consumer.
  The runtime manifest sets the flag and requires `rabbitmq-connection` in
  `opsbridge-secrets`. Missing or malformed broker URI fails composition.
- Broker outage after startup, or while the API starts, does not reject
  webhooks: the outbox remains durable in PostgreSQL. The consumer supervisor
  retries connection and the outbox publisher retries pending rows.
- `/healthz` remains liveness. `/health/messaging` returns 503 while the
  consumer channel is unavailable, so monitoring can alert without taking the
  webhook intake endpoint out of service. A separate alert on oldest pending
  outbox age is still required before cloud release.
- No synchronous wait on an async RabbitMQ factory occurs in DI. The publisher
  initializes on first use and remains singleton.

## Verification and limits

`MessagingRegistrationTests` failed before the registrations existed, then
passed. `MessagingCompositionIntegrationTests` starts both registered
services against Testcontainers PostgreSQL and RabbitMQ, inserts a canonical
event and waits until the outbox is Published and one GoodsReceipt is
persisted. A broker-down test proves the configured host can start and write
an outbox row while the consumer reports degraded. These are local tests,
not evidence of AKS deployment, alert firing or rollback.
