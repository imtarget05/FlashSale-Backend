# Integration Tests (Testcontainers)

## Architecture

```text
dotnet test
   ├── FlashSaleFixture (IAsyncLifetime, collection "flashsale")
   │     ├── PostgreSqlContainer  postgres:15-alpine
   │     ├── RedisContainer       redis:7-alpine
   │     └── RabbitMqContainer    rabbitmq:3-management-alpine
   ├── each test: EnsureDeleted → Migrate → seed 1 product (known stock)
   └── dispose: all containers stopped
```

No global Postgres/Redis/RabbitMQ required — only Docker. No Azure.
Full unfiltered run spins ~3 containers and takes minutes on first pull;
use `--filter` for focused runs.

## What each test proves (all assert Postgres state, not just HTTP)

| Test | Infra | Invariant |
|---|---|---|
| NormalPurchase | Postgres | stock 10→9, 1 order |
| ConcurrentPurchases (50× stock 10) | Postgres ×50 contexts | `sold ≤ 10`, `final ≥ 0`, `initial == final + sold` (==10 sold). Guard: atomic conditional UPDATE (ADR-002) |
| DuplicateDelivery (5× same key) | Redis + Postgres | 1 order, stock −1. Guards: Redis ledger fast-dedup + unique index + ExistsAsync |
| RedisDown fallback | dead Redis endpoint + Postgres | `Unavailable` → sync `PersistAsync` still completes, stock −1 |
| RabbitMq round-trip | RabbitMQ + Postgres | publish → consume → persist → ack; stock −2 |
| InMemory full → false | none | 5,005th write false → 503+release contract |
| InvalidQuantity (0, −5) | Postgres | `ArgumentOutOfRangeException`, stock unchanged, 0 orders |
| UnknownProduct | Postgres | `false`, stock unchanged, 0 orders |
| RestartSimulation | Postgres | fresh context keeps schema/orders/stock, no seed dup |

## Still not tested

- HTTP end-to-end (tests call repository/queue directly, not the API).
- Live Service Bus round-trip (no emulator used).
- Container-kill mid-drain chaos; KEDA scaling; OTel/metrics.
- Production migration job separated from app boot (ADR-006 option 3).
