#!/usr/bin/env bash
# Bootstrap the 1-day UNLOCKED time-based immutability policy on the backup
# container. Out-of-band from Terraform due to the azurerm 3.x provider hang
# during Phase 3B (documented in infrastructure/terraform/backup-storage/main.tf).
# NEVER pass --lock; locked policies cannot be unlocked or shortened.
set -euo pipefail
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:-stflashsalebackup}"
CONTAINER="${CONTAINER:-postgres-backups}"
PERIOD="${IMMUTABILITY_DAYS:-1}"

if az storage container immutability-policy show \
     --account-name "$STORAGE_ACCOUNT" --container-name "$CONTAINER" \
     -o json 2>/dev/null | grep -q immutabilityPeriodSinceCreationInDays; then
  echo "OK: immutability policy already exists on $CONTAINER (UNLOCKED, ${PERIOD}d)"
  exit 0
fi
az storage container immutability-policy create \
  --account-name "$STORAGE_ACCOUNT" --container-name "$CONTAINER" --period "$PERIOD" -o json
echo "BOOTSTRAP PASS: UNLOCKED ${PERIOD}-day immutability policy on $CONTAINER"
