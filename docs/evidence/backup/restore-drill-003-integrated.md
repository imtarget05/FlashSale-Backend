# Restore Drill 003 — Integrated recovery: full automation + broker + cache + state

Phase: 3C (business continuity). Predecessors: drill-001 (Phase 3A, local
restore), drill-002 (Phase 3B, off-host restore + hardening addendum).
Runbook under test: `docs/runbooks/disaster-recovery.md`.

## Objective

Execute the integrated runbook in order and record every step: (1) RabbitMQ
definitions off-host backup, (2) broker rebuild from wiped volume + end-to-end
order, (3) Redis rebuild from PostgreSQL truth, (4) Terraform state remote
backend + locking proof, (5) single-command full PostgreSQL recovery
(`run-full-recovery.sh`), (6) measured timings.

## Environment before the drill

- Compose stack `flashsale-backend`: postgres + redis up; rabbitmq restarted
  after a Docker Desktop outage (container exited 137 → `docker start`).
- Source DB at backup-set time (`flashsale_20260920T145146Z`): products=1,
  `iPhone 15 Pro Max`, stock=100, orders=0 — the seeder from that era.
  (The working tree now also contains an *uncommitted* richer product seeder;
  it was NOT applied to the database under test, and all drill DBs were
  created from the archived dump, so drill comparisons are unaffected.
  Committing or reverting that seeder is out of scope for Phase 3.)
- Live DB has accepted test orders since the backup — point-in-time semantics apply.
- Azure: `stflashsalebackup` (postgres-backups + 1-day UNLOCKED WORM) and, as of
  this drill, `stflashs3ctfbk01/tfstate` (remote encrypted state backend).

## 1) RabbitMQ definitions backup (off-host)

```bash
export RABBITMQ_PASSWORD=guest
./scripts/backup/rabbitmq-backup.sh
```

```
==> exporting RabbitMQ definitions from localhost:15672
broker: 3.13.7
queues=1 exchanges=8 bindings=8 users=1 vhosts=1 policies=0
==> uploading definitions to stflashsalebackup/postgres-backups/rabbitmq/20260920T153630Z/ (Entra auth)
{name: rabbitmq/20260920T153630Z/rabbitmq-defs_20260920T153630Z.json, size: 789, version: ...T15:36:31...Z}
{name: rabbitmq/20260920T153630Z/rabbitmq-defs_20260920T153630Z.sha256, size: 102, version: ...}
{name: rabbitmq/20260920T153630Z/rabbitmq-defs_20260920T153630Z.metadata.json, size: 261, version: ...}
RABBITMQ-BACKUP PASS: rabbitmq/20260920T153630Z/rabbitmq-defs_20260920T153630Z.json (+ .sha256, .metadata.json)
```

PASS: `.json` (789 B, queue `orders` durable) + sidecars verified in Azure.
Script fixes found during the drill: the default definitions-file glob matched
`*.metadata.json` (fixed with a timestamped glob excluding metadata), and the
management API returns **204** (not 200) on successful import — both are
documented as real API behaviour, not guesses.

## 2) Broker rebuild drill (wipe volume → empty broker → import)

```bash
export RABBITMQ_PASSWORD=guest COMPOSE_PROJECT=flashsale-backend
./scripts/backup/rabbitmq-rebuild.sh
```

```
==> stopping broker flashsale-backend-rabbitmq-1 and wiping its data volume
wiped volume: flashsale-backend_rabbitmq-data
==> recreating EMPTY broker via compose
empty broker queues: 0
==> importing definitions .../backups/rabbitmq-defs_20260920T153630Z.json
queues after import: [('orders', True)]
REBUILD PASS: empty broker + definitions import healthy
```

End-to-end order through the rebuilt broker (API started against `FlashSaleDb`,
Redis up, `Messaging__Provider` default):

```text
POST /api/orders → 202 {"message":"Order accepted","status":"processing"}
GET  /api/orders/{key} → {"status":"processing"}
GET  /api/products/1 → availableStock 99        (was 100 before the order)
queues: [('orders', True, 0)]
```

PASS: definitions restored the durable `orders` queue and one real order flowed
through the rebuilt broker with correct stock decrement.
**Data honesty:** in-flight message bodies were never backed up and cannot be
recovered — only topology + Postgres-persisted orders survive. That is the
recorded Transactional Outbox decision, not a drill failure.

## 3) Redis rebuild drill

Two layers were proven. First, the product-level behaviour with Redis
deliberately stopped: an order still succeeded via the synchronous Postgres
fallback (`{"message":"Order placed successfully"}`), confirming the Task-2
design that Redis is performance state, not truth.

Second, the scripted drill wiped the mirror and reseeded it:

```bash
export POSTGRES_PASSWORD=...
./scripts/backup/redis-rebuild.sh
```

```
==> DB-side truth before drill
==> wiping Redis derived state (FLUSHDB)
OK
keys after wipe: 0
==> reseeding via /internal/resync-stock/1
{"id":1,"resyncedTo":98}
==> validating rebuilt mirror
product: {"id":1,"name":"iPhone 15 Pro Max","availableStock":98}
REDIS-REBUILD PASS: Redis wiped and reseeded from PostgreSQL truth
```

PASS: FLUSHDB emptied the store (0 keys), the resync runbook reseeded the
counter from `AvailableStock`, and the product read matched DB truth (98).

## 4) Terraform state: remote encrypted backend + locking

