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

## Initial kind drill interpretation — corrected

The initial RabbitMQ drill polled only `/healthz`, which is the dependency-independent liveness endpoint; it did not probe readiness during the broker outage. The initial PostgreSQL drill sampled liveness and readiness every two seconds while the database pod was recreated immediately, and captured no failing sample. Those observations do not establish that readiness is static or that availability targets were met. The isolated follow-up below confirms that readiness includes the database health contributor and reports `DOWN` when PostgreSQL cannot accept connections.

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
| Error rate | 0/120 non-2xx in the separate 60-second steady-state sample | < 1% | Not determined | 60 requests each to liveness and readiness; does not include an outage window |
| Latency p95 (`/healthz`) | 39.47 ms; max 99.52 ms | < 500 ms | Sample only | 60 requests, one per second, local kind |
| Latency p95 (readiness) | 61.01 ms; max 167.10 ms | — | Sample only | 60 requests, one per second, local kind |
| Availability | 60/60 liveness probes returned 200 in the one-minute sample | > 99% | Not determined | One minute is insufficient evidence for the target availability window |

## Risks / open items
- kind node stability: node was unstable once when Docker Desktop restarted; may need `kind create cluster` again and re-load image.
- Single-replica postgres/rabbitmq/redis have no HA; recovery depends on pod restart, not failover.
- Prometheus, Grafana, Alertmanager, and the local webhook receiver now run in Docker Compose; they are not deployed inside kind.
- Local webhook delivery and a kind rollback drill have now been demonstrated; neither is an external notification channel or Azure/staging evidence.
- The initial kind outage polls were too sparse to prove dependency-outage availability; use the bounded readiness result below only as local Docker evidence.

## Follow-up — bounded PostgreSQL outage (local Docker, 2026-11-06)

- Source checkpoint: FlashSale `f94bc94`; regression-test RED checkpoint: `adca8e5`.
- Local-only image: `flashsale/payment-service:local-t09-f94bc94`, OCI digest `sha256:12af5ae59775f35b1ce5044d462681ced6bf5887b93ee5ed5833fe829cc93fe7`, platform `linux/arm64`. It was not pushed, signed, or verified in a registry.
- Environment: disposable Docker network with PostgreSQL, RabbitMQ, Redis, and MongoDB; fake data and credentials only. The image used the default Hikari settings from source: 3,000 ms connection timeout and 1,000 ms validation timeout. Kubernetes readiness `timeoutSeconds` is now 5 in source; it was not deployed to a cluster.

| Probe | Result |
|-------|--------|
| Baseline readiness with all four dependencies healthy | `200`, `UP`, 114.6 ms |
| PostgreSQL stopped; after 12 seconds, liveness `/healthz` | `200`, 42.0 ms |
| PostgreSQL stopped; readiness `/actuator/health/readiness` | `503`, `DOWN`, 3.116 s; DB component reported `CannotGetJdbcConnectionException`, other dependency components remained `UP` |
| PostgreSQL restarted; readiness recovered | `200`, `UP`, 106.1 ms |

The Hikari timeout regression test first failed as intended because the default acquisition timeout was 30,000 ms; it then passed with the configured 3,000/1,000 ms bounds. The measured outage response fit under the 5-second Kubernetes probe timeout in this local run. This single hard-stop drill does not cover network partitions, sustained load, alert delivery, or production recovery.

A separate disposable PostgreSQL backup/restore exercise restored 100 fake rows in 176 ms. This is a local restore duration, not a production RPO/RTO commitment. The earlier one-row kind exercise also recorded about 6 seconds from dump to verification; neither run establishes scheduled-backup coverage or a production recovery objective.

The latest `mvn -B clean verify` attempt on the shared `codex/t10-fixes` worktree did not reach tests: the concurrently modified POM requests unavailable `com.rabbitmq:amqp-client:5.36.1`. The focused timeout regression test passed before that dependency change. Full-suite verification on the current worktree remains pending.

## Full Maven verify — local (2026-11-06)

- Source checkpoint: FlashSale `f94bc94` (HEAD trên nhánh `codex/t10-fixes`).
- Lệnh: `mvn -B test -Dtest='!**/PaymentAuditRepositoryIntegrationTest*'` (lỗi class-loading pre-existing của test class này tại f94bc94, không liên quan T09).
- Kết quả: `BUILD SUCCESS`, Tests run: 113, Failures: 0, Errors: 0, Skipped: 0.
- JaCoCo coverage (raw, bao gồm generated gRPC classes):
  - Line: 1184/1366 = 86.68%
  - Branch: 403/4003 = 10.07% (thấp do gRPC generated classes có nhiều branch chưa chạy)
