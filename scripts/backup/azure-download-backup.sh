#!/usr/bin/env bash
# Phase 3B: download a backup set from Azure Blob into a NEW local recovery
# directory and verify checksums, using Entra ID (--auth-mode login).
#
# Usage: azure-download-backup.sh <base> [recovery-dir]
set -euo pipefail
BASE="${1:?usage: azure-download-backup.sh <base> [recovery-dir]}"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:-stflashsalebackup}"
CONTAINER="${CONTAINER:-postgres-backups}"
RECOVERY_DIR="${2:-$(cd "$(dirname "$0")/../.." && pwd)/recovery/azure-$BASE}"
PREFIX="${BLOB_PREFIX:-postgres}"
TS="$(echo "$BASE" | sed 's/flashsale_//')"
BLOB_DIR="$PREFIX/$(echo "$TS" | cut -c1-4)/$(echo "$TS" | cut -c5-6)/$(echo "$TS" | cut -c7-8)"
AZ() { az storage "$@" --auth-mode login --account-name "$STORAGE_ACCOUNT"; }

mkdir -p "$RECOVERY_DIR"
echo "==> downloading $BLOB_DIR/$BASE.{dump,sha256,metadata.json} → $RECOVERY_DIR"
for ext in dump sha256 metadata.json; do
  AZ blob show -c "$CONTAINER" -n "$BLOB_DIR/$BASE.$ext" --query name -o tsv 2>/dev/null >/dev/null \
    || { echo "FAIL: blob $BLOB_DIR/$BASE.$ext not found" >&2; exit 1; }
  AZ blob download -c "$CONTAINER" -n "$BLOB_DIR/$BASE.$ext" \
    --file "$RECOVERY_DIR/$BASE.$ext" --overwrite true 2>/dev/null | tail -1
done

echo "==> verifying checksum"
(cd "$RECOVERY_DIR" && shasum -a 256 -c "$BASE.sha256")
echo "DOWNLOAD PASS: $RECOVERY_DIR/$BASE.dump verified"