Provisioned (`bootstrap-tfstate-backend.sh`): resource group
`rg-flashsale-tfstate`, account `stflashs3ctfbk01` (StorageV2, TLS1.2,
public blob access disabled, firewall `Deny` + operator IP, RBAC
`Storage Blob Data Contributor`), container `tfstate`.

Migrated the real local state (7 resources, serial 12; a backup copy was kept
at `/tmp/tfstate-pre-migration.backup`) by adding `backend "azurerm"`
(`use_azuread_auth = true`) and answering `yes` to `init -migrate-state`:

```text
post-migration plan: TRUE_EXIT=0 — "no differences, so no changes are needed"
remote state object: tfstate/backup-storage.terraform.tfstate (21806 B)
```

Locking proof (deliberate contention: two `plan` processes, one holding the
lease, the second with `-lock-timeout=8s`):

```text
LOCK_TEST_EXIT=1
Acquiring state lock. This may take a few moments...
Error: Error acquiring the state lock
Error message: state blob is already locked
Lock Info: ...
```

PASS: a second concurrent operation fails fast with a lease error instead of
silently corrupting state. Afterwards the refresh-only apply synced the
output-only delta and `plan` again reports no changes (state converged).

Remaining: the state-storage account itself uses Shared Key auth for the
*backend* path only (azurerm backend requirement in this provider version),
while the backup account stays Shared-Key-disabled. That asymmetry is recorded
in ADR-009's successor note below.

## 5) Single-command full recovery (`run-full-recovery.sh`)

```bash
export POSTGRES_PASSWORD=... COMPOSE_PROJECT=flashsale-backend
./scripts/backup/run-full-recovery.sh flashsale_20260920T145146Z
```

Local original for that set was already quarantined in Phase 3B, so the script's
step-0 guard (refuse when a local copy exists) held and the drill provably used
**Azure only**:

```text
== [1/6] Azure download ==
DOWNLOAD PASS … flashsale_20260920T145146Z.dump verified
DOWNLOAD_SECONDS=6
== [2/6] clean restore (target: FlashSaleRecovery3C) ==
RESTORE_SECONDS=0
== [3/6] verification (source-vs-restored) ==
archive sidecar: size_bytes=5102 migrations=20260920123806_InitialCreate
restored: products=1 orders=0 stock(id1)=100 migrations=20260920123806_InitialCreate
          indexes=IX_Orders_IdempotencyKey,PK_Orders
live source now: products=1 orders=2 (orders/stock differ by design if post-backup orders exist)
VERIFY_SECONDS=1
== [4/6] application smoke on restored DB ==
live={"status":"healthy"} ready={"status":"ready"}
before={"id":1,...,"availableStock":100}
create={"message":"Order accepted",...,"status":"processing"}
after ={"id":1,...,"availableStock":99}
== [5/6] source DB untouched (read-only check) ==
== [6/6] FULL RECOVERY PASS: Azure → FlashSaleRecovery3C → app smoke OK ==
```

**Semantic correction recorded here:** the first version of step 3 compared the
restored snapshot against the *live* source DB and failed with "mismatch"
(live source had accepted 2 test orders since the backup: orders 2 vs 0,
stock 98 vs 100). That comparison is methodologically wrong: a point-in-time
restore must match the **archived sidecar** (size 5102, migration id, indexes),
*not* a database that moved on. The script was fixed to compare restored rows
against the sidecar plus schema (indexes/migrations) against the live source,
and the misleading FAIL disappeared. The smoke order decremented 100 → 99,
proving the app works on the restored copy.

## 6) Timings measured in this drill

| Operation | Measured |
|---|---|
| RabbitMQ definitions export + Azure upload | ~3 s (789 B artifact) |
| Broker wipe → empty recreate → import | ~35 s (container recreate + health wait) |
| Redis FLUSHDB → resync → validate | ~4 s |
| Terraform migrate-state + converged plan | init ~20 s (one-time), plan ~15 s |
| State-lock contention fail-fast | immediate lease error, exit 1 |
| Full Postgres recovery (download→smoke) | DOWNLOAD 6 s / RESTORE 0 s / VERIFY 1 s / STARTUP+SMOKE ~25 s |

Scope reminder: these are procedure timings on 5 KB artifacts and a warm host,
not an enterprise RTO. The RTO claim stays "reference target ≤ 2h; measured
procedure ≈ tens of seconds at this scale".

## 7) What remains open after 3C

1. Accepted-but-unpersisted message loss (Transactional Outbox — business decision).
2. Region failure (single region + LRS throughout).
3. Storage-account-level destruction (contents protected, account itself not replicated).
4. Secret escrow (DB password + Entra access assumed known).
5. Two incomplete WORM-held debug sets in `postgres-backups` (from 3B) expire out
   of their 1-day window and become deletable — see cleanup runbook.
6. The 3A + scheduled local staging sets still in `backups/` should be pruned
   once their Azure copies are confirmed old enough to matter (they cost KBs;
   no urgency, but the backup-policy pruning rule should be followed).

## Evidence locations (3C additions)

- `scripts/backup/rabbitmq-backup.sh`, `rabbitmq-rebuild.sh`, `redis-rebuild.sh`,
  `run-full-recovery.sh`
- `infrastructure/terraform/backup-storage/bootstrap-tfstate-backend.sh`,
  `versions.tf` (backend block)
- `docs/runbooks/disaster-recovery.md` (the integrated runbook)
- Azure: `rabbitmq/20260920T153630Z/*` in `stflashsalebackup/postgres-backups`;
  `tfstate/backup-storage.terraform.tfstate` in `stflashs3ctfbk01`;
  role assignments on both scopes; firewall rules on both accounts.
