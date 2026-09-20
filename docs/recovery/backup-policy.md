# Backup Policy (P01 FlashSale) — Phase 3A

## Principles
- **PostgreSQL is the business source of truth.** Its backup is the only
  business-critical protection. Everything else is reconstructable or Git.
- **A backup that has never been restored is not a backup** — Phase 3A gates
  on a completed clean-target restore drill with evidence.
- Volume persistence (`postgres-data`) ≠ backup: one host, one copy, destroyed
  by `down -v`, disk loss, or laptop loss.

## Reference targets (REFERENCE PORTFOLIO TARGET)
| Target | Value | Status |
|---|---|---|
| RPO | ≤ 24h | NOT YET GUARANTEED — backups are manual; achievable RPO = time since last manual run |
| RTO | ≤ 2h | drill-001 measured restore duration ~1 min (script only, manual) |

## Current implementation (Phase 3A — DONE)
- `scripts/backup/postgres-backup.sh` — `pg_dump -Fc` inside the compose
  postgres container → `backups/flashsale_<UTC>.dump` + `.sha256` +
  `.metadata.json` (version, size, migrations; no secrets). Password from env.
- `scripts/backup/verify-backup.sh` — checksum + `pg_restore --list` + expected
  tables (`Products`, `Orders`, `__EFMigrationsHistory`).
- `scripts/backup/postgres-restore.sh` — refuses live `FlashSaleDb` target
  (override requires `ALLOW_OVERWRITE_SOURCE=yes`), creates a NEW empty DB,
  restores, fails non-zero on invalid archive.

## PLANNED PHASE 3B (not implemented — do not claim)
- Copy artifacts to Azure Blob Storage; enable blob versioning, blob soft
  delete, container soft delete, retention policy; immutability (WORM) for a
  designated backup tier; least-privilege identity (no DB password in job).
- Scheduled backup (cron/CI) to make RPO ≤24h actually guaranteed.
- RabbitMQ definitions export off-host; broker rebuild drill.
- Terraform state → remote encrypted backend with locking.

## PLANNED LATER (only when business requirements justify)
- PITR / WAL archiving for RPO << 24h (Phase 7+ managed PostgreSQL or
  self-managed WAL shipping).
- AKS Backup for CSI volumes (Phase 7/10) — complements, never replaces,
  database-aware backup.
- Transactional Outbox if accepted-but-unpersisted order loss becomes a
  business-level requirement.

## Redis recovery (intentional design)
No Redis business-data backup. Recovery = recreate Redis → `DatabaseInitializer`
reseeds the stock mirror from Postgres → validate. Keeping Redis persistence
off is a decision, not an omission.

## Terraform state risk (recorded, not fixed in 3A)
Local-only TF state; host loss destroys infrastructure control history.
Fix = remote encrypted Azure backend with locking (Phase 3B / Phase 7).
