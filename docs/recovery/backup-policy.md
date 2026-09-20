# Backup Policy (P01 FlashSale) — Phase 3 CLOSED (3A + 3B + 3C)

> Phase 3 is formally CLOSED. What follows is the standing policy: what is
> proven, what runs on a schedule, and what is explicitly still open.
> Evidence: `docs/evidence/backup/restore-drill-001.md`,
> `restore-drill-002-offsite.md`, `restore-drill-003-integrated.md`.

## Principles
- **PostgreSQL is the business source of truth.** Its backup is the only
  business-critical protection. Everything else is reconstructable or Git.
- **A backup that has never been restored is not a backup** — Phase 3A gates
  on a completed clean-target restore drill with evidence.
- **A backup stored on the machine it protects is not a backup** — Phase 3B
  gates on an off-host copy plus a drill that recovers while the local copy is
  quarantined.
- Volume persistence (`postgres-data`) ≠ backup: one host, one copy, destroyed
  by `down -v`, disk loss, or laptop loss.

## Reference targets (REFERENCE PORTFOLIO TARGET)
| Target | Value | Status |
|---|---|---|
| RPO | ≤ 24h | **CONDITIONAL** — daily launchd schedule exists and is evidenced, but the guarantee holds only while the scheduler host is powered on/awake |
| RTO | ≤ 2h | drill-002 measured off-host recovery (download → restore → verified app smoke) = **20 s**; well inside target for this data size, but this is one drill on a warm host, not a certified enterprise RTO |

## Current implementation (Phase 3A — local recovery, DONE)
- `scripts/backup/postgres-backup.sh` — `pg_dump -Fc` inside the compose
  postgres container → `backups/flashsale_<UTC>.dump` + `.sha256` +
  `.metadata.json` (version, size, migrations; no secrets). Password from env.
- `scripts/backup/verify-backup.sh` — checksum + `pg_restore --list` + expected
  tables (`Products`, `Orders`, `__EFMigrationsHistory`).
- `scripts/backup/postgres-restore.sh` — refuses live `FlashSaleDb` target
  (override requires `ALLOW_OVERWRITE_SOURCE=yes`), creates a NEW empty DB,
  restores, fails non-zero on invalid archive.

## Current implementation (Phase 3B — off-host protected backup, DONE)
- **Azure Blob Storage** `stflashsalebackup` (StorageV2, Standard_LRS,
  TLS 1.2, public blob access disabled, **Shared Key authorization disabled**),
  container `postgres-backups` (private). Entra ID + `Storage Blob Data
  Contributor` (storage-account scope) for access; `--auth-mode login`
  everywhere; no keys/SAS in scripts.
- **Protection**: blob versioning ON, blob soft delete 14d, container soft
  delete 14d, network firewall `default_action=Deny` + operator IP allowlist,
  time-based immutability (WORM) **1 day, UNLOCKED**, lifecycle
  `backup-retention` (current blobs 30d, versions/snapshots 90d).
- `scripts/backup/azure-upload-backup.sh` / `azure-download-backup.sh` —
  authenticated upload/download of `.dump` + `.sha256` + `.metadata.json` to
  immutable timestamped paths `postgres/YYYY/MM/DD/` (no `latest.dump` object).
- `scripts/backup/run-offsite-backup.sh` — orchestrates backup → validate →
  upload → remote verification; the local staging copy is deliberately NOT
  deleted automatically.
- **Scheduling**: `scripts/backup/scheduler/` (launchd, daily 02:30, installed
  via `install-offsite-scheduler.sh --daily`); two independently timestamped
  scheduled sets were produced and verified in Azure during the drill. After a
  workstation IP change, update `infrastructure/terraform/backup-storage/terraform.tfvars`
  (`allowed_ip_rules`) and re-apply, or scheduled uploads fail with 403 —
  see `docs/runbooks/offsite-backup-scheduling.md` (troubleshooting).
- **Postgres off-site cadence**: the daily `run-offsite-backup.sh` schedule
  covers only PostgreSQL. RabbitMQ definitions ride along only when the broker
  topology changes (operator-run `rabbitmq-backup.sh`); Redis needs no cadence
  because it is re-derived. This is the Phase-3 RPO contract — see below.

## Phase 3 closure statement

Phase 3 (3A local restore, 3B Azure off-host, 3C integrated recovery) is
**CLOSED** when all of the following hold — and they now do:

```text
backup thật ──▶ Azure Blob ──▶ quarantine local ──▶ download ──▶
checksum OK ──▶ clean DB ──▶ restore ──▶ verification ──▶ app smoke       PASS (3A, 3B, 3C drill)
protection drills (versioning / soft delete / container restore / WORM)   PASS (3B)
RabbitMQ definitions off-host + broker rebuild + order through it         PASS (3C)
Redis wipe + reseed from Postgres truth                                   PASS (3C)
Terraform state remote + encrypted + lease-locked (contention-tested)     PASS (3C)
integrated runbook with falsifiable checks + non-goals                    DONE (3C)
```

