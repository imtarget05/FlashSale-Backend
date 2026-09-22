# Phase 7C readiness — P01 manifest audit & remediation (2026-09-21)

Scope: **offline only.** No cluster was contacted for this work; `aks-portfolio-dev`
is `Stopped` and quota is `0/10`. Every claim below was executed locally and its
output is reproduced here. Nothing is inferred from CI status or from documents.

## Why this audit existed

Phase 6 released `order-api` and `order-worker` successfully: images built,
Trivy-clean, pushed to ACR with SHA tags, digest-verified, GitOps commit pushed,
CI green. The Phase 7A cross-check then rendered the prod overlay and found it
emitting

```text
acrflashsale-placeholder.azurecr.io/order-api:latest
```

i.e. a registry that does not exist and a mutable tag that ADR-011 forbids.
**Every green check in the pipeline proved the image. None of them proved the
desired state.** That distinction is the whole finding.

## Findings and fixes

### 1. Kustomize image override silently skipped (was a hard blocker)

`overlays/prod/kustomization.yaml` declared

```yaml
images:
  - name: acrflashsalep6.azurecr.io/order-api   # never matched base
    newTag: <sha>
```

while `base/deployment.yaml` declared `image: acrflashsale-placeholder.azurecr.io/order-api:latest`.
Kustomize matches `images[].name` against the image **name** in the base; on a
miss it skips the transform without warning. Neither string matched the other.

Fix — registry-neutral name in base, registry supplied by the overlay:

```yaml
# base/api/deployment.yaml
image: order-api:placeholder-set-by-overlay
# overlays/prod/kustomization.yaml
- name: order-api
  newName: acrflashsalep6.azurecr.io/order-api
  newTag: 087731cd0ed6d2aeb5892dd3a8fd6f6da7c0339c
```

Rendered output after the fix (9 objects):

```text
acrflashsalep6.azurecr.io/order-api:087731cd0ed6d2aeb5892dd3a8fd6f6da7c0339c
acrflashsalep6.azurecr.io/order-worker:087731cd0ed6d2aeb5892dd3a8fd6f6da7c0339c
```

Both tags were confirmed to exist in ACR (not assumed):

```text
$ az acr repository show-tags -n acrflashsalep6 --repository order-api    | grep 087731cd  -> 1
$ az acr repository show-tags -n acrflashsalep6 --repository order-worker | grep 087731cd  -> 1
$ az acr show -n acrflashsalep6 --query adminUserEnabled                  -> false
```

Private pull remains the kubelet identity's `AcrPull` role (verified in 7A.2),
so no imagePullSecret is required.

### 2. `order-worker` had no manifest at all (was a hard blocker)

CI built, pushed and digest-verified the worker image for four releases, but the
repo contained exactly one Deployment. The async fulfillment flow (ADR-004)
cannot run without the consumer.

Added `base/worker/deployment.yaml`: 1 replica, no `containerPort` and no
`Service` (a queue consumer has no inbound surface), non-root uid 1654 matching
`Dockerfile.worker`, `readOnlyRootFilesystem`, PG + RabbitMQ connection strings,
and **no HTTP health probe** — `Order.Worker` is a `BackgroundService` host with
no listener, so a fabricated `/health` check would be a false claim. Its real
liveness signal at 7C is "the queue drains and the order row reaches Completed".

### 3. API manifest never received a RabbitMQ connection string (new finding)

Not in the original list. `base/deployment.yaml` mounted only `pg-connection`
and `redis-connection`. `MessagingProviderSelector.Resolve()`
(`FlashSale.Application/Messaging/MessagingProvider.cs`) falls through to
**`InMemory`** when no broker connection string is present, and
`InMemoryOrderQueue` is per-process. So even after adding the worker, the API
would enqueue into its own memory and the worker would wait forever on a queue
that does not exist — no error, no crash, no completed order.

Fix: `ConnectionStrings__RabbitMQ` added to both app containers, plus
`Messaging__Provider=RabbitMQ` set **explicitly** so a missing secret fails
loudly at DI resolution instead of degrading to a broken topology.

### 4. Secret material absent from Git (by design, documented)

`flashsale-secrets` is referenced by four workloads but defined nowhere. For 7C
this is resolved as **bootstrap-managed**, not by pulling Key Vault CSI forward
(which would turn a manifest fix into a new security phase).

`infrastructure/kubernetes/README-secrets.md` now specifies the contract: five
keys (`pg-connection`, `pg-password`, `redis-connection`, `rabbitmq-connection`,
`rabbitmq-password`), their consumers, value shapes, and the ordering
constraint (Secret must exist before workloads apply, else
`CreateContainerConfigError` looks like an app bug). Claim kept honest:

> Application desired state is GitOps-managed; runtime secret material is
> bootstrap-managed and intentionally excluded from Git pending external
> secret integration.

Trivy secret scan on the new files: `No issues detected`.

### 5. Data tier: in-cluster, not Azure managed services

