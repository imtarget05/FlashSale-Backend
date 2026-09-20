#!/usr/bin/env bash
# Artifact validation WITHOUT restoring (Phase 3A): existence, size, checksum,
# archive listability, expected tables present.
set -euo pipefail
ARTIFACT="${1:?usage: verify-backup.sh <artifact.dump>}"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-01-flashsale-backend}"
PG_CONTAINER="${PG_CONTAINER:-${COMPOSE_PROJECT}-postgres-1}"
[ -f "$ARTIFACT" ] || { echo "FAIL: missing $ARTIFACT" >&2; exit 1; }
[ -s "$ARTIFACT" ] || { echo "FAIL: empty $ARTIFACT" >&2; exit 1; }
DIR="$(dirname "$ARTIFACT")"; BASE="$(basename "$ARTIFACT" .dump)"
if [ -f "$DIR/${BASE}.sha256" ]; then
  (cd "$DIR" && shasum -a 256 -c "${BASE}.sha256") || exit 1
else
  echo "WARN: no .sha256 sidecar; computing ad-hoc."
  shasum -a 256 "$ARTIFACT"
fi
docker cp "$ARTIFACT" "$PG_CONTAINER:/tmp/verify.dump"
LISTING="$(docker exec "$PG_CONTAINER" pg_restore --list /tmp/verify.dump)"
docker exec "$PG_CONTAINER" rm -f /tmp/verify.dump /tmp/v.dump
for t in 'TABLE public Products' 'TABLE public Orders' '__EFMigrationsHistory'; do
  echo "$LISTING" | grep -q "$t" || { echo "FAIL: archive lacks $t" >&2; exit 1; }
  echo "OK: archive contains $t"
done
echo "VERIFY PASS: $ARTIFACT"
