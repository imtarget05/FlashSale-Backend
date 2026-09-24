# Local Platform v2 Resource Footprint Estimate

Date: 2026-09-24
Scope: local v2 stack (kind cluster)

## Budget

- **Total budget:** 7.75 GiB (from observability evidence)
- **Kind node overhead:** ~1.8 GiB
- **Loki/Tempo (external):** 256 Mi each
- **KEDA (external):** ~100 Mi
- **Infrastructure baseline:** ~2.4 GiB

## Application Workloads (requests / limits)

| Workload | CPU req | CPU lim | Mem req | Mem lim |
|---|---|---|---|---|
| order-api | 250m | 1000m | 512 Mi | 1024 Mi |
| order-worker | 100m | 500m | 256 Mi | 512 Mi |
| payment-service | 50m | 500m | 64 Mi | 256 Mi |
| postgres | 250m | 1000m | 512 Mi | 1024 Mi |
| redis | 100m | 500m | 128 Mi | 256 Mi |
| rabbitmq | 100m | 500m | 256 Mi | 512 Mi |
| kafka | 200m | 1000m | 256 Mi | 640 Mi |
| **Total** | **1050m** | **5000m** | **1984 Mi (~1.94 GiB)** | **4224 Mi (~4.13 GiB)** |

## Footprint

- **Requests path:** infra baseline ~2.4 GiB + app requests ~1.94 GiB = **~4.34 GiB**
- **Limits path:** infra baseline ~2.4 GiB + app limits ~4.13 GiB = **~6.53 GiB**

## Verdict

**PASS**

Both requests and limits paths are within the 7.75 GiB budget with margin (~3.4 GiB headroom on the limits path).

## Notes

- `local/kind/cluster.yaml` does not exist in this repo; kind cluster configuration is managed externally in the **AKS-SRE-Platform** repository.
- Loki, Tempo, and KEDA manifests are not present in this repo; they are deployed and managed externally by the platform team.
