# L10 Observability Known Limits

Date: 2026-09-24

## Alertmanager

- **[KNOWN-LIMIT] Alertmanager:** Not configured in this repository. Alerting is either managed externally (by the platform team) or not deployed in the local v2 stack.

## App /metrics Endpoints

- **order-api:** Exposes `GET /internal/metrics` returning a JSON snapshot (`ApiMetrics.Snapshot()`). This is NOT a Prometheus `/metrics` endpoint.
- **order-worker:** No HTTP listener; no `/metrics` endpoint.
- **payment-service:** No `/metrics` endpoint configured in the container spec; liveness/readiness probes use `/health`.

No container in the base manifests exposes a Prometheus-formatted `/metrics` endpoint.

## External Observability Components

- **Loki:** Referenced in `infrastructure/kubernetes/overlays/local/kustomization.yaml` (`tempo.monitoring` namespace). Manifest and configuration are not in this repo.
- **Tempo:** Referenced in `infrastructure/kubernetes/overlays/local/kustomization.yaml` (`OTEL_EXPORTER_OTLP_ENDPOINT: http://tempo.monitoring:4317`). Manifest and configuration are not in this repo.
- **Grafana:** Not referenced in base manifests or overlays in this repo.

## Platform Directories Absence

- **`platform/argocd/`:** Does not exist in this repo. Argo CD configuration (sync waves, hooks, Application manifests) is managed externally in the **AKS-SRE-Platform** repository.
- **`platform/observability/`:** Does not exist in this repo. Observability stack (Tempo, Loki, Alertmanager, Grafana dashboards) is managed externally.

## Other Gaps

- No ServiceMonitor or PodMonitor resources defined in this repo for Prometheus scraping.
- No alerting rules or recording rules defined in this repo.
