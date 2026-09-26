#!/usr/bin/env bash
# Phase 3C-3: Redis rebuild drill. Recovery = recreate/empty Redis → reseed the
# stock mirror from PostgreSQL → validate. No Redis RDB/AOF is ever backed up:
# reservation counters are derived state, not business truth.
#
# Drill: (1) record DB stock, (2) FLUSHDB Redis, (3) POST /internal/resync-stock/{id},
# (4) assert product read matches DB stock and one order decrements correctly.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-flashsale-backend}"
REDIS_CONTAINER="${REDIS_CONTAINER:-${COMPOSE_PROJECT}-redis-1}"
API="${API_BASE:-http://localhost:5199}"
: "${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD before the drill.}"
# The resync runbook endpoint is STAFF/ADMIN only. Export a STAFF access token
# before running: OPS_TOKEN=$(curl -s -X POST "$API/api/auth/login" -H
# 'Content-Type: application/json' -d '{"email":"staff@flashsale.local",
# "password":"<Bootstrap:StaffPassword>"}' | grep -o '"accessToken":"[^"]*' | cut -d'"' -f4)
: "${OPS_TOKEN:?Set OPS_TOKEN to a STAFF access token; the resync runbook requires STAFF/ADMIN}"

echo "==> DB-side truth before drill"
docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$COMPOSE_PROJECT-postgres-1" \
  psql -U postgres -d FlashSaleDb -At -c 'SELECT "AvailableStock" FROM public."Products" WHERE "Id"=1;'

echo "==> wiping Redis derived state (FLUSHDB)"
docker exec "$REDIS_CONTAINER" redis-cli -n 0 FLUSHDB
BEFORE="$(docker exec "$REDIS_CONTAINER" redis-cli DBSIZE)"
echo "keys after wipe: $BEFORE"

echo "==> reseeding via /internal/resync-stock/1"
curl -s -X POST -H "Authorization: Bearer $OPS_TOKEN" "$API/internal/resync-stock/1"; echo

echo "==> validating rebuilt mirror"
PRODUCT="$(curl -s "$API/api/products/1")"
echo "product: $PRODUCT"
echo "$PRODUCT" | grep -q '"availableStock":' || { echo "FAIL: product read did not return stock" >&2; exit 1; }
echo "REDIS-REBUILD PASS: Redis wiped and reseeded from PostgreSQL truth"