`tasks/current.md` claimed PostgreSQL/Redis/Service Bus/Container Apps were
delivered. Live check (`az resource list`) shows the only P01-adjacent resources
are `acrflashsalep6`, `id-flashsale-github-release`, the backup storage account
and the P03 AKS stack. The blueprint in `infrastructure/terraform/main.tf` was
never applied and has no state. Corrected in place — history kept, claim
downgraded to "blueprint validates; not applied", plus the P01/P03 ownership
boundary. The stale blanket freeze ("do not work on Terraform, Azure") was
replaced by that boundary because it contradicted the Phase 7 roadmap.

7C therefore runs **portfolio mode**: Postgres (StatefulSet + PVC, source of
truth), Redis (Deployment, no PVC — derived cache per ADR-003), RabbitMQ
(StatefulSet + PVC, so acked-but-unprocessed messages survive a restart). All
ClusterIP, no `type: LoadBalancer`, management port 15672 deliberately not
fronted. Total CPU requests: **0.8 cores** against the 8-vCPU system pool.

### 6. `readOnlyRootFilesystem` was unproven for the app containers (new finding)

The manifest set declares `readOnlyRootFilesystem: true` everywhere, and this
tree has never run on a cluster. Pulled the released images and tested the exact
security posture on `linux/amd64`:

```text
$ docker run --rm --read-only --user 1654:1654 --entrypoint sh <order-worker:087731cd> \
    -c 'touch /tmp/p; touch /app/logs/p'
touch: cannot touch '/tmp/p': Read-only file system
touch: cannot touch '/app/logs/p': Read-only file system
```

