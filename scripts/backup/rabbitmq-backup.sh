#!/usr/bin/env bash
# Phase 3C-1: export RabbitMQ broker DEFINITIONS (users/vhosts/queues/
# exchanges/bindings/policies — NOT message bodies) via the management API and
# upload the artifact to the same off-host Azure Blob account that holds the
# Postgres backups. Never from a running broker's message store: definitions
# export is a metadata snapshot, safe to take online.
#
# Usage: rabbitmq-backup.sh  → prints and echoes the Azure blob path.
# Requires: RABBITMQ_USER/RABBITMQ_PASSWORD (defaults guest/guest for local dev).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BACKUP_DIR="${BACKUP_DIR:-$ROOT/backups}"
RABBITMQ_HOST="${RABBITMQ_HOST:-localhost}"
RABBITMQ_MGMT_PORT="${RABBITMQ_MGMT_PORT:-15672}"
RABBITMQ_USER="${RABBITMQ_USER:-guest}"
: "${RABBITMQ_PASSWORD:?Set RABBITMQ_PASSWORD (guest for local dev) before export.}"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:-stflashsalebackup}"
CONTAINER="${CONTAINER:-postgres-backups}"
AZ() { az storage "$@" --auth-mode login --account-name "$STORAGE_ACCOUNT"; }

TS="$(date -u +%Y%m%dT%H%M%SZ)"
BASE="rabbitmq-defs_${TS}"
mkdir -p "$BACKUP_DIR"

echo "==> exporting RabbitMQ definitions from $RABBITMQ_HOST:$RABBITMQ_MGMT_PORT"
HTTP="$(curl -s -o "$BACKUP_DIR/$BASE.json" -w '%{http_code}' \
  -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" \
  "http://$RABBITMQ_HOST:$RABBITMQ_MGMT_PORT/api/definitions")"
[ "$HTTP" = "200" ] || { echo "FAIL: definitions export returned HTTP $HTTP" >&2; exit 1; }
[ -s "$BACKUP_DIR/$BASE.json" ] || { echo "FAIL: empty definitions file" >&2; exit 1; }

python3 - "$BACKUP_DIR/$BASE.json" <<'EOF'
import json, sys
d = json.load(open(sys.argv[1]))
for key in ("rabbit_version", "rabbitmq_version", "product_info"):
    if key in d:
        print(f"broker: {d[key]}")
        break
print(f"queues={len(d.get('queues', []))} exchanges={len(d.get('exchanges', []))} "
      f"bindings={len(d.get('bindings', []))} users={len(d.get('users', []))} "
      f"vhosts={len(d.get('vhosts', []))} policies={len(d.get('policies', []))}")
EOF

(
  cd "$BACKUP_DIR"
  shasum -a 256 "$BASE.json" > "$BASE.sha256"
  SIZE="$(wc -c < "$BASE.json" | tr -d ' ')"
  cat > "$BASE.metadata.json" <<EOF
{
  "timestamp_utc": "$TS",
  "kind": "rabbitmq-definitions",
  "broker": "$RABBITMQ_HOST:$RABBITMQ_MGMT_PORT",
  "file": "$BASE.json",
  "size_bytes": $SIZE,
  "sha256_file": "$BASE.sha256",
  "phase": "3C-rabbitmq"
}
EOF
)

echo "==> uploading definitions to $STORAGE_ACCOUNT/$CONTAINER/rabbitmq/$TS/ (Entra auth)"
for ext in json sha256 metadata.json; do
  MD5="$(openssl md5 -binary "$BACKUP_DIR/$BASE.$ext" | openssl base64 -A)"
  AZ blob upload -c "$CONTAINER" -n "rabbitmq/$TS/$BASE.$ext" \
    --file "$BACKUP_DIR/$BASE.$ext" --overwrite false --content-md5 "$MD5" 2>/dev/null | tail -1
done
for ext in json sha256 metadata.json; do
  AZ blob show -c "$CONTAINER" -n "rabbitmq/$TS/$BASE.$ext" \
    --query '{name:name,size:properties.contentLength,version:versionId}' -o json 2>/dev/null
done
echo "RABBITMQ-BACKUP PASS: rabbitmq/$TS/$BASE.json (+ .sha256, .metadata.json)"
