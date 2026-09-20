# ADR-009 — Backup Access and Security (RBAC, encryption, immutability)

- Status: Accepted (Phase 3B)
- Context: Backup artifacts contain sensitive business data (orders, inventory,
  idempotency keys) and are the last line of defence if the database is lost,
  corrupted or encrypted by ransomware. They therefore need three distinct
  guarantees: **only authorised identities can read/write them**, **nothing can
  silently alter them**, and **a deletion or overwrite is recoverable**.
- Related: ADR-008 (redundancy, retention), `docs/architecture/backup-data-protection.md`,
  `docs/runbooks/backup-storage-cleanup.md`.

## Decision

1. **Microsoft Entra ID + Azure RBAC** for every data-plane operation; scripts use
   `az storage ... --auth-mode login`. No storage account keys, no SAS tokens and
   no connection strings anywhere in the repo or in CI.
2. **Shared Key authorization DISABLED** (`allowSharedKeyAccess = false`). This
   removes the "one leaked key = full read/write/delete of every backup" failure
   mode and forces every caller through an identity that can be revoked,
   audited and conditioned.
3. **Encryption at rest: enabled by default and not optional.** Azure Storage
   always encrypts data with AES-256 using a platform-managed key (service-side
   encryption, "Microsoft-managed keys"); the account is `StorageV2` with
   `https_traffic_only_enabled = true` and `min_tls_version = TLS1_2`, so data is
   also encrypted in transit. Enterprise upgrade path (not implemented):
   **customer-managed keys (CMK)** in Key Vault with rotation and a
   key-vault-scoped access policy, plus infrastructure (double) encryption when
   regulatory rules demand it. CMK is a *custody* requirement, not a
   confidentiality switch — the default SSE already protects data at rest.
4. **Private containers only** (`container_access_type = private`,
   `allow_nested_items_to_be_public = false`). Anonymous access is impossible.
5. **Network restriction (implemented):** the storage firewall runs
   `default_action = "Deny"` with an explicit `ip_rules` allowlist (the developer
   egress IP) and `bypass = ["AzureServices"]`. See the trade-off note below on
   why `public_network_access_enabled = false` is *not* used in Portfolio Mode.
6. **Tamper and deletion protection (implemented):**
   - **blob versioning** — an overwrite never destroys the previous bytes;
   - **blob soft delete 14 days** + **container soft delete 14 days** — deletions
     are recoverable;
   - **time-based immutability 1 day, UNLOCKED** — delete *and* overwrite are
     refused while the window is active.
7. **Least privilege for the operator:** `Storage Blob Data Contributor` scoped
   to the storage account, not the subscription. It is the minimum role that
   still allows the restore drill to read the artifact back (a write-only role
   cannot verify a recovery).
8. **Secret-free artifacts:** `.dump`, `.sha256`, `.metadata.json` contain no
   credentials; metadata records only database name, PostgreSQL version, byte
   size, migration IDs and timestamp. Integrity is carried by the SHA-256
   sidecar, and the uploader additionally sends `Content-MD5` so the service
   itself rejects a corrupted transfer.

## Trade-off: `public_network_access_enabled = false` vs IP allowlist

The hardening ideal is "no public network path at all": private endpoint +
`public_network_access_enabled = false` + the backup job running in Azure with a
managed identity.

In Phase 3B the backup job runs on the developer machine and there is no VNet,
VPN or private endpoint yet. Setting `public_network_access_enabled = false`
would therefore make the workflow impossible to run (uploads and restore drills
would fail), and a control that forces the team to disable it again is worse than
a control that holds. The implemented compromise is:

```text
public_network_access_enabled = true     # public endpoint exists ...
network_rules.default_action  = "Deny"   # ... but nothing is allowed by default
network_rules.ip_rules        = [<developer egress IP>/32]
network_rules.bypass          = ["AzureServices"]
```

Consequences, stated honestly:

- Only allowlisted IPs can reach the data plane; everything else gets
  `403 AuthorizationFailure`.
- The allowlist is an **IP dependency**: a new egress IP (home/office/ISP change)
  breaks scheduled backups until `allowed_ip_rules` is updated. Recovery is easy
  because the ARM control plane is *not* firewalled:
  `az storage account update --default-action Allow`.
- Residual gaps vs the ideal: no private endpoint, no VNet integration, no
  service endpoint, no managed identity, no Defender for Storage alerting.

## Enterprise reference Mode (documented, NOT implemented)

| Control | Enterprise implementation |
|---|---|
| Network | private endpoint + `public_network_access_enabled = false`; backup job inside the VNet (AKS CronJob / Container Apps Job) |
| Identity | workload managed identity with `Storage Blob Data Contributor` on the container only; no human credential, no laptop |
| Keys | CMK in Key Vault with rotation, `infrastructure_encryption_enabled = true` |
| Immutability | **locked** time-based policy with a compliance retention window; legal hold where required |
| Monitoring | Defender for Storage + diagnostic settings → Log Analytics; alerts on `DeleteBlob`, `SetBlobImmutabilityPolicy`, mass-overwrite patterns |
| Human access | Conditional Access + PIM just-in-time activation of the data role; documented break-glass account |
| Separation | backup storage in its own subscription/resource group with a distinct change-control path |

## Anti-ransomware reasoning

| Attack | Why it fails here |
|---|---|
| Encrypt/overwrite backup blobs in place | versioning keeps the previous bytes; WORM refuses the overwrite during retention (verified: `BlobImmutableDueToPolicy`) |
| Mass delete backups | blob + container soft delete make deletion recoverable; WORM refuses deletes |
| Steal a key / connection string | Shared Key is disabled, so a leaked key authorises nothing (verified: `KeyBasedAuthenticationNotPermitted`) |
| Use a stolen SAS | no SAS is ever issued or stored |
| Delete the whole container | container soft delete keeps it recoverable for 14 days (verified: `Deleted=true`, `RemainingRetentionDays=14`, restore HTTP 201) |
| Delete the storage account | blocked while policies/retentions are in force, and ARM RBAC still gates the destroy operation |

The **unlocked** qualifier matters: an identity with sufficient permissions can
still change or remove the policy. Unlocked WORM is a strong technical control and
a valid test target, but it is not compliance-grade WORM — only a locked policy is,
and locking is irreversible.

## Verified evidence (Phase 3B)

| Claim | Evidence |
|---|---|
| Entra-only access works, key access is refused | login-mode upload 200/201; key-mode upload → `KeyBasedAuthenticationNotPermitted` |
| Anonymous access impossible | anonymous blob GET and container list → `409 PublicAccessNotPermitted` |
| Private containers | `container_access_type = private`; `publicAccess` null on both containers |
| Least-privilege RBAC scope | `az role assignment list --scope <storage account>` → only `Storage Blob Data Contributor` for the operator |
| Tamper protection | DELETE and PUT(overwrite) on a real backup blob → `409 BlobImmutableDueToPolicy` (both) |
| Deletion recoverability | deleted base blob recovered by `versionId`; deleted container restored |
| No secrets in repo | secret scan over scripts + docs for keys/SAS/PEM → no matches |

Details and full transcripts: `docs/evidence/backup/restore-drill-002-offsite.md`.

## Consequences

- Onboarding a new operator or CI job now requires an **RBAC assignment**, not a
  key copy — slower to start, far safer to run and revoke.
- Some legacy tooling that only understands Shared Key cannot be used against
  this account (accepted; the pipeline is deliberately one-way and scripted).
- Because network access is IP-restricted, a workstation IP change is an
  operational event: documented in
  `docs/runbooks/offsite-backup-scheduling.md` (troubleshooting).