`/app/logs` is covered by the existing `dlq-logs` emptyDir (K8s makes emptyDirs
world-writable, unlike the image's baked-in directory). `/tmp` was **not**
covered, and .NET's `Path.GetTempPath()` resolves to `/tmp` on Linux. Added a
`tmp` emptyDir to both app containers. The API also boots to the DB connection
attempt under this posture (`FS_WRITE_OK`, then `Npgsql...Connection refused`
against a deliberately absent DB), which confirms the root filesystem is not the
blocker.

Data-tier uids were verified the same way rather than guessed — the images start
as root and drop privileges via `gosu`/`su-exec`, so a wrong uid means a
crashloop:

| Image | uid:gid proven | Extra mounts required for read-only root |
|---|---|---|
| `postgres:15-alpine` | `70:70` | `/var/run/postgresql`, `/tmp` (initdb + socket) |
| `redis:7-alpine` | `999:1000` | none (`PING`/`SET`/`GET`/`DECR` all pass read-only) |
| `rabbitmq:3-management-alpine` | `100:101` | `/tmp`; `rabbitmqadmin` declare/publish/get all pass |

## The gate that prevents a repeat

Three scripts, all runnable on a laptop with no cluster and no credentials:

* `scripts/validate-manifests.sh` — the entry point. Renders the overlay and
  asserts on the **output**: no `placeholder`, no `:latest`, both ACR images
  pinned to a 40-char SHA, both Deployments present, data tier present, no
  `LoadBalancer`, no management port, per-container env/secret wiring, and the
  worker exposing neither a port nor a Service.
* `scripts/validate-objects.py` — decodes the rendered objects and
  cross-references them (volumeMounts vs declared volumes, selector vs pod
  labels, `StatefulSet.serviceName` vs existing Services, secret keys vs the
  documented contract, resource requests/limits, namespace set, duplicates).
  Pure Python + PyYAML: no kubeconfig, no API discovery, no network.
* `scripts/gitops-pin-overlay.py` — replaces the inline CI heredoc. Pins **both**
  images, and exits non-zero if either pin did not land, instead of writing a
  half-updated file.

Wired into CI as `manifest-validation` (added to the `ci-gate` aggregate, so it
blocks PRs, not just releases) and as a pre-commit step inside `gitops-update`
(so a bad pin can never reach `main` for ArgoCD to sync).


### Negative tests — proving the gate can fail

A check that cannot fail manufactures false confidence. Each mutation was applied
to the real files, the gate run, then reverted:

| # | Injected fault | Gate result |
|---|---|---|
| 1 | Restore the original bug (overlay `name` carries registry, no `newName`) | FAIL: contains `placeholder`; order-api not pinned |
| 2 | `newTag: latest` | FAIL: mutable tag; order-api not pinned |
| 3 | Remove `worker/deployment.yaml` from base | FAIL: no `Deployment/order-worker` |
| 4 | Postgres `Service` → `type: LoadBalancer` | FAIL: public exposure |
| 5 | Drop the RabbitMQ secret ref from **API only** | FAIL: order-api missing `rabbitmq-connection` |

`scripts/validate-objects.py` was held to the same bar — 9 faults injected into
the rendered object set, each reverted after the run:

| Injected fault | Caught | Failure reported |
|---|---|---|
| mount an undeclared volume | yes | `mounts undeclared volume 'ghost'` |
| wrong secret key name | yes | `Secret 'flashsale-secrets' has no key 'wrong'` |
| reference a non-existent Secret | yes | `unknown Secret 'nope'` |
| selector does not match pod labels | yes | `selector {...} does not match pod labels` |
| drop `resources.limits` from postgres | yes | `resources.limits.cpu required` |
| postgres Service → `LoadBalancer` | yes | `type LoadBalancer provisions a public IP` |
| remove `namespace` from a Deployment | yes | `no namespace -- the overlay must set one` |
| `StatefulSet.serviceName` → missing Service | yes | `serviceName 'nope' does not exist` |
| declare the same object twice | yes | `declared more than once` |

Result: **9/9 caught**, and the unmutated file passes.

Iteration note: the first version of assertion 5 used a repo-wide
`grep 'key: rabbitmq-connection'` and **passed** with the API env var deleted,
because the worker also references that key. That false pass is why the check is
now per-document (`doc_of`), and why test 5 exists at all.

Also caught during development: `gitops-pin-overlay.py` initially rewrote
`newTag` without its key prefix, producing bare `<sha>` lines. Its own
"expected 2 pins" assertion refused to write the file — the validation earned its
keep immediately. GitOps script behaviour after the fix: idempotent (identical
hash on re-run), rejects a non-40-hex SHA, rejects a registry containing a
scheme or path, rejects an overlay missing an entry, and never leaves a
partially updated file behind.

## Known limitations (not claimed as done)

* **No cluster-side verification.** Everything above is client-side. Schema
  validation against real CRDs/OpenAPI, server-side dry-run, actual scheduling,
  PVC provisioning and a real order round-trip all require the cluster to be
  running — Phase 7C, gated on the 16/16 quota approval.
* **`kubectl --dry-run=client` is not an offline check, and was removed for that
  reason.** Both `apply` and `create --validate=false` still perform API
  discovery (GET /api) so kubectl can map Kind → GVR. Verified on this machine:
  with the repo's kubeconfig pointing at the stopped cluster,
  `kubectl create --dry-run=client --validate=false` exits **1**
  (`unable to recognize ...: dial tcp: lookup aksportfoliodev-...: no such
  host`), while `validate-manifests.sh` passes. An earlier revision of this
  script used that kubectl form and reported PASS — only because kubectl was
  serving a cached discovery document. The gate therefore flipped from PASS to
  FAIL with no change to any manifest, which is precisely the non-determinism
  this whole audit is meant to eliminate. Object validation is now pure Python.
* Images are `linux/amd64` only. Local verification needed `--platform
  linux/amd64` emulation; AKS nodes are amd64, so this is not a production issue.
* `commonLabels` is deprecated by Kustomize (warning, not error). Left alone:
  migrating to `labels` changes selector semantics and deserves its own tested
  change, not a drive-by edit.
* Worker has no liveness/readiness probe by design; its 7C signal is business
  level (queue drains, order reaches Completed).
* No ADR yet for the in-cluster data-tier decision. Reasoning currently lives in
  manifest headers and `tasks/current.md`; ADR-012 is the tidy follow-up.
* This work is **uncommitted**, because `FlashSale-Backend` has unrelated
  in-flight changes from another session (`Product.cs`, EF migration
  `AddRealisticProductProperties`, ADR-010, README). Do not sweep them in.

## Files changed

```text
infrastructure/kubernetes/base/api/deployment.yaml     was base/deployment.yaml: image name, RabbitMQ env, tmp volume
infrastructure/kubernetes/base/api/service.yaml        moved, unchanged
infrastructure/kubernetes/base/worker/deployment.yaml  NEW
infrastructure/kubernetes/base/data/postgres.yaml      NEW
infrastructure/kubernetes/base/data/redis.yaml         NEW
infrastructure/kubernetes/base/data/rabbitmq.yaml      NEW
infrastructure/kubernetes/base/kustomization.yaml      6 resources
infrastructure/kubernetes/overlays/prod/kustomization.yaml  newName + worker pin
infrastructure/kubernetes/README-secrets.md            NEW (contract, no values)
scripts/validate-manifests.sh                          NEW
scripts/validate-objects.py                            NEW
scripts/gitops-pin-overlay.py                          NEW
.github/workflows/ci.yml                               manifest-validation job; gitops-update uses the scripts
tasks/current.md                                       claims corrected; ownership boundary
.gitignore                                             __pycache__/
```

## Definition of done for "P01 ready for 7C"

Offline — **all verified in this session**: real registry rendered, no `:latest`,
no placeholder, both workloads present, both images SHA-pinned and confirmed
present in ACR, CI validates rendered output, secret contract documented, no
secret committed, data tier present and ClusterIP-only, nothing exposed
publicly, requests/limits on all five workloads, probes wherever an HTTP surface
exists, kustomize renders, all 9 objects cross-reference cleanly, Trivy HIGH+
clean, and the whole gate passes with a dead, empty or missing kubeconfig.

Online — **open, requires a running cluster**: server-side dry-run, namespace +
`flashsale-secrets` created, all pods Ready, order round-trip `202 → Completed`
with the worker consuming from RabbitMQ, and the oversell guard still holding
under the k6 harness.


