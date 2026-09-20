# Integrated Disaster Recovery Runbook (Phase 3C)

This runbook reconstructs the FlashSale system after a host-level or
environment-level loss using ONLY assets that live off-host plus versioned
code. It is ordered so that each step's output feeds the next, and every step
has a falsifiable PASS check.

## Preconditions

| Asset | Where it lives (NOT on the dead host) |
|---|---|
| Source code + compose definition | git (`FlashSale-Backend`) |
| PostgreSQL backup set (`.dump` + `.sha256` + `.metadata.json`) | Azure Blob `stflashsalebackup/postgres-backups` |
| RabbitMQ definitions (`.json` + `.sha256` + `.metadata.json`) | Azure Blob `stflashsalebackup/postgres-backups/rabbitmq/` |
| Terraform infrastructure definition | git (`infrastructure/terraform/backup-storage/`) |
| Terraform state (infrastructure control history) | Azure Storage backend `stflashs3ctfbk01/tfstate` |
| Runbooks themselves | git (`docs/runbooks/`, `docs/recovery/`) |

> Redis is deliberately absent: reservation counters are re-derived from
> PostgreSQL. Message *bodies* in flight are also absent: accepted-but-
> unpersisted orders are a documented business risk, not a recoverable asset
> (see Transactional Outbox, open decision).

## The recovery order (and why)

```text
1. Terraform backend      → without state you cannot manage storage
2. Storage account        → without the account there is nothing to download
3. PostgreSQL restore     → business source of truth first
4. RabbitMQ rebuild       → broker topology from definitions
5. Redis rebuild          → derived mirror reseeded from Postgres
6. Application smoke      → only now is the system "recovered"
```

Any other order wastes time: e.g. rebuilding the broker before the database is
restored lets the app accept orders against a database that does not exist yet.

## Step 1 — Recover Terraform state control

```bash
cd infrastructure/terraform/backup-storage
terraform init          # backend = azurerm (stflashs3ctfbk01/tfstate)
terraform plan          # expect: no changes (or only intended diffs)
```

PASS: plan succeeds against the remote state and shows no unexpected drift.
(Documented failure this protects against: local-only `terraform.tfstate`
destroyed with the host. Locking is proven by the lease-contention test in
`docs/evidence/backup/restore-drill-003-integrated.md`.)

## Step 2 — Reconstruct the storage foundation (only if destroyed)

```bash
terraform apply   # isolated root: storage account, containers, lifecycle, network rules, RBAC
./scripts/backup/bootstrap-immutability-policy.sh  # re-apply UNLOCKED 1-day WORM
az storage blob list -c postgres-backups --account-name stflashsalebackup \
  --auth-mode login --prefix postgres/ --query '[?ends_with(name, `.dump`)].name' -o tsv
```

PASS: latest backup set visible with `.dump` + `.sha256` + `.metadata.json`.
Note: if the storage account itself was destroyed, its contents are gone too —
this path assumes the account survived (LRS + firewall + soft delete protect
the *contents*, not the account). Account-level destruction is the remaining
scenario (see "What this runbook does NOT cover").

## Step 3 — Full PostgreSQL recovery (single command)

```bash
export POSTGRES_PASSWORD=... COMPOSE_PROJECT=flashsale-backend
./scripts/backup/run-full-recovery.sh <backup-base> [target-db]
```

This performs download → checksum → clean restore → verification against the
archived sidecar → app smoke. PASS criteria (exit 0 + all of):

```text
DOWNLOAD PASS … .dump verified
restored products=1, migrations + indexes match the archived set
live={healthy} ready={ready}
POST /api/orders → 202 accepted; stock decrements by exactly the ordered qty
source DB untouched (when the source still exists)
```

## Step 4 — RabbitMQ broker rebuild

```bash
export RABBITMQ_PASSWORD=guest   # local-dev default; prod uses Key Vault (Phase 7+)
./scripts/backup/rabbitmq-backup.sh    # export current definitions first (if broker alive)
./scripts/backup/rabbitmq-rebuild.sh [definitions.json]
```

PASS: empty-broker listing shows the expected durable queues (`orders, True`)
after import, and one end-to-end order is accepted then visible.
**Data note:** definitions ≠ messages. In-flight messages die with the broker;
only Postgres-persisted orders survive. If the business later requires zero
message loss, the answer is the Transactional Outbox decision, not broker backup.

## Step 5 — Redis rebuild

```bash
export POSTGRES_PASSWORD=...
./scripts/backup/redis-rebuild.sh
```

PASS: `REDIS-REBUILD PASS: Redis wiped and reseeded from PostgreSQL truth`
(product read matches DB stock afterwards).

## Step 6 — Business verification checklist

| Check | Command | Expected |
|---|---|---|
| Product catalog intact | `GET /api/products/1` | `iPhone 15 Pro Max`, stock = DB stock minus smoke qty |
| New order accepted | `POST /api/orders` | `202`, returns an `idempotencyKey` |
| Order persisted exactly once | `GET /api/orders/{key}` | `completed` (with worker) or `processing` (API-only drill) |
| Stock conserved | `GET /api/products/1` | decremented by exactly the smoke quantity |
| Unique idempotency enforced | duplicate INSERT | `IX_Orders_IdempotencyKey` violation |
| Source DB untouched | psql counts on `FlashSaleDb` | unchanged counts (when the source exists) |

## Timings to measure (record in the drill evidence)

`DOWNLOAD_SECONDS`, `RESTORE_SECONDS`, `VERIFY_SECONDS`,
`APP_STARTUP_SECONDS`, `SMOKE_SECONDS`, `TOTAL_RECOVERY_SECONDS`
(+ RabbitMQ export/import durations, Redis reseed duration).

## What this runbook does NOT cover (explicit non-goals)

1. **Cross-region failover.** Both the workload and the backup account are
   single-region (`southeastasia`, LRS). Region loss breaks this runbook;
   geo-redundancy is the Phase 10 subject.
2. **Message-body recovery.** Accepted-but-unpersisted RabbitMQ messages are not
   backed up and are lost on broker destruction (open outbox decision).
3. **Secret recovery.** The operator is assumed to know the DB password and hold
   Entra access with the data role. There is no sealed secret escrow yet.
4. **Application image rebuild from nothing.** `dotnet` build from source is
   assumed; immutable image promotion is the Phase 6 subject.
