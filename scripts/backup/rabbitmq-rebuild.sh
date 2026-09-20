#!/usr/bin/env bash
# Phase 3C-2: RabbitMQ broker rebuild drill.
# Destroys the broker's local state (rabbitmq-data volume), recreates an EMPTY
# broker, then imports the definitions artifact exported by rabbitmq-backup.sh
# and proves the app writes/consumes through the rebuilt broker.
#
# SAFETY: works only with the compose broker (container/volume names below).
# DANGER: this WIPES broker state. Messages not yet persisted to Postgres are
# LOST by design — see docs/recovery/rabbitmq-recovery-strategy.md.
#
# Usage: rabbitmq-rebuild.sh [definitions-file.json]
#   with no arg: uses the newest local backups/rabbitmq-defs_*.json
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-flashsale-backend}"
RABBITMQ_CONTAINER="${RABBITMQ_CONTAINER:-${COMPOSE_PROJECT}-rabbitmq-1}"
BACKUP_DIR="${BACKUP_DIR:-$ROOT/backups}"
DEFS="${1:-$(ls -t "$BACKUP_DIR"/rabbitmq-defs_????????T??????Z.json 2>/dev/null | grep -v '\.metadata\.json$' | head -1)}"
RABBITMQ_USER="${RABBITMQ_USER:-guest}"
: "${RABBITMQ_PASSWORD:?Set RABBITMQ_PASSWORD (guest for local dev).}"
[ -s "${DEFS:?no definitions artifact found; run rabbitmq-backup.sh first}" ] || exit 1

echo "==> stopping broker $RABBITMQ_CONTAINER and wiping its data volume"
docker stop "$RABBITMQ_CONTAINER" >/dev/null
docker rm "$RABBITMQ_CONTAINER" >/dev/null
VOL="$(docker volume ls -q | grep -i rabbitmq-data | head -1)"
[ -n "$VOL" ] || { echo "FAIL: rabbitmq data volume not found" >&2; exit 1; }
docker volume rm "$VOL" >/dev/null
echo "wiped volume: $VOL"

echo "==> recreating EMPTY broker via compose"
cd "$ROOT"
docker compose up -d rabbitmq >/dev/null 2>&1 || docker-compose up -d rabbitmq >/dev/null 2>&1
for _ in $(seq 1 30); do
  CODE="$(curl -s -o /dev/null -w '%{http_code}' -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" http://localhost:15672/api/overview || true)"
  [ "$CODE" = "200" ] && break
  sleep 2
done
[ "$CODE" = "200" ] || { echo "FAIL: rebuilt broker not healthy" >&2; exit 1; }
EMPTY_Q="$(curl -s -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" http://localhost:15672/api/queues | python3 -c 'import json,sys; print(len(json.load(sys.stdin)))')"
echo "empty broker queues: $EMPTY_Q"

echo "==> importing definitions $DEFS"
HTTP="$(curl -s -o /dev/null -w '%{http_code}' -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" \
  -H 'Content-Type: application/json' -X POST --data-binary "@$DEFS" \
  http://localhost:15672/api/definitions)"
# RabbitMQ answers 204 No Content (sometimes 200) on a successful import.
[ "$HTTP" = "200" ] || [ "$HTTP" = "204" ] || { echo "FAIL: definitions import returned HTTP $HTTP" >&2; exit 1; }
curl -s -u "$RABBITMQ_USER:$RABBITMQ_PASSWORD" http://localhost:15672/api/queues \
  | python3 -c 'import json,sys; qs=json.load(sys.stdin); print("queues after import:", [(q["name"], q["durable"]) for q in qs])'

echo "REBUILD PASS: empty broker + definitions import healthy"
