# V2.3/V2.4 Observability & Autoscaling Verification (2026-09-24, kind)

## V2.3 — Distributed Tracing (Tempo + Loki + OpenTelemetry) — COMPLETE ✅
- **Infrastructure**:
  - `helm install loki grafana/loki`: `loki-0` 2/2 Running (256Mi limit).
  - `helm install tempo grafana/tempo`: `tempo-0` 1/1 Running (256Mi limit).
  - Grafana datasources via sidecar ConfigMap `grafana-datasources-loki-tempo`: Loki + Tempo both healthy (`OK`).
  - Derived fields: Loki `trace_id` -> Tempo; Tempo `tracesToLogs` -> Loki.
- **Application Instrumentation**:
  - `Order.Api`: Configured with `OpenTelemetry.Exporter.OpenTelemetryProtocol` (1.19.1), `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, and custom `ActivitySource` `FlashSale.Payment`.
  - `Payment.Service`: Configured with `OpenTelemetry.Exporter.OpenTelemetryProtocol`, ASP.NET Core instrumentation, and W3C traceparent logging.
  - `HttpPaymentClient`: Starts `FlashSale.Payment` activity for `POST payment-service /api/payments` with semantic tags.
  - Endpoints wired via overlay env `OTEL_EXPORTER_OTLP_ENDPOINT: http://tempo.monitoring:4317`.
- **E2E Distributed Trace Verification**:
  - Verified live E2E distributed trace across both services during checkout saga (`/api/saga/checkout`):
    - Example Trace ID: `db8921ac21f8f2a20c9bca642a07bf9a`
    - Span 1: `order-api` `POST /api/saga/checkout` (SERVER span)
    - Span 2: `order-api` `FlashSale.Payment` `POST payment-service /api/payments` (CLIENT span)
    - Span 3: `order-api` `System.Net.Http` `POST http://payment-service/api/payments` (CLIENT span)
    - Span 4: `payment-service` `POST /api/payments` (SERVER span)
  - Successfully retrieved via Tempo API (`http://localhost:3200/api/traces/{traceId}`) and visualized in Grafana Tempo datasource.

## V2.4 — Event-Driven Autoscaling (KEDA + Kafka KRaft) — COMPLETE ✅
- **Infrastructure**:
  - KEDA 2.21.0 deployed in namespace `keda` (`keda-operator`, `keda-operator-metrics-apiserver`).
  - Kafka KRaft broker updated with cluster-wide FQDN advertised listener:
    `KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka.flashsale.svc.cluster.local:9092`
    (Fixes cross-namespace discovery where KEDA operator in `keda` namespace queries Kafka broker in `flashsale` namespace).
  - Topic `orders.events` provisioned with 3 partitions and replication factor 1.
- **Autoscaling Configuration**:
  - `ScaledObject/order-worker` in namespace `flashsale`:
    - Scale target: `order-worker` Deployment (min: 1, max: 4)
    - Trigger: Kafka consumer group `flashsale-automation`, topic `orders.events`
    - Metric: Consumer lag with `lagThreshold: "10"`
    - Endpoint: `kafka.flashsale.svc.cluster.local:9092`
- **Verification Evidence**:
  - ScaledObject status:
    - `READY=True`
    - `ACTIVE=True`
  - HorizontalPodAutoscaler `keda-hpa-order-worker`:
    - Status: `AbleToScale=True`, `ScalingActive=True` (ValidMetricFound: external metric `s0-kafka-orders-events`)
    - Target: Average lag threshold monitored live.

## Final re-verification after Kafka topic lifecycle fix

- Argo CD: `flashsale` is `Synced/Healthy` at revision `50291b0`.
- Kafka recovery: `kafka-0` is `1/1 Running`; `kafka-topic-init` and `order-migrate` are `Complete`.
- Topic bootstrap is a normal sync-wave Job. The StatefulSet has no blocking `postStart` hook, so the broker can become Ready before topic creation.
- Fresh Saga run through Envoy: **21 passed, 0 failed** (happy, decline compensation, timeout compensation, inventory rejection, duplicate replay).
- Fresh Outbox/Kafka run: **14 passed, 0 failed**. Scenario B intentionally let the dispatcher drain the dead-lettered row before the operator requeue; both operations remained safe and the final DB/API state had zero pending, stuck, or dead-lettered rows.
- Fresh KEDA read: `ScaledObject` `Ready=True/Active=True`; HPA external metric `s0-kafka-orders-events` was live.
- Fresh Tempo query: trace `a374e4b3054ba1b19970932c648055eb` returned HTTP 200 and contains the current `order-api` → `payment-service` path.
- Final demo script result: **exit 0** with live Saga success/compensation, clean outbox, and KEDA `Ready=True/Active=True` reading `s0-kafka-orders-events`.
- The demo validates the current Gateway data-plane Service by owner label and fails if the Gateway health, Saga state, outbox cleanliness, or KEDA metric check does not pass.

## V2.5 — Master Demo & Verification Scripts — COMPLETE ✅
- `scripts/saga-orchestration-smoke.sh`: 21 passed, 0 failed across all 5 distributed saga scenarios.
- `scripts/demo-local-platform-v2.sh`: Runs and validates all 4 pillars; its run includes live Saga state transitions, so completion time depends on the local workflows.
  1. Workload status across kind nodes (all Ready).
  2. Saga happy-path (.01) and decline compensation (.02).
  3. Outbox drain verification (0 pending, 0 stuck, 0 dead-lettered).
  4. Observability and autoscaling health (Loki, Tempo, KEDA ScaledObject Active).
