# Runbook: Stock Resync (`POST /internal/resync-stock/{productId}`)

- **Endpoint**: `POST /internal/resync-stock/{productId}?dryRun=true|false` (default `dryRun=true`)
- **Auth**: STAFF/ADMIN bearer token (same policy as all other `/internal/*` routes).
- **Response**: `{productId, redis, postgres, drift, fixed}` where `drift = redis − postgres`
  (a missing Redis key counts as `0`) and `fixed` is true only when this call overwrote Redis.

## When to run it

Run resync when the two stock tiers disagree. Per [ADR-003](../adr/003-redis-fast-fail-reservation.md),
Redis is a fast-fail filter and PostgreSQL is the source of truth; drift is an incident, not a bug:

1. **Worker crash between Redis decrement and DB persist** — the reservation (T1) succeeded
   but the authoritative atomic UPDATE never landed, so Redis reads low. The worker's
   conditional UPDATE dead-letters the message with `StockDriftException` instead of overselling.
2. **Manual stock edit** — anyone updates PostgreSQL directly (or a seed/migration rewrites it)
   without mirroring to Redis, so the two tiers disagree in either direction.

Do NOT run resync as routine maintenance: a healthy sale never drifts, and overwriting Redis
during a sale moves the live fast-fail counter.

## Exact curl

The local compose API listens on `localhost:${API_PORT:-5000}` (see `docker-compose.yml`).
`$STAFF_TOKEN` is a JWT for a STAFF or ADMIN account (obtain via `POST /api/auth/login`).

```bash
# 1. Dry-run (default): report only, writes nothing.
curl -s -X POST "http://localhost:5000/internal/resync-stock/1?dryRun=true" \
  -H "Authorization: Bearer $STAFF_TOKEN"

# 2. Apply: overwrite the Redis mirror with the PostgreSQL value.
curl -s -X POST "http://localhost:5000/internal/resync-stock/1?dryRun=false" \
  -H "Authorization: Bearer $STAFF_TOKEN"
```

Always run step 1 first and confirm `drift != 0` before step 2. A `404` means the product
does not exist in PostgreSQL — fix the product id, not the cache.

## How to read the Prometheus counters

Scrape `GET /metrics` (unauthenticated by design; it deliberately excludes the `auth.*`
counters — those live only in the STAFF/ADMIN-only `GET /internal/metrics` JSON snapshot).
Dots in instrument names become underscores; the `reason` tag becomes a label:

| Question | Series (Prometheus text form) |
|---|---|
| Guard fired (conditional UPDATE matched 0 rows) | `flashsale_orders_stock_drift` |
| Refusals caused by drift (vs normal sell-out) | `flashsale_orders_rejected{reason="stock_drift"}` |
| Normal sell-out refusals (expected during a sale) | `flashsale_orders_rejected{reason="out_of_stock"}` |

`stock_drift` refusions are the anomaly signal; `out_of_stock` refusals are the expected
outcome of a flash sale. After a resync with `fixed:true`, the drift series must stop
increasing — if it keeps climbing, the writer that caused the drift is still active.

## Alert threshold suggestion

Starting point (tune from your own observed baseline — this repo ships no production
baseline, so treat the numbers below as a scaffold, not a verdict):

- **Alert**: `increase(flashsale_orders_stock_drift[5m]) > 0` — any guard firing means the
  tiers disagreed; page the on-call to run the dry-run above and find the writer.
- **Do not alert** on `flashsale_orders_rejected{reason="out_of_stock"}` alone — that is
  healthy rejection during a sale.

Keep the threshold at "any drift" rather than a rate: drift should be zero in steady state,
so a tolerant threshold only delays the resync.
