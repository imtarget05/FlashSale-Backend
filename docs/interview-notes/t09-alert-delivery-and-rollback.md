# Interview Notes — T09 Readiness Alerts and Rollback

## Readiness alert path

The service readiness endpoint is HTTP, not a Prometheus metrics endpoint. A
Blackbox Exporter probes `/actuator/health/readiness`; Prometheus fires
`PaymentServiceReadinessDown` when the probe fails for 15 seconds. Alertmanager
then sends firing and resolved events to a local webhook receiver used only for
testing.

I kept the metrics endpoint behind the service's existing authentication
boundary after local validation returned HTTP 401. Exposing it anonymously just
to make a dashboard work would be a security change, not an observability
shortcut.

## What the drill proved

- The webhook receiver observed both firing and resolved Alertmanager payloads
  during a local kind Redis outage.
- The old image's readiness request timed out instead of returning promptly.
  Redis connect and command timeouts are now bounded at one second, and
  `RedisOutageIntegrationTest` verifies readiness returns HTTP 503 within five
  seconds.
- A local kind rollout of the candidate image failed because the existing
  PostgreSQL schema lacked `payment_records.customer_email`. Rolling back to
  the prior deployment revision restored 2/2 Ready pods and HTTP 200 readiness.

## Trade-offs and remaining evidence

- The in-repo receiver is a test sink, not a real operator notification
  channel: it is unauthenticated on the internal Compose network, published
  only on loopback, and stores the latest 100 events in memory.
- The exact 503-to-webhook sequence has not been observed on one live image:
  the old image timed out during its outage drill, while the updated image
  could not start against the stale kind database schema.
- No production SMTP/Slack/PagerDuty configuration, authorized Azure/staging
  drill, or owner go/no-go approval is available. Production therefore remains
  **NO-GO**.
