# ADR-008 — Backup Storage Redundancy Strategy (LRS vs ZRS vs GRS vs GZRS)

- Status: Accepted (Phase 3B)
- Context: PostgreSQL backups leave the developer host and live in Azure Blob
  Storage (`stflashsalebackup`, container `postgres-backups`). Keeping the only
  copy on the same machine as the database is a single point of failure: host,
  disk or laptop loss destroys both the workload *and* the backup.
- Related: ADR-009 (access, encryption, immutability), ADR-006 (migration
  strategy), `docs/architecture/backup-data-protection.md`.

## Decision

**Portfolio Mode: `Standard_LRS`** (locally redundant storage, single region
`southeastasia`). **Enterprise reference Mode: `Standard_ZRS` or `Standard_GZRS`**
chosen from a business continuity requirement, not from a résumé keyword.

## Options compared

| Option | Copies | Protects against | Cost (relative) | Portfolio verdict |
|---|---|---|---|---|
| LRS | 3 synchronous copies in ONE datacenter of ONE region | disk/rack/node/server failure | lowest | **chosen** — real Azure evidence, credit-friendly |
| ZRS | 3 synchronous copies across 3 availability zones in one region | whole-datacenter (zone) failure inside one region | ~1.5× LRS | enterprise minimum for production state |
| GRS | LRS in primary + asynchronous replication to a secondary region | region-level disaster | ~2× LRS | reference option for geo-compliance |
| GZRS | ZRS in primary + async replication to secondary region | zone + region failure | highest | reference option for critical tier |

## Why LRS for Portfolio Mode

1. The portfolio objective is to prove **off-host protection mechanics**
   (versioning, soft delete, immutability, RBAC, restore drill), not to buy
   geo-resilience.
2. Backup artifacts here are ~5 KB; the cost driver would be replication, not
   volume. Spending credits on GRS would buy a claim we cannot exercise
   (we cannot trigger a region failover drill in this phase).
3. Honest limitation: **LRS keeps all three copies inside one datacenter.**
   Loss of that datacenter loses both the workload and the LRS backup.

## Why redundancy does NOT replace versioning / soft delete / immutability

Redundancy replicates *whatever is written*, including mistakes: `DELETE`,
`DROP DATABASE`, ransomware overwrite and accidental `terraform destroy` are
replicated by ZRS/GRS/GZRS exactly like legitimate writes. Therefore:

- redundancy = infrastructure failure tolerance,
- versioning + soft delete + immutability = *logical* deletion/overwrite
  protection (this is what actually saved data in the Phase 3B tests).

Both layers exist in Phase 3B; neither substitutes for the other.

## Enterprise reference Mode

- `Standard_GZRS` (or ZRS when a single-region SLA is acceptable),
- geo-redundancy paired with a documented **geo-recovery** runbook and
  region-failover test — a redundancy tier is worthless if nobody has ever
  failed over,
- secondary region in a different Azure geography if compliance requires it,
- cost sign-off because replicated GB and egress are billed.

## Retention policy

Retention is split across three mechanisms, because each one protects against a
different failure. Numbers below are the **implemented** Portfolio Mode values
(Terraform variables in `infrastructure/terraform/backup-storage/`).

| Layer | Portfolio Mode (implemented) | Protects against | Enterprise reference |
|---|---|---|---|
| Current backup blobs (`postgres/`) | lifecycle delete after **30 days** (`retention_current_days = 30`) | unbounded storage growth; keeps ~30 daily recovery points | 30–365 days from the compliance/backup policy, tiered (hot → cool → archive) |
| Non-current blob versions + snapshots | lifecycle delete after **90 days** (`retention_noncurrent_days = 90`) | a bad/overwritten backup discovered late | aligned to the same compliance window, often 90–365 days |
| Blob soft delete | **14 days** | accidental/malicious delete of a blob or version | 14–365 days; often set to the maximum the workload tolerates |
| Container soft delete | **14 days** | accidental container deletion | same |
| Immutability (WORM) | **1 day, UNLOCKED** | overwrite/delete during the window (ransomware, bad script) | **locked** policy, retention = compliance window, legal hold where required |

Ordering rule that makes the layers non-conflicting:

```text
WORM           1 day      ← shortest, proves the mechanism
soft delete    14 days    ← must outlive WORM so deletes are recoverable
lifecycle      30 days (current) / 90 days (versions) ← runs last
```

Lifecycle deletion therefore only ever acts on data whose WORM window has
already expired, and soft delete still shields the object for a further 14 days.
A 30-day "current blob" deletion plus 90-day version deletion means a daily
backup is live for 30 days and its bytes remain recoverable (as a non-current
version) for roughly 90 days.

Why 30 days and not 7 for the current tier: a daily cadence at 30 days gives
~30 independent recovery points, which covers the realistic "we noticed the
corruption/corruption-class bug weeks later" case without unbounded growth.
Why 90 days for versions rather than deleting them with the current blob: the
whole point of the versioning layer is to survive a *late* discovery.

**Portfolio vs enterprise honesty:** 30/90/14/14 days is a defensible portfolio
default, *not* a compliance claim. A regulated workload derives all four numbers
from its RPO, its retention obligation and its legal-hold requirements, and
locks the WORM policy to match.

## Cost implications of retention

Retained versions and soft-deleted objects are **billed bytes**. At this data
size (a daily dump of ~5 KB) the whole retention tail costs fractions of a cent
per month; at production scale the version tier is a real line item, which is
exactly why the lifecycle rule exists rather than "keep everything forever".
Lifecycle tiering (Cool/Archive) is deliberately not configured here: moving
5 KB artifacts between tiers would cost more in transactions than it saves.

## Implementation location

- Terraform (authoritative): `infrastructure/terraform/backup-storage/` — an
  **isolated root** that owns only the backup storage account, its private
  containers, the lifecycle policy, the network firewall and the RBAC
  assignment. Terraform state lives with it (still local, see Phase 3C for the
  remote backend with locking).
  It is deliberately **not** merged into `infrastructure/terraform/main.tf`,
  which defines the application stack (ACR, PostgreSQL Flexible Server, Redis,
  Service Bus, Container Apps): a `terraform apply` of that root would create
  application resources, and Phase 3B must not do that.
- Runtime: `scripts/backup/run-offsite-backup.sh` (upload + verify) and
  `scripts/backup/scheduler/` (launchd daily schedule).

## Consequences

- Portfolio Mode cannot claim region-level survivability. Any statement about
  multi-region DR stays "reference design, not implemented" until a failover has
  actually been drilled.
- Switching LRS → ZRS later is possible via a live migration/SKU change; the
  access model (Entra ID/RBAC) and protection features carry over unchanged.
- Because lifecycle, soft delete and WORM all act on the same objects, any
  future change to one number must be checked against the ordering rule above
  (WORM < soft delete < lifecycle). Documented in
  `docs/runbooks/backup-storage-cleanup.md`.
