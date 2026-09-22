# Data Classification (P01 FlashSale)

> REFERENCE PORTFOLIO REQUIREMENTS — hypothetical targets for learning, not a real company's policy.

| Store | Role | Protection |
|---|---|---|
| PostgreSQL (`FlashSaleDb`) | **BUSINESS SOURCE OF TRUTH** — Products, Orders, stock, idempotency keys, migration history | Backup MANDATORY (Phase 3A local proof → 3B off-host) |
| Redis (`stock:{id}`, `reservation:{id}`) | RECONSTRUCTABLE DERIVED STATE — fast-fail mirror rebuilt from Postgres on boot | NO business-data backup; rebuild drill only |
| RabbitMQ (`orders` queue) | TRANSIENT DELIVERY STATE — accepted-but-unpersisted messages | Definitions backup (Phase 3B); messages NOT the authoritative DB |
| Terraform state | INFRASTRUCTURE CONTROL STATE — currently local files only | Remote encrypted backend (Phase 3B / Phase 7) |
| Kubernetes manifests | DESIRED STATE IN GIT | Git is the backup; no separate dump |
| Container images (ACR) | IMMUTABLE RELEASE ARTIFACTS | Registry retention policy (Phase 6) |
| Application logs / metrics | OPERATIONS EVIDENCE | Short retention locally; central retention (Phase 8) |
| DLQ evidence (structured stdout log, Service Bus native DLQ) | OPERATIONS EVIDENCE (order failure forensics) | Central log store from process start |

Consequence: only PostgreSQL loss is a **critical business outage**. Redis/RabbitMQ loss = degraded mode, recoverable from Postgres truth + redelivery.
