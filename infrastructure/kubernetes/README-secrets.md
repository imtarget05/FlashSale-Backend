# Runtime secret contract — `flashsale-secrets`

The manifests in `base/` reference a Kubernetes `Secret` named
`flashsale-secrets`. **That Secret is intentionally not in this repository.**

## Ownership boundary

| Concern | Owner | Where |
|---|---|---|
| Application desired state (Deployments, Services, StatefulSets) | Git — this repo | `infrastructure/kubernetes/` |
| Runtime secret **material** | Out-of-band bootstrap | cluster, created by hand before Phase 7C |

This is deliberate bootstrap debt, not an oversight:

> Application desired state is GitOps-managed; runtime secret material is
> bootstrap-managed and intentionally excluded from Git pending external
> secret integration.

The planned replacement is Key Vault CSI (or External Secrets Operator) +
Workload Identity — already tracked in `tasks/current.md` ("Key Vault CSI
driver instead of Kubernetes secrets"). Until then, no claim is made that
"GitOps manages everything".

Committing a Secret, a SealedSecret with the key material inline, or an
`.env` file into this repo would defeat the Phase 5 Trivy secret scan and
expose the database. The `secret` scanner in `.github/workflows/ci.yml`
(`security-scan`) runs on the whole checkout specifically to catch that.

## Required keys

Namespace: `flash-sale-prod` (set by `overlays/prod`).

| Key | Consumed by | Format | Example (non-secret shape) |
|---|---|---|---|
| `pg-connection` | `order-api`, `order-worker` | Npgsql connection string | `Host=postgres;Port=5432;Database=FlashSaleDb;Username=postgres;Password=<pw>;Maximum Pool Size=80` |
| `redis-connection` | `order-api` | StackExchange.Redis endpoint | `redis:6379` |
| `rabbitmq-connection` | `order-api`, `order-worker` | AMQP URI | `amqp://<user>:<pw>@rabbitmq:5672/` |
| `jwt-signing-key` | `order-api` | HS256 signing key, **≥ 32 bytes** | `<random 32+ byte string>` |
| `pg-password` | `postgres` StatefulSet | bare password | `<pw>` |
| `rabbitmq-password` | `rabbitmq` StatefulSet | bare password | `<pw>` |

Key names come from the `secretKeyRef.key` fields in the manifests; the
`ConnectionStrings__*` variable names come from .NET configuration
(`GetConnectionString(...)`), where `__` maps to the `ConnectionStrings:`
section. `pg-password` and `rabbitmq-password` exist because the data tier is
deployed in-cluster for Phase 7C (see the header comments in
`base/data/*.yaml`) and must agree with the connection strings above.

`jwt-signing-key` backs `Auth__Jwt__SigningKey` (ADR-013 §2). It is required by
`order-api` **and deliberately absent from `order-migrate`**: `JwtOptions`
fails fast when the key is missing, so the manifest must not ask the migration
Job for a signing key it should never need. Rotating this key invalidates every
issued access token and refresh token, which is the intended blast radius.

## Create it (Phase 7C bootstrap)

Run against the cluster, from a shell where the real values live — **never**
from a committed script, and never paste the command with real values into an
issue, PR, or evidence file:

```bash
kubectl -n flash-sale-prod create secret generic flashsale-secrets \
  --from-literal=pg-connection="Host=postgres;Port=5432;Database=FlashSaleDb;Username=postgres;Password=$PG_PW;Maximum Pool Size=80" \
  --from-literal=redis-connection="redis:6379" \
  --from-literal=rabbitmq-connection="amqp://flashsale:$RBQ_PW@rabbitmq:5672/" \
  --from-literal=jwt-signing-key="$JWT_KEY" \
  --from-literal=pg-password="$PG_PW" \
  --from-literal=rabbitmq-password="$RBQ_PW"
```

The namespace must exist first:

```bash
kubectl create namespace flash-sale-prod --dry-run=client -o yaml | kubectl apply -f -
```

## Verify before deploying

```bash
kubectl -n flash-sale-prod get secret flashsale-secrets \
  -o jsonpath='{.data}' | tr ',' '\n' | sed 's/:.*//'   # key names only, never values
```

Expected: the five keys listed above. Reading values back out requires
base64-decoding and should not be part of routine evidence gathering.

## Why the provider must be set explicitly

`Messaging__Provider` is a plain env var (not a secret) pinned to `RabbitMQ` in
both app manifests. Without it, `MessagingProviderSelector.Resolve()` falls
back to auto-detection and returns **InMemory** when no broker connection
string is present. `InMemoryOrderQueue` is per-process, so the API and the
worker would each hold their own private queue and never exchange a single
message — no error, no crash, and no completed order. That silent fallback is
the failure mode this document and `scripts/validate-manifests.sh` guard
against.
