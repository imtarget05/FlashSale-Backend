# T09 — SLO, Incident and Recovery Drill

## Scope
- Cluster: kind `flashsale-staging` (v1.30.0, single control-plane node, arm64)
- Workload: payment-service (2 replicas), postgres (1), redis (1), rabbitmq (1)
- Image: `flashsale/payment-service:local-96dc3d9 @ sha256:9cb5a5ede5331c3150ad57f7e3bcdf3102fc0fddd628a8ec6d9cf2b499905af8`
- FlashSale SHA: `96dc3d9` (merge(flashsale): integrate T04-T10a regression guards and OIDC/deploy files)
- Test endpoint: `http://localhost:8080/healthz` and `http://localhost:8080/actuator/health/readiness` (via `kubectl port-forward svc/payment-service 8080:80 -n flashsale`)

## SLI / SLO targets

| SLI | Definition | Target | Status |
|-----|------------|--------|--------|
| Error rate | % of /healthz + /actuator/health/readiness requests returning non-2xx | < 1% | TARGET |
| Latency p95 | 95th percentile response time of /healthz | < 500 ms | TARGET |
| Availability | % of 1-minute uptime windows where /healthz returns 200 | > 99% | TARGET |

## Controlled outage drill — rabbitmq (07:56:24Z)

Dependency killed: rabbitmq pod `rabbitmq-768cf4645f-ppnxk` via `kubectl delete pod --grace-period=0`.

| Event | Timestamp (UTC) | Command | Exit code | Observation |
|-------|-----------------|---------|-----------|-------------|
| Baseline healthz | 2026-11-06T07:56:24Z | `curl ... /healthz` | 0 | 200 |
| Baseline readiness | 2026-11-06T07:56:24Z | `curl ... /actuator/health/readiness` | 0 | 200 |
| Kill rabbitmq | 2026-11-06T07:56:24Z | `kubectl delete pod -n flashsale rabbitmq-768cf4645f-ppnxk --grace-period=0` | 0 | pod terminated |
| Time-to-detect | — | poll `/healthz` every 2s (120s) | — | **Never detected**: /healthz stayed 200 throughout |
| Pod recreated | ~2026-11-06T07:58:41Z | deployment controller | — | `rabbitmq-768cf4645f-48gl4` 1/1 Ready (~2m17s) |
| Time-to-recover | — | poll `/healthz` | — | N/A (health never failed) |

Measured TTD = not detected via /healthz | Measured TTR = ~137s (pod restart) | SLO met? = error rate 0% (health endpoint blind to rabbitmq)

## Controlled outage drill — postgres (07:58:53Z)

Dependency killed: postgres pod `postgres-6c5f5f9d48-59np8` via `kubectl delete pod --grace-period=0`.

| Event | Timestamp (UTC) | Command | Exit code | Observation |
|-------|-----------------|---------|-----------|-------------|
| Baseline healthz | 2026-11-06T07:58:53Z | `curl ... /healthz` | 0 | 200, `{"status":"healthy"}` |
| Baseline readiness | 2026-11-06T07:58:53Z | `curl ... /actuator/health/readiness` | 0 | 200, `{"status":"UP"}` |
| Kill postgres | 2026-11-06T07:58:53Z | `kubectl delete pod -n flashsale postgres-6c5f5f9d48-59np8 --grace-period=0` | 0 | pod terminated |
| Internal error detected | 2026-11-06T07:58:53.944Z | `kubectl logs` | — | HikariPool "This connection has been closed" (~1s after kill) |
| Time-to-detect (/healthz) | — | poll `/healthz`+`/readiness` every 2s (180s) | — | **Never detected**: both stayed 200 throughout |
| Pod restarted | 2026-11-06T07:58:53Z | `status.startTime` | — | `postgres-6c5f5f9d48-pblkq` 1/1 Ready (immediate) |
| Time-to-recover | — | poll `/healthz` | — | N/A (health never failed) |

Measured TTD via /healthz = not detected | Measured TTR = <1s (pod restart) | Internal errors detected in logs at +~1s | SLO met? = error rate 0% via health endpoint, but health endpoint is decoupled from dependencies

## Key finding — observability gap

`/healthz` and `/actuator/health/readiness` return static `200` / `{"status":"healthy"|"UP"}` regardless of postgres or rabbitmq state. Payment-service *does* detect dependency loss internally (HikariPool warnings within ~1s of postgres kill) but does NOT surface it via health endpoints. SLO error rate measured via /healthz = 0% is misleading; a dependency-aware readiness probe is needed.

## Backup / restore drill — postgres (08:02:29Z–08:02:45Z)

Target: postgres pod `postgres-6c5f5f9d48-pblkq`, DB `FlashSaleDb`, user `postgres`, tables `payment_records`, `payment_outbox`.

| Step | Command | Exit code | Rows before | Rows after |
|------|---------|-----------|-------------|------------|
| Insert test row | `INSERT INTO payment_records ... VALUES ('drill-key-001', ...)` | 0 | 0 | 1 |
| pg_dump | `kubectl exec $POD pg_dump -U postgres -d FlashSaleDb > /tmp/payment_dump2.sql` | 0 | 1 | — |
| Data loss | `DELETE FROM payment_records;` | 0 | 1 | 0 |
| pg_restore | `cat /tmp/payment_dump2.sql | kubectl exec -i $POD psql -U postgres -d FlashSaleDb` | 0 | 0 | 1 |
| Verify | `SELECT count(*) FROM payment_records;` | 0 | — | 1 (matches) |

- pg_dump: `2026-11-06T08:02:39Z`, exit=0, 118 lines (first dump), then with row
- pg_restore: `2026-11-06T08:02:45Z`, exit=0 (expected "relation already exists" warnings since tables pre-exist; data INSERTs applied)
- Row count before restore: 0 | after restore: 1 ✓ matches original

Measured RPO = ~6s (dump→delete window) | Measured RTO = ~6s (dump→restore-verify) | SLO met? = N/A (backup/restore not an SLO SLI)

## Timeline

| Time (UTC) | Event |
|------------|-------|
| 07:56:24 | Baseline healthz/readiness 200; kill rabbitmq |
| ~07:58:41 | rabbitmq pod recreated, 1/1 Ready (~137s) |
| 07:58:53 | Baseline healthz/readiness 200; kill postgres |
| 07:58:53.944 | Payment-service logs: HikariPool connection closed (+~1s) |
| 07:58:53 | postgres pod `pblkq` started, 1/1 Ready (immediate) |
| 08:02:29 | Backup drill: row counts 0/0 |
| 08:02:39 | pg_dump exit=0 |
| 08:02:41 | DELETE payment_records → count 0 |
| 08:02:45 | pg_restore exit=0 → count 1 ✓ |

## SLO assessment

| SLI | Measured | Target | Met? | Notes |
|-----|----------|--------|------|-------|
| Error rate (via /healthz) | 0% | < 1% | YES | Misleading — health endpoint blind to deps |
| Latency p95 (/healthz) | ~12–75ms observed | < 500 ms | YES | Single samples; full p95 in t10 |
| Availability | 100% (health never down) | > 99% | YES | Misleading — health never reflects dep outage |

## Risks / open items
- kind node stability: node was unstable once when Docker Desktop restarted; may need `kind create cluster` again and re-load image.
- Single-replica postgres/rabbitmq/redis have no HA; recovery depends on pod restart, not failover.
- No Prometheus/Grafana on kind; evidence is `kubectl logs` + `curl` timings only.
- Health endpoints need dependency-aware probes (observability gap identified above).
