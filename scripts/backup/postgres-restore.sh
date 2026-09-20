#!/usr/bin/env bash
# Restore guard-railed pg_restore (Phase 3A): NEW DATABASE ONLY by default.
# Refuses target FlashSaleDb unless ALLOW_OVERWRITE_SOURCE=yes is explicit.
set -euo pipefail
ARTIFACT="${1:?usage: postgres-restore.sh <artifact.dump> [target-db]}"
TARGET_DB="${2:-FlashSaleRestoreDrill}"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-01-flashsale-backend}"
PG_CONTAINER="${PG_CONTAINER:-${COMPOSE_PROJECT}-postgres-1}"
PGUSER="${POSTGRES_USER:-postgres}"
: "${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD before restore.}"
if [ "$TARGET_DB" = "${POSTGRES_DB:-FlashSaleDb}" ] && [ "${ALLOW_OVERWRITE_SOURCE:-no}" != "yes" ]; then
  echo "REFUSED: target is the live database. Set ALLOW_OVERWRITE_SOURCE=yes to override." >&2
  exit 2
fi
[ -s "$ARTIFACT" ] || { echo "REFUSED: artifact missing/empty: $ARTIFACT" >&2; exit 2; }

echo "==> validating archive $ARTIFACT"
docker cp "$ARTIFACT" "$PG_CONTAINER:/tmp/restore.dump"
LISTING="$(docker exec "$PG_CONTAINER" pg_restore --list /tmp/restore.dump)"
echo "$LISTING" | grep -q 'TABLE public Orders' \
  || { echo "REFUSED: archive lacks Orders table." >&2; exit 2; }

echo "==> creating clean target $TARGET_DB"
docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
  psql -U "$PGUSER" -d postgres -c "DROP DATABASE IF EXISTS \"$TARGET_DB\";"
docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
  psql -U "$PGUSER" -d postgres -c "CREATE DATABASE \"$TARGET_DB\";"

echo "==> pg_restore into $TARGET_DB"
docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
  pg_restore -U "$PGUSER" -d "$TARGET_DB" --no-owner --role="$PGUSER" /tmp/restore.dump
docker exec "$PG_CONTAINER" rm -f /tmp/restore.dump
echo "==> restore complete: $TARGET_DB"
