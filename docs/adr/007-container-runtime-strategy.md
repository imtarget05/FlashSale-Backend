# ADR-007: Container Runtime Strategy

- **Status**: Accepted
- **Date**: 2026-09-20

## Context
P01 compose was dev-only: root runtime, `wget` probe that cannot work
(aspnet image has no wget/curl — verified), Kafka started by default though
unused, no resource bounds, hard-coded dev credentials, ephemeral DLQ.

## Decision
- **Multi-stage** SDK→aspnet kept; `HealthProbe` (framework-dependent,
  `dotnet exec`) published alongside the API for HEALTHCHECK without new
  packages; `.dockerignore` cuts context (git, docs, tfstate, bin/obj).
- **Non-root**: built-in `app` user (uid 1654, verified `id` in both
  containers); publish output chowned, `/app/logs` writable for file DLQ.
- **Health**: `/health/live` (process alive) + `/healthz` alias;
  `/health/ready` gates on Postgres `CanConnectAsync` only. Redis/RabbitMQ
  are RUNTIME OPTIONAL (Task-2 fallback proven) so they never gate readiness.
  RabbitMQ stays STARTUP REQUIRED via `depends_on healthy` because the
  blocking ctor fails boot without a broker (known limitation).
- **Compose**: health-aware ordering; local/demo limits (api 1 CPU/1G,
  worker 0.5/512M, pg 1/1G, redis 0.5/256M, rabbit 0.5/512M),
  `unless-stopped`, 10m×3 log rotation; named volumes
  `postgres-data`/`rabbitmq-data`/`dlq-data`; Redis runs persistence-off
  (reservation tier rebuilds from Postgres each boot).
- **Kafka** → `profiles: [experimental]` opt-in; dead
  `KafkaActivityTracker` code kept but documented UNUSED.
- **Env**: `${VAR:-default}` everywhere + `.env.example` (`API_PORT=5000`
  default; macOS ControlCenter squats 5000, so local runs use
  `API_PORT=5065`); `.env` git-ignored (no `.env` committed).

## Trade-offs
- `libgssapi_krb5.so.2` Npgsql/Kerberos notice in logs is harmless (no GSS).
- HealthProbe adds a second publish step (~MBs) for a truthful health signal.
- DLQ volume preserves local evidence across restarts but is NOT a
  production DLQ story (Service Bus native DLQ on Azure).

## Consequences
- `docker compose build/up/ps` reproducible; API healthy in ~40s;
  restart keeps Postgres orders/stock; images ~705/710MB (SDK-based,
  debuggability over minimal size).
