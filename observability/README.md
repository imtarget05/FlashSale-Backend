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
| Local webhook receiver | 9094 | http://localhost:9094/events |

Reload a config change without a restart:

```bash
curl -XPOST http://localhost:9105/-/reload
```

## Scrape targets

| Job | Path | Port | Service |
|---|---|---|---|
| `payment-readiness` | `/probe` | configurable URL | Blackbox Exporter checks payment readiness |

The configured blackbox module accepts HTTP 2xx responses only. A 503 or
timeout therefore sets `probe_success` to `0`, which raises
`PaymentServiceReadinessDown` after 15 seconds.

## Dashboards

- `project-flashsale.json` - FlashSale - orders accepted/rejected, stock-guard drift (oversell), in-flight
- `golden-signals.json` - Golden signals - QPS / error rate / P95 / saturation per scrape job
- The dashboards have not been validated against the current Java payment
  service metrics; the historical project dashboard still refers to the
  removed .NET service.

Set `PAYMENT_READINESS_URL` when the service is not listening on its default
host port (`host.docker.internal:8081`). The application metrics endpoint is
not scraped by this stack because the current service security configuration
requires authentication; do not make it public solely to enable scraping.

## Metrics

Spring Actuator exposes Prometheus metrics, but this stack does not scrape the
endpoint because it returned HTTP 401 during local validation. Metrics scraping
needs an explicit authenticated/allowlisted design; the readiness probe avoids
changing the service's current security boundary.

## Alerts

`prometheus/alerts.yml` alerts when a scrape target is down and when the
payment service readiness URL stops returning HTTP 2xx. The readiness check
uses the Blackbox Exporter; Alertmanager posts firing and resolved notifications
to the local webhook receiver. The receiver is a development/test sink on the
Compose network, not a production notification service. Configure an
operator-managed HTTPS receiver (or SMTP/ChatOps integration) before treating
alerts as operationally delivered outside a local demo. This repo does not
vendor the LLM gateway, so the `gateway` group is intentionally absent.

The default readiness URL is
`http://host.docker.internal:8081/actuator/health/readiness`. Override it with
`PAYMENT_READINESS_URL` when the service is exposed elsewhere. For kind, start
a local port-forward before the observability stack:

```bash
kubectl -n flashsale port-forward svc/payment-service 18080:80
PAYMENT_READINESS_URL=http://host.docker.internal:18080/actuator/health/readiness \
  docker compose up -d
```

Inspect delivered webhook events at `http://localhost:9094/events`. The
receiver stores only the latest 100 events in memory; restarting it clears the
test history.

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