The RPO contract as of closure:

| Data | Mechanism | Achievable RPO |
|---|---|---|
| PostgreSQL (business truth) | daily off-site `run-offsite-backup.sh` (conditional on host awake — same caveat as 3B) | ≤ 24h conditional |
| RabbitMQ definitions (topology) | manual `rabbitmq-backup.sh` after topology change | point-in-time of last export |
| In-flight messages | NOT backed up (open outbox decision) | **no RPO — accepted loss** |
| Redis counters | re-derived (no backup) | 0 (reconstructable) |
| Terraform state | remote backend, every apply recorded | immediate |

Remaining risks accepted at closure: transactional-outbox decision still open,
single-region/LRS, storage-account-level destruction, no secret escrow — all
assigned to Phase 10 (DR) or later, none of them reopens Phase 3.
- **Drills proven**: versioning (two versions of the same key, previous version
  retrievable), blob/version deletion recoverable (data retrieved by versionId),
  container soft delete (`Deleted=true`, `RemainingRetentionDays=14` → restored
  with HTTP 201), WORM (delete **and** overwrite denied with
  `BlobImmutableDueToPolicy`), and a full local-loss recovery
  (`docs/evidence/backup/restore-drill-002-offsite.md`).
- **RabbitMQ definitions**: `scripts/backup/rabbitmq-backup.sh` exports broker
  definitions (queues/exchanges/bindings/users/vhosts/policies) to the same
  off-host account under `rabbitmq/<UTC>/` (+ `.sha256`, `.metadata.json`).
  `scripts/backup/rabbitmq-rebuild.sh` wipes the broker volume, recreates an
  empty broker and imports definitions — proven with a durable `orders` queue
  restored and a real order flowing through (`docs/evidence/backup/restore-drill-003-integrated.md`).
  Definitions ≠ message bodies: in-flight messages are not recoverable.
- **Redis rebuild**: `scripts/backup/redis-rebuild.sh` wipes the mirror
  (FLUSHDB) and reseeds via `POST /internal/resync-stock/{id}` from PostgreSQL
  truth — proven (`REDIS-REBUILD PASS`). No Redis persistence backup exists or
  is planned (design decision, ADR-003).
- **Recovery automation**: `scripts/backup/run-full-recovery.sh` runs the whole
  chain in one command (download → checksum → clean restore → sidecar-based
  verification → app smoke) and refuses to run while the local original still
  exists, so it proves Azure-only recovery by construction.
- **Terraform state protection**: the `backup-storage` root's real state (moved
  from local-only) now lives in the remote encrypted backend
  `stflashs3ctfbk01/tfstate` (`backend "azurerm"`, Entra auth) with native blob
  **lease locking** — proven by a deliberate contention test (second operation
  fails fast with `state blob is already locked`).
- **Integrated runbook**: `docs/runbooks/disaster-recovery.md` orders the
  reconstruction (state → storage → Postgres → RabbitMQ → Redis → smoke) with
  falsifiable PASS checks and an explicit non-goals list.

## ENTERPRISE REFERENCE MODE (documented, NOT implemented — do not claim)
- Redundancy from a business requirement: ZRS/GZRS plus a *tested* region
  failover (see ADR-008); LRS in Portfolio Mode protects only one datacenter.
- Locked immutable policy (compliance WORM) after validation, longer retention,
  legal hold where regulation requires, plus a change-controlled process for
  retention changes.
- Private endpoint + `public_network_access_enabled = false`, workload access
  via managed identity from a cloud scheduler (no laptop, no human credential)
  — see ADR-009.
- Audit: Defender for Storage + diagnostic settings to a log workspace,
  alerting on delete/immutability-policy changes.
- Scheduling from the cloud (Kubernetes CronJob / managed job) so the RPO
  guarantee no longer depends on a developer machine being awake.
- PITR / WAL archiving for RPO << 24h; AKS Backup for CSI volumes as a
  complement (never a replacement) for database-aware backups.
- RabbitMQ definitions export and Redis rebuild drills (Phase 3C).
- Remote encrypted Terraform state backend with locking (Phase 3C; still
  local-only today — recorded in Phase 3A).

## Redis recovery (intentional design)
No Redis business-data backup. Recovery = recreate Redis → `DatabaseInitializer`
reseeds the stock mirror from Postgres → validate. Keeping Redis persistence
off is a decision, not an omission.

## Terraform state risk (recorded, not fixed in 3B)
Local-only TF state for the data-protection module; host loss destroys
infrastructure control history. Fix = remote encrypted Azure backend with
locking (Phase 3C / Phase 7).
