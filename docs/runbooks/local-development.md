# Local Development Runbook (P01)

## Prerequisites
- Docker Desktop (Compose v2). No .NET SDK / Postgres / Redis / RabbitMQ needed.
- macOS note: ControlCenter occupies port 5000 → use `API_PORT=5065`.

## Clone + env
```bash
git clone <repo> && cd CloudDevOpsPortfolio/01-FlashSale-Backend
cp .env.example .env   # optional; defaults work without it
```

## Build + start (core only, no Kafka)
```bash
docker compose build
API_PORT=5065 docker compose up -d
docker compose ps   # postgres/redis/rabbitmq healthy, order-api healthy ~40s
```

## Check health
```bash
curl localhost:5065/health/live    # {"status":"healthy"}
curl localhost:5065/health/ready   # {"status":"ready"} (gates on Postgres)
curl localhost:5065/api/products/1 # {"id":1,...,"availableStock":N}
```

## Create an order
```bash
KEY=$(uuidgen)
curl -X POST localhost:5065/api/orders -H "Content-Type: application/json" \
  -H "Idempotency-Key: $KEY" -d '{"productId":1,"quantity":1}'  # 202
sleep 3
curl localhost:5065/api/orders/$KEY  # completed
```

## View logs
```bash
docker compose logs order-api order-worker
docker exec 01-flashsale-backend-postgres-1 psql -U postgres -d FlashSaleDb \
  -c 'SELECT COUNT(*) FROM "Orders";'
```

## Stop / restart (data preserved)
```bash
docker compose stop order-api order-worker
docker compose start order-api order-worker   # orders/stock intact (postgres-data)
```

## Clean up
```bash
docker compose down        # keeps volumes
docker compose down -v     # DESTROYS Postgres data (only after evidence collected)
docker compose --profile experimental up kafka  # opt-in only
```

## Troubleshooting
| Symptom | Cause / fix |
|---|---|
| `bind: address already in use :5000` | macOS ControlCenter; use `API_PORT=5065` |
| `libgssapi_krb5.so.2` in logs | harmless Npgsql notice, ignore |
| API `unhealthy` on old images | rebuilt: probe is now `dotnet HealthProbe.dll`, not wget |
| `42P07 relation exists` on `ef database update` | DB was EnsureCreated-era; use fresh DB |
| Kafka missing | intended: experimental profile only |
