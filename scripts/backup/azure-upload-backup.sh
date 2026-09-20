#!/usr/bin/env bash
# Phase 3B: upload a verified local backup set (.dump/.sha256/.metadata.json)
# to Azure Blob Storage using Microsoft Entra ID (--auth-mode login).
# NO storage account keys, NO SAS tokens, ever.
#
# Usage: azure-upload-backup.sh <base>  (e.g. flashsale_20260920T133223Z)
set -euo pipefail
BASE="${1:?usage: azure-upload-backup.sh <base> (e.g. flashsale_20260920T133223Z)}"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:-stflashsalebackup}"
CONTAINER="${CONTAINER:-postgres-backups}"
BACKUP_DIR="${BACKUP_DIR:-$(cd "$(dirname "$0")/../../backups" && pwd)}"
PREFIX="${BLOB_PREFIX:-postgres}"
TS="$(echo "$BASE" | sed 's/flashsale_//')"
BLOB_DIR="$PREFIX/$(echo "$TS" | cut -c1-4)/$(echo "$TS" | cut -c5-6)/$(echo "$TS" | cut -c7-8)"
AZ() { az storage "$@" --auth-mode login --account-name "$STORAGE_ACCOUNT"; }

for ext in dump sha256 metadata.json; do
  [ -s "$BACKUP_DIR/$BASE.$ext" ] || { echo "FAIL: missing $BACKUP_DIR/$BASE.$ext" >&2; exit 1; }
done

echo "==> uploading $BASE.* to $STORAGE_ACCOUNT/$CONTAINER/$BLOB_DIR/ (Entra auth)"
for ext in dump sha256 metadata.json; do
  # Content-MD5 (base64) is verified SERVER-SIDE by Azure Storage: if a single
  # byte is corrupted in transit, the upload is rejected with
  # Md5Mismatch instead of silently storing a bad object.
  MD5="$(openssl md5 -binary "$BACKUP_DIR/$BASE.$ext" | openssl base64 -A)"
  AZ blob upload -c "$CONTAINER" -n "$BLOB_DIR/$BASE.$ext" \
    --file "$BACKUP_DIR/$BASE.$ext" --overwrite false --content-md5 "$MD5" 2>/dev/null | tail -1
done

echo "==> verifying remote objects (existence, size, version)"
for ext in dump sha256 metadata.json; do
  AZ blob show -c "$CONTAINER" -n "$BLOB_DIR/$BASE.$ext" \
    --query '{name:name,size:properties.contentLength,version:versionId,md5:properties.contentSettings.contentMd5}' -o json 2>/dev/null
done

echo "==> verifying uploaded bytes (re-download .dump, compare SHA-256)"
AZ blob download -c "$CONTAINER" -n "$BLOB_DIR/$BASE.dump" \
  -f /tmp/az-$BASE.roundtrip.dump --overwrite true 2>/dev/null
GOT="$(shasum -a 256 /tmp/az-$BASE.roundtrip.dump | awk '{print $1}')"
WANT="$(awk '{print $1}' "$BACKUP_DIR/$BASE.sha256")"
rm -f /tmp/az-$BASE.roundtrip.dump
[ "$GOT" = "$WANT" ] || { echo "FAIL: round-trip SHA-256 mismatch (got $GOT, want $WANT)" >&2; exit 1; }
echo "OK: round-trip SHA-256 matches ($WANT)"

AZ blob download -c "$CONTAINER" -n "$BLOB_DIR/$BASE.sha256" -f /tmp/az-$BASE.sha256 --overwrite true 2>/dev/null
cmp -s "$BACKUP_DIR/$BASE.sha256" /tmp/az-$BASE.sha256 || { echo "FAIL: sha256 sidecar mismatch local vs Azure" >&2; exit 1; }
rm -f /tmp/az-$BASE.sha256
echo "UPLOAD PASS: 3/3 objects in Azure (checksum sidecar matches local) → $BLOB_DIR/$BASE.dump"
