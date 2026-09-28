# Recruiter Evidence — FlashSale-Backend (.NET 10 Flash-Sale Platform)
Problem: flash-sale oversell + double-charge khi 50 req đua 10 suất hàng.
Architecture: Clean Arch `Domain <- Application <- Infrastructure <- Api/Worker` (`AzureFlashSale.slnx`); Postgres source-of-truth + Redis Lua CAS pre-filter + RabbitMQ/ServiceBus queue + Outbox/Inbox + Checkout Saga bù trừ.
Evidence:
- `src/Order.Api/Program.cs` — 25 Minimal API routes, `/health/live|ready`, `/metrics` Prometheus
- `src/FlashSale.Infrastructure/Redis/RedisStockGateway.cs` — Lua `ReserveScript`
- `src/FlashSale.Application/Orders/SubmitOrderUseCase.cs` — flow Redis→enqueue→fallback
- `tests/UnitTests/` 22 files + `tests/IntegrationTests/` Testcontainers + `load-tests/k6/flash-sale.js` + `load-tests/concurrency/oversell_demo.py` (RESULT:PASS)
- `observability/` Prometheus/Grafana/SLO + OTEL tracing + K8s `infrastructure/kubernetes/`
- `.github/workflows/ci.yml` — build, unit, integration, concurrency-harness, Trivy, ACR push
Demo: `docker compose up` → `k6 run load-tests/k6/flash-sale.js` → Grafana `project-flashsale.json`, oversell = 0.
