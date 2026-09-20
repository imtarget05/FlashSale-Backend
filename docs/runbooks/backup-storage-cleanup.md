# Runbook — backup storage cleanup & retention safety (Phase 3B)

## Golden rules

1. **Never lock** the immutability policy during portfolio work. A locked policy
   cannot be unlocked and its retention can only be *extended*; a wrong value
   immobilises the container until it expires and can block cleanup of the
   resource group / storage account.
2. **Never `terraform destroy`** the backup storage as a "cleanup" step. The
   storage account is deliberately not destroyed by task completion.
3. Deleting data is **not immediate** when soft delete and/or versioning are on:
   deletion is recorded, retention is counted down, restore is still possible.

## Current retention map (Portfolio Mode)

| Item | Value | Effect |
|---|---|---|
| Blob versioning | ON | old bytes addressable by `versionId` |
| Blob soft delete | 14 days | deleted blobs/versions recoverable |
| Container soft delete | 14 days | deleted containers recoverable (`RemainingRetentionDays`) |
| Immutability (WORM) | 1 day, **UNLOCKED** | delete/overwrite of protected blobs denied while active |
| Lifecycle | `backup-retention` rule (all containers) | current blob 30d, non-current version 90d, snapshot 90d |
| Network firewall | `default_action = Deny` + operator IP allowlist | data plane reachable only from allowlisted IPs |

Ordering contract (ADR-008): WORM 1d < soft delete 14d < lifecycle 30d — cleanup
must never act on data that is still inside a protection window.

## Removing the test immutability policy (before any container/account deletion)

Unlocked policies can be removed by an identity with permissions on the
container; the syntax requires the current ETag:

```bash
AZ="az storage container immutability-policy"
ETAG=$($AZ show --account-name stflashsalebackup --container-name postgres-backups --query etag -o tsv)
$AZ delete --account-name stflashsalebackup --container-name postgres-backups --if-match "$ETAG"
```

Note: removing the policy stops *future* protection; blobs whose retention has
already started remain protected until their 1-day window expires. Also delete
any legal hold first if one was set (`az storage container legal-hold clear ...`).

## Cost & cleanup implications

- Storage: ~5 KB per backup set; the cost driver is transactions and (for the
  enterprise path) replica GB — not these artifacts.
- Soft delete/versioning **bills the retained bytes**; with tiny dumps that is
  fractions of a cent, but at enterprise scale old versions are a real line item
  (that is why the lifecycle policy exists).
- An immutable blob **cannot be deleted to save money** until its retention
  expires. Unlocked ≠ free: the protection applies to the *objects* regardless of
  policy lock state.
- Deleting the storage account itself fails while immutability policies or legal
  holds are in force; delete the policy first, wait for active retentions, then
  delete the account.

## Escalation order for a real cleanup

1. Stop the scheduler (`install-offsite-scheduler.sh --unload`).
2. Confirm the newest backup you still need is *not* inside an active WORM
   window (or accept waiting it out — max 1 day here).
3. Remove immutability policy (unlocked) / legal hold.
4. Delete the containers (soft delete starts a further 14-day window) or restore
   them if that was a mistake (`Restore Container`).
5. Only then consider deleting the storage account / resource group — and only
   with an explicit, documented decision: backups are the last line of defence.

## Recovering from an accidental delete (quick reference)

```bash
# blob / version deleted  → restore by version or undelete
az storage blob list -c protection-tests --account-name stflashsalebackup \
  --auth-mode login --include dv --query '[].{name:name,version:versionId}'
az storage blob download -c postgres-backups --account-name stflashsalebackup \
  --auth-mode login -n postgres/YYYY/MM/DD/flashsale_<UTC>Z.dump --version-id <id> -f out.dump

# container deleted → list deleted containers, then restore (headers required)
az storage container list --include-deleted --account-name stflashsalebackup --auth-mode login
curl -X PUT -H "Authorization: Bearer $(az account get-access-token --resource https://storage.azure.com/ --query accessToken -o tsv)" \
  -H 'x-ms-version: 2023-11-03' -H 'x-ms-deleted-container-name: <name>' \
  -H 'x-ms-deleted-container-version: <version>' -H 'Content-Length: 0' \
  "https://stflashsalebackup.blob.core.windows.net/<name>?restype=container&comp=undelete"
```
