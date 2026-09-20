# Restore Drill 002 — OFF-HOST backup (Azure Blob) → local loss → clean restore → app smoke

> **Hardening addendum (later the same day).** After this drill first passed, the
> backup storage was hardened without touching any application resource:
> storage firewall `default_action=Deny` + operator IP allowlist, lifecycle
> `backup-retention` (current blobs 30d / versions+snapshots 90d), and
> server-side `Content-MD5` + re-download SHA-256 round-trip checks in
> `azure-upload-backup.sh`. The Terraform root moved from
> `AKS-SRE-Platform/terraform/data-protection/` to
> `infrastructure/terraform/backup-storage/` **with its state**, so all resources
> below are still the same resources (apply showed 2 in-place updates, 0 adds,
> 0 destroys). Enforcement was proven live: swapping the allowlist to
> `203.0.113.99` made the laptop's data-plane call fail with the network-rules
> error, and restoring the operator IP via ARM made it succeed; afterwards
> `terraform plan` reported no drift. Two further off-site runs
> (`..._152635Z`, `..._152720Z`, `..._152733Z`, all `OFF-SITE BACKUP PASS`, the
> last with a verified round-trip SHA-256) confirm the hardened account still
> uploads, versions and restores.
> Detail: ADR-008 (retention), ADR-009 (access/security incl. the
> `public_network_access_enabled=false` trade-off).

Phase: 3B (off-host backup protection). Predecessor: `restore-drill-001.md` (Phase 3A, local-only).

## Objective

Prove that business data survives **loss of the local backup artifact**: upload a
real PostgreSQL backup to protected Azure Blob Storage, quarantine the local copy
(treat it as lost), recover **only** from Azure, verify the checksum, restore into
a new clean database, and run the real Order API against the restored data.

## Azure preflight

| Item | Value |
|---|---|
| az CLI | 2.90.0 |
| terraform | 1.16.3 (darwin_arm64) |
| subscription | `Azure subscription 1` — `a3deec78-7edb-41cd-9e94-ec1d4d9379f5` |
| tenant | `aa79a92c-ec09-4de1-baa9-151b8f9df886` |
| principal | `Tân Mai` (user, `binhtan5734_gmail.com#EXT#@binhtan5734gmail.onmicrosoft.com`) |
| region | `southeastasia` |
| provider | azurerm 3.119.x, `storage_use_azuread = true` |
| notes | `az account show` initially returned AADSTS530035 (tenant Security Defaults) for management-plane calls; resolved by re-running the interactive login. `Microsoft.Storage` was `NotRegistered` → registered. **No key, token, SAS or password is printed anywhere in this document.** |

## Source environment

Compose stack `flashsale-backend`, container `flashsale-backend-postgres-1`
(PostgreSQL 15.19), .NET Order API (`src/Order.Api`).

Source state measured before the backup (the source DB is never modified by this drill):

```text
products=1   orders=0   stock_id1=100
migrations=20260920123806_InitialCreate
indexes: PK_Orders (unique on Id), IX_Orders_IdempotencyKey (unique on IdempotencyKey)
constraints: PK_Orders:p
```

## Backup + upload

```bash
POSTGRES_PASSWORD=... COMPOSE_PROJECT=flashsale-backend ./scripts/backup/run-offsite-backup.sh
```

```
== [1/4] local backup ==
==> pg_dump -Fc FlashSaleDb (container flashsale-backend-postgres-1)
==> wrote .../backups/flashsale_20260920T145146Z.dump (+ .sha256, .metadata.json)
== [2/4] artifact validation ==
flashsale_20260920T145146Z.dump: OK
OK: archive contains TABLE public Products
OK: archive contains TABLE public Orders
OK: archive contains __EFMigrationsHistory
VERIFY PASS
== [3/4] Azure upload ==
==> uploading flashsale_20260920T145146Z.* to stflashsalebackup/postgres-backups/postgres/2026/09/20/ (Entra auth)
UPLOAD PASS: 3/3 objects in Azure (checksum sidecar matches local)
== [4/4] OFF-SITE BACKUP PASS: flashsale_20260920T145146Z available in Azure ==
```

**Backup timestamp:** `2026-09-20T14:51:46Z`. **Size:** 5,102 bytes.
**Blob paths (container `postgres-backups`, private):**

```text
postgres/2026/09/20/flashsale_20260920T145146Z.dump          5102 B  v=2026-09-20T14:51:48.1478273Z
postgres/2026/09/20/flashsale_20260920T145146Z.sha256          98 B  v=2026-09-20T14:51:48.9853352Z
postgres/2026/09/20/flashsale_20260920T145146Z.metadata.json  362 B  v=2026-09-20T14:51:49.8201614Z
```