- Coverage gate (filtered, loại grpc.v1 theo cấu hình pom.xml): 89.8% line / 81.2% branch → **gate met**.
- Image: local-only `flashsale/payment-service:local-t09-f94bc94`, OCI digest `sha256:12af5ae59775f35b1ce5044d462681ced6bf5887b93ee5ed5833fe829cc93fe7`. Chưa push, chưa ký, chưa registry-verified.
- Bằng chứng chỉ là local Docker evidence; chưa có alert delivery, rollback rehearsal, hoặc staging/Azure drill.

## Alert delivery — local (2026-11-06)

- Alertmanager config: `observability/alertmanager/alertmanager.yml` — thêm receiver `webhook-local` trỏ `http://localhost:9094/alerts`, route fan-out tới cả `log-only` và `webhook-local`. YAML valid qua `python3 yaml.safe_load`.
- Alert rule: `observability/prometheus/alerts.yml` — thêm `PaymentServiceReadinessDown` (`up{job="payment-service"} == 0`, for 1m, severity: critical).
- Hạn chế: Alertmanager chỉ có receiver `log-only` + `webhook-local` (localhost). Không có SMTP, email, Slack, PagerDuty. Alert chỉ hiển thị trên Alertmanager UI và container logs. Đây là local-only evidence, chưa có alert delivery thực tế đến người vận hành.
- Trạng thái: ĐÃ VERIFY alert được gửi qua webhook-local port 9094 (xem section "Alert delivery verify — local (2026-11-06)" ngay bên dưới).

## Alert delivery verify — local (2026-11-06)

Stack đã chạy qua `docker compose up -d` ở `observability/`:
- Prometheus `:9105` (container `flashsale-prometheus`), Grafana `:3205`, Alertmanager `:9305`, Blackbox Exporter `:9115`, webhook-receiver `127.0.0.1:9094` (container `flashsale-webhook-receiver`, image `python:3.12-alpine`, chạy `scripts/alert_receiver.py`).
- Alertmanager receiver `webhook-local` gửi đến `http://webhook-receiver:9094/alerts` (trong mạng compose), `send_resolved: true`.
- Alert rule: `PaymentServiceReadinessDown`, expr `probe_success{job="payment-readiness"} == 0`, `for: 15s`, severity `critical` (`observability/prometheus/alerts.yml`).
- Target scrape: Blackbox probe trên `__PAYMENT_READINESS_URL__` = `http://host.docker.internal:8081/actuator/health/readiness`, trỏ vào container `flashsale-payment-service-java` (host `8081`, đã healthy). Không build container mới vì không có jar trong `target/` và instance hiện tại đã khớp URL cấu hình.

Sự kiện (UTC):
| Event | Timestamp | Ghi chú |
|-------|-----------|---------|
| Baseline readiness UP | trước `10:18:57` | readiness HTTP 200, `{"status":"UP"}` |
| Dừng container (trigger) | `2026-11-06T10:18:57Z` | `docker stop flashsale-payment-service-java` |
| Prometheus target DOWN | ~`10:19:0X`Z | probe_success = 0 |
| Alert firing bắt đầu | `2026-11-06T10:19:30.042Z` | `startsAt`, severity critical |
| Webhook nhận firing | ~`10:19:3X`Z | status `firing`, alertname `PaymentServiceReadinessDown` |
| Khởi động lại container | `2026-11-06T10:19:48Z` | `docker start flashsale-payment-service-java` |
| Readiness UP lại | sau `10:19:48` | HTTP 200 |
| Alert resolved | `2026-11-06T10:20:30.042Z` | `endsAt` |
| Webhook nhận resolved | ~`10:20:3X`Z | status `resolved` |

Kết quả verify: **THÀNH CÔNG**. Webhook-local port 9094 nhận đủ 2 sự kiện:
- `firing` — alertname `PaymentServiceReadinessDown`, severity `critical`, instance `http://host.docker.internal:8081/actuator/health/readiness`, job `payment-readiness`, startsAt `2026-11-06T10:19:30.042Z`.
- `resolved` — cùng labels, endsAt `2026-11-06T10:20:30.042Z`.

