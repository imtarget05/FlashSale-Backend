# Cloud remediation runbook

Status: remediation planning only. No cloud/cluster mutation performed. No credentials used or committed.

Scope: three production-impacting issues — Render `UnknownHostException: postgres`, AKS order-worker CrashLoopBackOff (invalid Azure Service Bus connection used as RabbitMQ host), ArgoCD `aks-sre-platform` Degraded at stale revision `7633cad`.

Repo facts verified in this session:
- `render.yaml` defines only two web services (`flashsale-payment-api-preview` on branch `preview`, `flashsale-payment-api-prod` on branch `main`). No PostgreSQL service is declared in the blueprint.
- `src/payment-service-java/src/main/resources/application.properties:11`: `spring.datasource.url=${SPRING_DATASOURCE_URL:jdbc:postgresql://postgres:5432/FlashSaleDb}` — default host `postgres` is valid only inside local docker-compose (`docker-compose.yml:45` service name `postgres`), not on Render.
- `render.yaml` already sets `SPRING_DATASOURCE_URL`, `SPRING_DATASOURCE_USERNAME`, `SPRING_DATASOURCE_PASSWORD` with `sync: false` for both services — dashboard values are authoritative and must not be committed.
- `infrastructure/argocd/flashsale-application.yaml` (`flashsale-production`, `targetRevision: main`) is NOT the degraded app.
- `aks-sre-platform` and revision `7633cad` do not appear in this repo; that app lives in the separate AKS-SRE-Platform repo (PR #30, imtarget05/AKS-SRE-Platform per project memory).
- Leaked credentials (Render hook URLs, Cloudflare token, GH PAT, Sonar, Neon) must not be used or reused; rotate them out-of-band.

---

## 1. Render preview/prod deploy failure — `UnknownHostException: postgres`

### Root cause

`SPRING_DATASOURCE_URL` defaults to `jdbc:postgresql://postgres:5432/FlashSaleDb` (`application.properties:11`). On Render there is no service named `postgres` and `render.yaml` declares no `type: postgres` database, so the JVM cannot resolve host `postgres` → `UnknownHostException: postgres`. `render.yaml` already opts those vars out of blueprint sync (`sync: false`), so the Render dashboard values are authoritative; the blueprint cannot fix this by file edit alone.

### What must change

**Render dashboard (required, both services):**

| Service | Env var | Value to set in Render dashboard |
|---|---|---|
| `flashsale-payment-api-preview` | `SPRING_DATASOURCE_URL` | Full JDBC URL of the preview Render PostgreSQL instance, e.g. `jdbc:postgresql://<preview-pg-host>.oregion.render.com:5432/<preview-db>?sslmode=require` |
| | `SPRING_DATASOURCE_USERNAME` | Preview DB user (Render dashboard credentials) |
| | `SPRING_DATASOURCE_PASSWORD` | Preview DB password (Render dashboard credentials) |
| `flashsale-payment-api-prod` | `SPRING_DATASOURCE_URL` | Full JDBC URL of the production Render PostgreSQL instance |
| | `SPRING_DATASOURCE_USERNAME` | Production DB user |
| | `SPRING_DATASOURCE_PASSWORD` | Production DB password |

Do not use `jdbc:postgresql://postgres:5432/...` on Render. Preview and production must use separate Render Postgres instances/databases and separate credentials (see `docs/deployment/render-secrets-checklist.md`).

**Local `render.yaml` (optional, documentation only):**
- Keep `sync: false` on the three datasource vars — do not change to `sync: true` and do not add secret values to the file.
- Optional: add a comment block above each `SPRING_DATASOURCE_URL` entry noting the value must be set in the Render dashboard with the Render Postgres hostname, never `postgres`. No secret values may be added.

### Local vs cloud

| Aspect | Where | Access required |
|---|---|---|
| `render.yaml` comment/docs edit | Local file only | None |
| Setting `SPRING_DATASOURCE_URL`/`USERNAME`/`PASSWORD` | Render dashboard | Render account with valid, non-leaked credentials (do not use leaked `RENDER_DEPLOY_HOOK_*` values) |

Verification: after setting dashboard vars, trigger preview deploy, then check `/health/live` returns 200 and the readiness actuator includes `db` healthy. Promote to prod only after preview passes.

---

## 2. AKS order-worker CrashLoopBackOff — invalid Azure Service Bus connection

### Root cause

`infrastructure/terraform/main.tf` sets `SPRING_RABBITMQ_HOST` to `<azurerm_servicebus_namespace.orders.name>.servicebus.windows.net` for both `payment-api` (`main.tf:140`) and `payment-worker` (`main.tf:257`) — i.e. the Azure Service Bus namespace FQDN is configured as the RabbitMQ host. The worker then tries to speak the RabbitMQ AMQP dialect to a Service Bus endpoint and fails to connect → CrashLoopBackOff.

The in-cluster contract (this repo's AKS-style worker) expects RabbitMQ at the `rabbitmq` Service:
- `infrastructure/kubernetes/base/worker/deployment.yaml:52-56`: `ConnectionStrings__RabbitMQ` ← secret `flashsale-secrets` key `rabbitmq-connection`, format `amqp://<user>:<pw>@rabbitmq:5672/` (`README-secrets.md:37`)
- `infrastructure/kubernetes/base/data/rabbitmq.yaml:12-28`: in-cluster Service `rabbitmq` on port 5672

### What must correct

The `rabbitmq-connection` secret (or equivalent worker env) must point at the RabbitMQ broker, not the Service Bus namespace.

**Option A — keep RabbitMQ as the broker (matches this repo's worker manifests):**
- Set `flashsale-secrets` key `rabbitmq-connection` → `amqp://flashsale:<rabbitmq-password>@rabbitmq:5672/` (in-cluster Service `rabbitmq:5672`).
- Keep `Messaging__Provider=RabbitMQ` (`base/worker/deployment.yaml:69`).

**Option B — switch the worker to Azure Service Bus (production path per `docs/adr/005-messaging-provider-strategy.md`):**
- Add secret `sb-connection` (`ConnectionStrings__ServiceBus`) with the real Service Bus connection string `Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=...`.
- Set `Messaging__Provider=ServiceBus` and remove/misuse of `SPRING_RABBITMQ_HOST` as a Service Bus FQDN.

Do not set `SPRING_RABBITMQ_HOST` (or `rabbitmq-connection`) to `<sb-namespace>.servicebus.windows.net`.

### Exact references to check

| File | Lines | What to verify |
|---|---|---|
| `infrastructure/terraform/main.tf` | 139-153, 256-270 | `SPRING_RABBITMQ_HOST` must not be the Service Bus namespace FQDN |
| `infrastructure/terraform/main.tf` | 191-198, 286-293 | `sb-username`/`sb-password` secrets derived from Service Bus namespace — only for Service Bus provider |
| `infrastructure/terraform/outputs.tf` | 17-18 | `servicebus_namespace_name` output — do not reuse as RabbitMQ host |
| `infrastructure/kubernetes/base/worker/deployment.yaml` | 52-56, 69-71 | `ConnectionStrings__RabbitMQ` secret ref + `Messaging__Provider=RabbitMQ` |
| `infrastructure/kubernetes/base/data/rabbitmq.yaml` | 12-28 | In-cluster `rabbitmq` Service on port 5672 |
| `infrastructure/kubernetes/README-secrets.md` | 33-41 | Secret contract: `rabbitmq-connection` AMQP URI format |
| `docs/adr/005-messaging-provider-strategy.md` | — | `ConnectionStrings__ServiceBus` / `sb-connection` if switching provider |

### Local vs cloud

| Aspect | Where | Access required |
|---|---|---|
| `main.tf` env-var fix | Local file edit | None for edit; `terraform apply` requires Azure credentials and should only be used in a validated non-production environment |
| `flashsale-secrets` `rabbitmq-connection` correction | Cluster (`kubectl -n flash-sale-prod` or `flashsale`) | Cluster access with valid kubeconfig; secret material never committed |
| ArgoCD rollout of corrected worker image/manifest | Cluster | ArgoCD/cluster access |

---

## 3. ArgoCD `aks-sre-platform` Degraded, pinned to revision `7633cad`

### Current state in this repo

`infrastructure/argocd/flashsale-application.yaml` defines app `flashsale-production` with `targetRevision: main` — this app is NOT degraded and NOT pinned to `7633cad`. No change needed here.

The `aks-sre-platform` Application does not exist in this repo; it is managed in the separate AKS-SRE-Platform repository (PR #30 on imtarget05/AKS-SRE-Platform per project memory).

### Manifest location (to confirm in AKS-SRE-Platform repo)

Expected location: `infrastructure/argocd/apps/aks-sre-platform.yaml` or `argocd/aks-sre-platform.yaml` in the AKS-SRE-Platform repo. Confirm with:

```bash
argocd app get aks-sre-platform   # shows source.repoURL, targetRevision, path
argocd app list | grep aks-sre
```

Look for `targetRevision: 7633cad` (or equivalent) and change it to `targetRevision: main`.

### Sync command

After correcting `targetRevision` to `main` in the Application manifest:

```bash
argocd app set aks-sre-platform --revision main
argocd app sync aks-sre-platform
```

Alternative (imperative, no file edit):

```bash
argocd app sync aks-sre-platform --revision main
```

Verify:

```bash
argocd app get aks-sre-platform   # expect Healthy / Synced, revision main
kubectl -n argocd get application aks-sre-platform -o yaml
```

### Local vs cloud

| Aspect | Where | Access required |
|---|---|---|
| Manifest `targetRevision` edit | Local file in AKS-SRE-Platform repo | None for edit |
| `argocd app set` / `argocd app sync` | ArgoCD API / cluster | ArgoCD credentials + cluster access; target the `aks-sre-platform` app, not production workloads |

---

## 4. Local vs cloud summary per item

| Item | File edit possible purely local? | Cloud/cluster access + valid credentials required? | May use leaked credentials? |
|---|---|---|---|
| 1 Render `SPRING_DATASOURCE_URL` | Partial: `render.yaml` comment/docs only; no secret values | Yes — Render dashboard for actual env var values | No |
| 2 Worker RabbitMQ/Service Bus fix | Partial: `main.tf` env var; secret format docs | Yes — `kubectl` to set `flashsale-secrets`, or `terraform apply`/ACA update | No |
| 3 ArgoCD `aks-sre-platform` revision | Partial: manifest edit in AKS-SRE-Platform repo | Yes — `argocd` CLI against the cluster | No |

---

Never reuse leaked values: Render hook URLs, Cloudflare API token, GitHub PAT, Sonar token, Neon credentials. Rotate them before any dependent cloud operation.