Remote `metadata.json` (no secrets) read back from Azure:

```json
{"timestamp_utc":"20260920T145146Z","database":"FlashSaleDb","postgres_version":"15.19",
 "format":"pg_dump custom (-Fc)","file":"flashsale_20260920T145146Z.dump","size_bytes":5102,
 "sha256_file":"flashsale_20260920T145146Z.sha256","migrations":"20260920123806_InitialCreate",
 "source":"compose-postgres","phase":"3A-local"}
```

## Azure protection configuration (verified after apply)

```text
### storage account
{"kind":"StorageV2","sku":"Standard_LRS","tls":"TLS1_2",
 "allowBlobPublicAccess":false,"allowSharedKeyAccess":false,
 "hns":false,"httpsOnly":true}
### blob service
{"isVersioningEnabled":true,
 "deleteRetentionPolicy":{"enabled":true,"days":14},
 "containerDeleteRetentionPolicy":{"enabled":true,"days":14}}
### immutability policy (container postgres-backups)
{"state":"Unlocked","days":1,
 "id":".../containers/postgres-backups/immutabilityPolicies/default"}
### lifecycle (management policy "default")
{"rule":"backup-retention","enabled":true,
 "filters":{"blobTypes":["blockBlob"]},
 "actions":{"baseBlob":{"delete":{"daysAfterModificationGreaterThan":30}},
            "version":{"delete":{"daysAfterCreationGreaterThan":90}},
            "snapshot":{"delete":{"daysAfterCreationGreaterThan":90}}}}
### rbac
[{"role":"Storage Blob Data Contributor","principalType":"User",
  "scope":".../storageAccounts/stflashsalebackup"}]
```

Entra-only access proven negatively and positively:

```text
az storage blob upload (Shared Key path)  → ErrorCode:KeyBasedAuthenticationNotPermitted
az storage blob upload --auth-mode login  → upload succeeds
anonymous GET of a backup blob            → HTTP 409 PublicAccessNotPermitted
anonymous container list                  → HTTP 409 PublicAccessNotPermitted
```

## Protection feature tests

### 1) Versioning (container `protection-tests`, key `versioning-test.txt`)

Two uploads to the SAME key:

```text
version 2026-09-20T14:53:49.9681139Z  25 B   PREVIOUS
version 2026-09-20T14:53:51.0557660Z  41 B   CURRENT
download --version-id 2026-09-20T14:53:49.9681139Z → "protection-test-version-A"
download (current)                                  → "protection-test-version-B-changed-content"
```

Result: two versions coexist, the previous version is identifiable **and its
original bytes are still retrievable**. PASS.

### 2) Delete protection / recoverability

Deleted a base blob (`soft-delete-test.txt`). In a **versioning-enabled account the
delete does not create a separate "deleted blob marker"**: the current version
becomes a noncurrent version and the blob name disappears from normal listing
(confirmed with `--include dv`, the REST `include=versions,deleted` listing, and
the CLI). Recovered the deleted data from the retained version:

```text
RECOVERED CONTENT: soft-delete-drill-payload
sha256(recovered) == sha256(original)  (5a69dc9e4e53539b…)
```

Deleting a *version* directly returned
`403 The specified operation is not allowed on version.` — a documented behaviour
difference worth knowing before writing runbooks. PASS (data was recoverable).

### 3) Container soft delete (dedicated container `container-delete-test`)

```text
create container  → {"created": true}
delete container  → {"deleted": true}
list deleted      → <Name>container-delete-test</Name>
                    <Deleted>true</Deleted>
                    <Version>01DD49101C7FC835</Version>
                    <RemainingRetentionDays>14</RemainingRetentionDays>
restore (PUT comp=undelete + x-ms-deleted-container-name/-version) → HTTP 201 Created
list containers   → container-delete-test, postgres-backups, protection-tests
```

Note: `az storage container restore` in azure-cli 2.90 failed silently without
`--deleted-version`; the REST call with both headers is the reliable path (recorded
in `docs/runbooks/backup-storage-cleanup.md`). The real backup container was never
deleted. PASS.

### 4) Immutability / WORM on a REAL backup blob (UNLOCKED policy, 1 day)

Attempts against `postgres/2026/09/20/flashsale_20260920T145146Z.dump`:

```text
DELETE  → HTTP 409  x-ms-error-code: BlobImmutableDueToPolicy
          "This operation is not permitted as the blob is immutable due to a policy."
PUT (overwrite with 25 bytes of junk)
        → HTTP 409  x-ms-error-code: BlobImmutableDueToPolicy
          "This operation is not permitted as the blob is immutable due to a policy."
```

Both destructive operations were denied by Azure and the stored backup was not
modified. PASS. **The policy is UNLOCKED**, so an administrator with sufficient
permissions can still change/remove it — `UNLOCKED != compliance-grade WORM`.
Locking was intentionally NOT performed (a locked policy cannot be unlocked or
shortened and would obstruct cleanup of a credit-funded subscription).

## Local-loss simulation

```bash
BASE=flashsale_20260920T145146Z
mkdir -p .loss-simulation-quarantine
mv backups/$BASE.dump backups/$BASE.sha256 backups/$BASE.metadata.json .loss-simulation-quarantine/
```

The recovery workspace (`recovery/`) did not exist at that moment, so the operator
had **no local copy** of the artifact. The source database was NOT touched.

## Recovery from Azure only

```bash
./scripts/backup/azure-download-backup.sh flashsale_20260920T145146Z
```

```
==> downloading postgres/2026/09/20/flashsale_20260920T145146Z.{dump,sha256,metadata.json} → recovery/azure-flashsale_20260920T145146Z
==> verifying checksum
flashsale_20260920T145146Z.dump: OK
DOWNLOAD PASS: .../recovery/azure-flashsale_20260920T145146Z/flashsale_20260920T145146Z.dump verified
```

Checksum verification succeeded against the `.sha256` sidecar that came **from
Azure**, i.e. the downloaded bytes match the checksum recorded at backup time.

## Clean restore (new database, downloaded artifact only)

```bash
./scripts/backup/postgres-restore.sh recovery/azure-flashsale_20260920T145146Z/flashsale_20260920T145146Z.dump FlashSaleRestore3BFinal
```

```
==> validating archive ...  (TOC contains TABLE public Orders)
==> creating clean target FlashSaleRestore3BFinal
NOTICE: database "FlashSaleRestore3BFinal" does not exist, skipping
CREATE DATABASE
==> pg_restore into FlashSaleRestore3BFinal
==> restore complete
```

## Source vs restored comparison

| Check | Source (`FlashSaleDb`) | Restored from Azure (`FlashSaleRestore3BFinal`) | Match |
|---|---|---|---|
| Products rows | 1 | 1 | ✅ |
| Orders rows | 0 | 0 (immediately after restore) | ✅ |
| `Products.Id=1.AvailableStock` | 100 | 100 | ✅ |
| `__EFMigrationsHistory` | `20260920123806_InitialCreate` | `20260920123806_InitialCreate` | ✅ |
| Indexes on Orders | `PK_Orders`, `IX_Orders_IdempotencyKey` | identical definitions | ✅ |
| Constraints on Orders | `PK_Orders:p` | `PK_Orders:p` | ✅ |
| Duplicate idempotency-key insert | — | `ERROR: duplicate key value violates unique constraint "IX_Orders_IdempotencyKey"` | ✅ unique index enforced |
| Source DB after all drill operations | orders=0, stock=100 (untouched) | — | ✅ |

## Application smoke test against the restored database

Order API started with a connection string pointing ONLY at the restored DB
(Redis: `abortConnect=false`, messaging provider `InMemory` for the drill):

```text
health/live   → {"status":"healthy"}
health/ready  → {"status":"ready"}
GET /api/products/1 → {"id":1,"name":"iPhone 15 Pro Max","availableStock":99}
POST /api/orders (idempotencyKey=offsite-final-…) → 202 {"message":"Order accepted",
      "idempotencyKey":"091f28abc45647988b610ee77ae58abd","status":"processing"}
GET /api/orders/{key} → {"idempotencyKey":"offsite-final-…","status":"processing"}
GET /api/products/1 → availableStock 98          ← stock decrement correct (99 → 98)
orders in restored DB after smoke = 2, stock = 98
```

The API's own rows confirm writes landed in the restored database, while the
source database remained at `orders=0 / stock=100`.

## Off-host recovery duration (measured)

| Step | Duration |
|---|---|
| Download from Azure + checksum verify (3 objects, cold az CLI) | **10 s** |
| Restore (validate archive → create clean DB → pg_restore) | **5 s** (first run 0–1 s) |
| Verification queries (rows, stock, migration, indexes, constraint) | **< 1 s** |
| Application startup to first healthy response | **2 s** |
| Application smoke assertions | **2 s** |
| **Total (download → verified smoke)** | **≈ 20 s** |