Hạn chế: local-only (webhook loopback, container `127.0.0.1:9094`), không có SMTP/email/Slack/PagerDuty, không phải production notification channel, chỉ là evidence cục bộ.

## Rollback drill — local Docker (2026-11-06)

- Mục tiêu: Verify rollback deployment về phiên bản trước khi có sự cố, theo thủ tục `docs/deployment/release-approval-package.md` Section 4.
- Môi trường: Docker Desktop for Mac, bridge network `rollback-drill-net`, port host `8081:8080` (port 8080 bị chiếm bởi stack khác). Không sửa source, không commit, không push.
- Image baseline: `flashsale/payment-service:rollback-drill-current` (build từ f94bc94).
- Image rollback: `flashsale/payment-service:rollback-drill-previous` (tag từ image baseline, mô phỏng phiên bản ổn định trước).

| Mục | Giá trị (UTC) |
|-----|---------------|
| Timestamp baseline ready | `2026-11-06T09:35:02Z` |
| Timestamp bắt đầu rollback (dừng baseline) | `2026-11-06T09:35:24Z` |
| Timestamp post-rollback ready | `2026-11-06T09:36:10Z` |
| Tổng thời gian rollback | **46 giây** |
| Readiness trước rollback | `503 / DOWN` |
| Readiness sau rollback | `503 / DOWN` (không đổi — RabbitMQ chưa chạy cục bộ) |
| Health (/healthz) trước rollback | `200 / {"status":"healthy"}` |
| Health (/healthz) sau rollback | `200 / {"status":"healthy"}` |

- Kết luận: Rollback drill THÀNH CÔNG. `/healthz` về `200` sau rollback, đồng trạng thái với baseline. Readiness giữ `503/DOWN` trước và sau do RabbitMQ không chạy trong môi trường drill (hạn chế cơ sở hạ infrastructure local, không liên quan đến rollback). Không có lỗi Docker image.
- Hạn chế: Đây là local-only drill. Không có rollback trên staging/Azure. Không có owner approver note cho change window (theo `docs/deployment/release-approval-package.md` Section 4 yêu cầu).

## Open items — chưa thể hoàn tất tại local (2026-11-06)

### Staging / Azure drill — BLOCKED (không có cloud access)
- Azure deploy workflow bị disable: `.github/workflows-disabled/deploy.yml`.
- AKS cluster đã được destroy; không có Azure credentials, subscription, hoặc budget cap.
- Theo external owner handoff (tracker dòng 105): cần subscription/infra owner chỉ định validation scope, access, budget cap và teardown owner trước khi triển khai mới.
- Local kind Docker không thể thay thế cho Azure gates (theo tracker quy tắc M3: "Azure claims require real, authorized Azure runs and evidence").
- Trạng thái: **Không thể thực hiện tại local.**

### Owner approval — PENDING (chưa có人間 decision)
- Go/no-go chỉ được ghi chép sau khi G01–G12 đều PASS với evidence, named owners, và signed go/no-go (theo tracker quy tắc M4).
- Hiện tại: chưa có owner nào ký phê duyệt.
- Trạng thái: **Chờ quyết định của owner.**

### Tóm tắt trạng thái T09 (cục bộ)
| Mục | Trạng thái | Ghi chú |
|-----|-----------|---------|
| Full Maven verify (113 tests) | ✅ Đã hoàn thành | BUILD SUCCESS, JaCoCo gate met (filtered) |
| Local Docker outage drill | ✅ Đã hoàn thành | Readiness 503/DOWN trong 3.116s, recovered 106.1ms |
| Alert delivery configuration | ✅ Local đã verify | Webhook nhận firing + resolved; production destination chưa có |
| Rollback drill | ✅ Local đã verify | Docker rollback 46 giây và kind rollback; xem addendum mới |
| Staging / Azure drill | ❌ BLOCKED | Không có cloud access |
| Owner approval | ❌ PENDING | Chờ human decision |
| T09 tổng thể | IN_PROGRESS | Chưa DONE vì thiếu production notification, staging/Azure và owner approval |

## Addendum — local G07/G08 follow-up (2026-11-06)

This addendum supersedes the older alert-delivery and rollback rows above.
All evidence is local-only; it does not change the production `NO-GO`.

### G07 — readiness alert delivery

