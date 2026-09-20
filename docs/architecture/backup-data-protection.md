# Backup data protection architecture (Phase 3B)

## Scope boundary

Phase 3B provisions **data-protection infrastructure only**. No AKS, ACR,
Azure PostgreSQL, Redis, Service Bus, Container Apps, App Service, ingress or
monitoring stack is part of this phase. The Azure footprint is exactly:
resource group, one StorageV2 account, two private containers, RBAC assignment,
management (lifecycle) policy, and an out-of-band immutability policy.

## Implemented flow (Portfolio Mode)

```text
FlashSale PostgreSQL (docker compose, this developer host)
        │  pg_dump -Fc            scripts/backup/postgres-backup.sh
        ▼
Local staging artifact  backups/flashsale_<UTC>.dump + .sha256 + .metadata.json
        │  artifact validation    scripts/backup/verify-backup.sh
        ▼
Azure Blob Storage (off-host) scripts/backup/azure-upload-backup.sh
  storage account stflashsalebackup   (StorageV2, Standard_LRS, TLS1.2,
                                       public blob access disabled,
                                       Shared Key authorization DISABLED,
                                       firewall default_action=Deny + operator IP allowlist)
    └── container postgres-backups (private)
            └── postgres/YYYY/MM/DD/flashsale_<UTC>Z.{dump,sha256,metadata.json}
                    ├── blob versioning              ON
                    ├── blob soft delete             14 days
                    ├── container soft delete        14 days
                    ├── lifecycle policy             protection-tests/ only
                    └── immutability (WORM)          1 day, UNLOCKED
    └── container protection-tests (private, disposable drills)
```

Recovery flow (proven in drill-002):

```text
Azure Blob (off-host, authenticated download)
        │  scripts/backup/azure-download-backup.sh
        ▼
Local recovery directory recovery/azure-<set>/  ← local original is QUARANTINED
        │  sha256 verification vs the downloaded .sha256 sidecar
        ▼
NEW clean PostgreSQL database
        │  scripts/backup/postgres-restore.sh
        ▼
Verification (rows, stock, migrations, constraints/indexes)
        ▼
Order API smoke test (health, product read, order create, stock decrement)
```

## Orchestration

- `scripts/backup/run-offsite-backup.sh` — 1) local backup, 2) validate,
  3) upload `.dump`/`.sha256`/`.metadata.json`, 4) verify all three objects exist
  remotely **and** that the downloaded checksum sidecar matches. Success is
  reported only after step 4. Local staging is never deleted automatically.
- `scripts/backup/scheduler/` — launchd (macOS) scheduling; see
  `docs/runbooks/offsite-backup-scheduling.md`.

## Blob naming contract

Immutable logical paths, one set per timestamp. There is **no** `latest.dump`
authoritative object: overwriting a single fixed key would destroy history and
would be blocked by the immutability policy anyway (verified: overwrite is
denied with `BlobImmutableDueToPolicy` while retention is active).

```text
postgres/2026/09/20/flashsale_20260920T145146Z.dump
postgres/2026/09/20/flashsale_20260920T145146Z.sha256
postgres/2026/09/20/flashsale_20260920T145146Z.metadata.json
```

## Protection feature interaction (lifecycle ↔ versioning ↔ soft delete ↔ WORM)

| Layer | Setting (Portfolio) | What it protects against |
|---|---|---|
| Blob versioning | ON | overwrite/accidental replacement: old bytes stay addressable by versionId |
| Blob soft delete | 14 days | deletion of a blob/version: recoverable via version or restore |
| Container soft delete | 14 days | accidental container deletion: `Deleted=true`, `RemainingRetentionDays` counted down |
| Immutability (WORM) | 1 day, **UNLOCKED** | delete *and* overwrite denied while the retention window is active |
| Lifecycle policy | **30 days** current blobs / **90 days** versions+snapshots (all containers) → see ADR-008 | unbounded growth while keeping ~30 daily recovery points |

Ordering rule used (ADR-008): WORM (1d) < soft delete (14d) < lifecycle (30d),
so lifecycle never removes data before its soft-delete protection has run out.
A daily backup is live for 30 days, and its bytes remain recoverable as a
non-current version until ~90 days.

Immutable note: the immutability policy is **UNLOCKED**. An administrator with
sufficient permissions can change or remove it, so `UNLOCKED != compliance-grade
WORM`. Locking is an enterprise/compliance decision taken only after the policy
has been validated (a locked policy can never be unlocked or shortened).
