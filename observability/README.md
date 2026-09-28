# Observability - FlashSale Backend

Self-contained Prometheus + Grafana + Alertmanager stack for this repo. It lives
here rather than in a shared folder, so cloning **this** repository is enough to
see the whole runtime picture - which is what a reviewer, a demo or a debugging
session actually needs.

## Quickstart

```bash
cd FlashSale-Backend/observability
docker compose up -d
docker compose config                     # validate without starting
```

| Service | Port | URL |
|---|---|---|
| Prometheus | 9105 | http://localhost:9105 (`Status -> Targets`) |
| Grafana | 3205 | http://localhost:3205 (admin / `$GF_SECURITY_ADMIN_PASSWORD`, default `admin`) |
| Alertmanager | 9305 | http://localhost:9305 |

Reload a config change without a restart:

```bash
curl -XPOST http://localhost:9105/-/reload
```

## Scrape targets

| Job | Metrics path | Port | Service |
|---|---|---|---|
| `flashsale` | `/metrics` | 5000 | FlashSale Backend |

## Dashboards

- `project-flashsale.json` - FlashSale - orders accepted/rejected, stock-guard drift (oversell), in-flight
- `golden-signals.json` - Golden signals - QPS / error rate / P95 / saturation per scrape job

## Metrics this repo exposes

| Endpoint | Service | Series |
|---|---|---|
| `GET /metrics` | Order.Api | `flashsale_orders_accepted`, `flashsale_orders_rejected{reason}`, `flashsale_orders_completed`, `flashsale_orders_stock_drift`, `flashsale_http_in_flight`, `flashsale_http_request_duration` summary |

Auth counters are **deliberately excluded** from the unauthenticated endpoint. The
richer staff-only view stays at `GET /internal/metrics` (requires StaffOrAdmin).

## Alerts

`prometheus/alerts.yml` has the `services` group: any scrape target down for 2m. This repo does not vendor the LLM gateway, so the `gateway` group is intentionally absent.

## SLOs

`slo.yaml` holds the machine-readable SLI / target / window / error-budget table.

## Operational notes

- Grafana `admin` + a default password is fine for a local demo. For anything
  shared, set `GF_SECURITY_ADMIN_PASSWORD` and keep anonymous access off.
- Every `/metrics` endpoint is aggregate-only and unauthenticated, because a
  Prometheus scraper carries no session cookie. Business detail stays behind the
  existing auth-protected endpoints.
- A target that is not running shows `DOWN`; it never blocks the other jobs.
- Ports are offset per project (Prometheus 9105) so several portfolios can run
  at the same time. Override with `PROMETHEUS_PORT` / `GRAFANA_PORT` /
  `ALERTMANAGER_PORT`.
- Docker is required. CI asserts these configs parse; it does not start the stack.