Operator wall-clock for the whole drill was longer (~10 min) because of an
environment incident: Docker Desktop terminated mid-drill (containers exited,
`docker.sock` unreachable), which added a restart cycle before the smoke test
could be re-run. That incident is reported rather than hidden: the numbers above
come from the clean, uninterrupted run.

Scope of this metric: 5 KB artifact, single-node local PostgreSQL, warm host.
It proves the **procedure** end-to-end; it is not an enterprise-scale RTO.

## Scheduled backup evidence (Step 20)

launchd agent installed via `scripts/backup/scheduler/install-offsite-scheduler.sh --test`
(90-second interval, evidence only), then switched to `--daily` (02:30 local).

```
=== 2026-09-20T15:14:46Z scheduled off-site backup start ===
VERIFY PASS .../flashsale_20260920T151446Z.dump
UPLOAD PASS: 3/3 objects in Azure → postgres/2026/09/20/flashsale_20260920T151446Z.dump
== [4/4] OFF-SITE BACKUP PASS ===
=== 2026-09-20T15:14:54Z scheduled off-site backup OK ===
=== 2026-09-20T15:16:24Z scheduled off-site backup start ===
VERIFY PASS .../flashsale_20260920T151624Z.dump
UPLOAD PASS: 3/3 objects in Azure → postgres/2026/09/20/flashsale_20260920T151624Z.dump
=== 2026-09-20T15:16:31Z scheduled off-site backup OK ===
```

**Backup 1:** `flashsale_20260920T151446Z` (dump 15:14:49Z, sha256 15:14:49Z, metadata 15:14:50Z)
**Backup 2:** `flashsale_20260920T151624Z` (dump 15:16:26Z, sha256 15:16:26Z, metadata 15:16:27Z)

Two independently timestamped, automatically produced backup sets, each with all
three objects present in Azure. Final configuration: `--daily` agent loaded
(`com.flashsale.offsite-backup`).

Failures found and fixed while implementing this:
1. `az storage --auth-mode login --account-name X blob upload …` (options before
   the command group) is rejected — options must follow the group.
2. Upload without `--overwrite false` hits `BlobAlreadyExists`, which under
   `set -euo pipefail` aborted the script silently; `--overwrite false` is now
   explicit so a re-upload of the same key fails loudly instead of replacing a
   protected artifact.
3. launchd agents can't read `~/Downloads` (macOS TCC) → the install step stages
   the scripts under `~/Library/Application Support/flashsale-offsite-backup/`.
4. launchd's minimal `PATH` broke `docker`/`az` → the wrapper exports an explicit
   PATH.

## RPO assessment

- Target (portfolio reference): ≤ 24h.
- Current achievable: one scheduled backup per day at 02:30 local **while the
  developer machine is powered on and awake**; manual `run-offsite-backup.sh`
  available at any time.
- Evidence of automatic execution: two scheduled sets ~98 s apart, produced and
  verified in Azure.
- **Verdict: RPO ≤ 24h is CONDITIONAL on scheduler-host availability.** If the
  laptop sleeps or is off for 3 days, the newest backup is 3 days old.

## RTO assessment

- Target (portfolio reference): ≤ 2h.
- Measured off-host drill: ≈ 20 s (download 10 s, restore 5 s, verify <1 s,
  app healthy + smoke 4 s) for a 5 KB artifact on a warm host.
- Scope: procedure proof, not a certified enterprise RTO; no cold-host, DNS,
  network-restriction or scale simulation was performed in this phase.

## Security findings

1. Shared Key authorization is **disabled**; a key-based upload returns
   `KeyBasedAuthenticationNotPermitted`, and only Entra ID (`--auth-mode login`)
   works. No key, SAS or connection string exists in any script or doc.
2. Anonymous blob access is impossible: `PublicAccessNotPermitted` (HTTP 409) for
   both blob read and container listing.
3. The storage **firewall denies all data-plane traffic except the allowlisted
   operator IP** (`defaultAction=Deny`, `ipRules=[operator egress IP]`,
   `bypass=AzureServices`). Enforcement was proven by swapping the allowlist to
   `203.0.113.99`: the laptop's subsequent data-plane call failed with the CLI's
   "may be blocked by network rules" error, and restoring the operator IP via the
   (unfirewalled) ARM control plane made the same call succeed.
   `public_network_access_enabled` stays `true` on purpose in Portfolio Mode —
   setting it to `false` with no private endpoint would block the laptop backup
   job entirely (ADR-009 trade-off note; enterprise path = private endpoint).
