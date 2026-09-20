#!/usr/bin/env bash
# PostgreSQL logical backup (Phase 3A): pg_dump -Fc inside the compose
# postgres container, artifact copied to ./backups/ with metadata.
# Password is NEVER hard-coded: read POSTGRES_PASSWORD from env (or .env).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BACKUP_DIR="${BACKUP_DIR:-$ROOT/backups}"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-01-flashsale-backend}"
PG_CONTAINER="${PG_CONTAINER:-${COMPOSE_PROJECT}-postgres-1}"
PGDATABASE="${POSTGRES_DB:-FlashSaleDb}"
PGUSER="${POSTGRES_USER:-postgres}"
: "${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD (source .env) before backup.}"
TS="$(date -u +%Y%m%dT%H%M%SZ)"
BASE="flashsale_${TS}"
mkdir -p "$BACKUP_DIR"

echo "==> pg_dump -Fc $PGDATABASE (container $PG_CONTAINER)"
docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
  pg_dump -U "$PGUSER" -d "$PGDATABASE" -Fc -f "/tmp/${BASE}.dump"
docker cp "$PG_CONTAINER:/tmp/${BASE}.dump" "$BACKUP_DIR/${BASE}.dump"
docker exec "$PG_CONTAINER" rm -f "/tmp/${BASE}.dump"

(
  cd "$BACKUP_DIR"
  shasum -a 256 "${BASE}.dump" > "${BASE}.sha256"
  PGVER="$(docker exec "$PG_CONTAINER" postgres --version | awk '{print $3}')"
  SIZE="$(wc -c < "${BASE}.dump" | tr -d ' ')"
  MIGRATIONS="$(docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$PG_CONTAINER" \
    psql -U "$PGUSER" -d "$PGDATABASE" -At -c 'SELECT string_agg("MigrationId", CHR(44) ORDER BY "MigrationId") FROM "__EFMigrationsHistory";')"
  cat > "${BASE}.metadata.json" <<EOF
{
  "timestamp_utc": "$TS",
  "database": "$PGDATABASE",
  "postgres_version": "$PGVER",
  "format": "pg_dump custom (-Fc)",
  "file": "${BASE}.dump",
  "size_bytes": $SIZE,
  "sha256_file": "${BASE}.sha256",
  "migrations": "$MIGRATIONS",
  "source": "compose-postgres",
  "phase": "3A-local"
}
EOF
)
echo "==> wrote $BACKUP_DIR/${BASE}.dump (+ .sha256, .metadata.json)"