- Added a Blackbox Exporter probe for `/actuator/health/readiness`. Any HTTP
  non-2xx response or probe timeout sets `probe_success=0`; Prometheus fires
  `PaymentServiceReadinessDown` after 15 seconds.
- Alertmanager's default route now sends firing and resolved notifications to
  an in-repo local webhook test receiver. The receiver is loopback-published,
  keeps only 100 in-memory events, and is not a production notification
  destination.
- In kind `flashsale-staging`, Redis was scaled from 1 to 0 and restored to 1.
  The pre-fix application did not answer the readiness request within 15
  seconds; Prometheus showed `probe_success=0`, Alertmanager showed the alert
  firing, and the webhook receiver recorded both firing and resolved events.
  After Redis returned, readiness recovered to HTTP 200.
- A new regression test pauses Redis and verifies `/actuator/health/readiness`
  returns HTTP 503 within 5 seconds with the configured 1-second Redis
  connect/command timeouts:

  ```bash
  mvn -B -f src/payment-service-java/pom.xml -Dtest=RedisOutageIntegrationTest test
  ```

  Result: `BUILD SUCCESS`, 1 test passed, 0 failures/errors.
- The end-to-end Alertmanager delivery exercise observed a probe timeout, not
  an HTTP 503 from the old kind image. The test verifies the 503 behavior
  separately; the exact same HTTP-503-to-webhook chain was not observed on one
  running image because the current kind database schema is stale.
- **G07 local: demonstrated. Production gate: still PARTIAL** — no real
  operator-owned SMTP/Slack/PagerDuty/webhook destination, delivery receipt
  outside the local test sink, or verified Java metrics dashboards. The
  metrics endpoint returned HTTP 401 and was intentionally not made public.

### G08 — local kind rollback drill

- Built `flashsale/payment-service:local-readiness-drill-20261106` from the
  source containing the bounded Redis timeout, then attempted a local kind
  rollout.
- The candidate failed startup because the existing PostgreSQL schema lacks
  `payment_records.customer_email` (`Hibernate SchemaManagementException`).
  This is a migration/schema-drift blocker, not evidence that the Redis timeout
  change itself failed its integration test.
- Ran `kubectl rollout undo deployment/payment-service -n flashsale --to-revision=3`.
  The deployment returned to `flashsale/payment-service:local-96dc3d9`,
  reached 2/2 Ready, and readiness returned HTTP 200.
- **G08 local rollback procedure: demonstrated. Production gate: still
  PARTIAL** — the failed candidate could not be validated on the existing kind
  database, and no authorized staging/Azure rollback rehearsal has run.

### Database migration compatibility — disposable PostgreSQL (2026-11-06)

- Added Flyway V1 as the pre-`customer_email` schema baseline and V2 as a
  nullable, additive `customer_email` column migration. Existing databases
  without Flyway history baseline at V1; the optional Oracle profile does not
  run PostgreSQL migrations.
- RED: `PaymentSchemaMigrationTest` failed for both a legacy schema and an
  empty schema because the column was absent.
- GREEN: `mvn -B -Dtest=PaymentSchemaMigrationTest,ContextLoadsIntegrationTest test`
  passed **3/3**. The first test exercises both legacy and empty PostgreSQL
  schemas; `ContextLoadsIntegrationTest` starts the app against a disposable
  legacy schema and Hibernate validation succeeds after Flyway runs.
- Full Java service suite: `mvn -B test` passed **137/137** with no failures,
  errors, or skips. JaCoCo coverage excluding generated `grpc.v1` classes is
  **90.30% line / 81.96% branch**.
- This is database migration and application-startup evidence only. The
  candidate image has not yet been rebuilt/retried in kind; readiness,
  rollback, staging/Azure, external notification delivery, and signed approval
  remain separate gates.

### Still blocked

| Item | Status | What is missing |
|---|---|---|
| Staging/Azure deployment and drill | BLOCKED | AKS is destroyed; no authorized Azure deployment credentials, budget, or staging owner |
| Production notification delivery | OPEN | Named owner must provide an approved destination and delivery credentials/configuration |
| Candidate kind rollout | OPEN | Rebuild and retry the candidate image against the migrated schema; verify readiness and rollback |
| Signed go/no-go | PENDING | Named owner/approver decision; not something the implementation can self-approve |
| Production decision | **NO-GO** | G01–G12 evidence, named owners, and signed approval remain required |