4. RBAC is scoped to the storage account for the operator identity
   (`Storage Blob Data Contributor`); the pre-existing subscription `Owner` role
   is not used by the backup scripts.
5. Upload integrity is enforced twice: the uploader sends a per-file
   `Content-MD5` that Azure verifies server-side (a corrupted transfer is
   rejected with `Md5Mismatch` instead of stored), and the script then
   re-downloads the `.dump` and compares its SHA-256 against the `.sha256`
   sidecar (round-trip check).
6. Residual risk: the allowlist is an IP dependency (new egress IP = 403s until
   re-applied; recovery via `--default-action Allow` on the control plane), no
   private endpoint, and the operator's laptop holds an interactive Azure
   session (no managed identity yet).
7. Residual risk: the local `.env` (git-ignored, mode 600 when staged for the
   scheduler) holds the database password used by the scheduled job; secret
   handling moves to Key Vault / managed identity in the cloud phase.

## Cost considerations

- Storage: ~5 KB per backup set; hundreds of sets stay far below a cent per month.
  The real cost driver for the enterprise path would be redundancy/replica GB and
  transactions, not these artifacts.
- Versioning + 14-day soft delete **bill retained bytes**: negligible here, a real
  line item at scale (hence the lifecycle policy).
- Immutability (even unlocked) blocks deletion until retention expires — you
  cannot "save money" by deleting a protected blob early.
- Container/storage-account deletion is blocked while policies/retentions are in
  force, so credit-fuelled cleanup must wait out the 1-day WORM + 14-day soft
  delete windows (see the cleanup runbook).

## Remaining data-loss scenarios

1. Host loss with an EMPTY off-site history: everything created since the last
   scheduled run (up to ~24h, more if the machine was asleep) is lost.
2. Region/datacenter loss: LRS keeps all copies in one datacenter (ADR-008).
3. Identity loss: no access to the storage account without a valid Entra identity
   holding the data role (no break-glass account documented).
4. RabbitMQ definitions and Redis state are not backed up off-host (Phase 3C).
5. Terraform state for the data-protection module is local-only; losing the host
   loses clean infrastructure control (Phase 3C).
6. Long-horizon integrity: lifecycle and soft-delete windows are configured, but
   no multi-month restore test has been run.

## Evidence locations

- Scripts: `scripts/backup/{run-offsite-backup,azure-upload-backup,azure-download-backup,postgres-backup,verify-backup,postgres-restore}.sh`
  and `scripts/backup/scheduler/*`.
- Terraform: `infrastructure/terraform/backup-storage/{versions,variables,main,outputs}.tf`
  + `terraform.tfvars` (operator IP allowlist, retention values).
  Authoritative isolated data-protection root; Terraform state is local and
  not committed. (Previously at `AKS-SRE-Platform/terraform/data-protection/`;
  moved together with its state — see the README pointer left there.)

- Docs: `docs/architecture/backup-data-protection.md`,
  `docs/adr/008-backup-storage-redundancy.md`,
  `docs/adr/009-backup-access-security.md`,
  `docs/runbooks/backup-storage-cleanup.md`,
  `docs/runbooks/offsite-backup-scheduling.md`,
  `docs/recovery/backup-policy.md`.
- Downloaded recovery artifact (git-ignored): `recovery/azure-flashsale_20260920T145146Z/`.
- Quarantined local originals (git-ignored): `.loss-simulation-quarantine/`.
- Scheduler log: `~/Library/Application Support/flashsale-offsite-backup/repo/logs/offsite-backup.log`.

## Known leftover state (reported honestly, cannot be cleaned yet)

Azure container `postgres-backups` currently also holds two **incomplete debug
sets** created while diagnosing the upload script:

```text
postgres/2026/09/20/flashsale_20260920T144802Z.dump + .sha256     (no metadata.json)
postgres/2026/09/20/flashsale_20260920T144809Z.dump               (dump only)
```

They are not valid backup sets (missing companions), but they **cannot be
deleted right now**: they were uploaded after the 1-day immutability policy was
created, so `az storage blob delete` on them returns
`BlobImmutableDueToPolicy` (verified). This is the WORM policy working as
designed — see `docs/runbooks/backup-storage-cleanup.md` for the explicit
cleanup procedure once the retention window expires. It is also a live reminder
that delete protection must be planned for before it is enabled.
