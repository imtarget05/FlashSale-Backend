#!/usr/bin/env bash
# Phase 3B off-site backup orchestration (Step 10):
#   1. create local backup (Phase 3A script — untouched)
#   2. validate artifact (verify-backup.sh)
#   3. upload .dump/.sha256/.metadata.json to Azure (Entra auth)
#   4. verify Azure objects exist
# Reports success ONLY after remote verification completes.
# The local staging copy is NEVER deleted here (quarantine is manual/step 16).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
: "${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD (source .env) before off-site backup.}"

echo "== [1/4] local backup =="
"$ROOT/scripts/backup/postgres-backup.sh"
BASE="$(ls -t "$ROOT/backups" | grep '\.dump$' | head -1 | sed 's/\.dump$//')"
echo "backup set: $BASE"

echo "== [2/4] artifact validation =="
"$ROOT/scripts/backup/verify-backup.sh" "$ROOT/backups/$BASE.dump"

echo "== [3/4] Azure upload =="
"$ROOT/scripts/backup/azure-upload-backup.sh" "$BASE"

echo "== [4/4] OFF-SITE BACKUP PASS: $BASE available in Azure =="
