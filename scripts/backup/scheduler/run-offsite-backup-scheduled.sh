#!/usr/bin/env bash
# Scheduled entrypoint for the off-site backup (Phase 3B, Step 20).
# Designed to be invoked by launchd (macOS) or cron. No secrets are stored
# here: POSTGRES_PASSWORD is read from the git-ignored .env file.
#
# IMPORTANT: this only produces an RPO guarantee while the scheduler host is
# powered on and awake. See docs/runbooks/offsite-backup-scheduling.md.
set -euo pipefail
# launchd/cron start with a minimal PATH: docker (Docker Desktop) and az
# (Homebrew) must be reachable explicitly, otherwise the job fails silently.
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
LOG_DIR="${LOG_DIR:-$ROOT/logs}"
mkdir -p "$LOG_DIR"
LOG="$LOG_DIR/offsite-backup.log"

if [ -f "$ROOT/.env" ]; then
  # shellcheck disable=SC1091
  set -a; . "$ROOT/.env"; set +a
fi
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD missing: create $ROOT/.env (git-ignored)}"
export COMPOSE_PROJECT="${COMPOSE_PROJECT:-flashsale-backend}"
export STORAGE_ACCOUNT="${STORAGE_ACCOUNT:-stflashsalebackup}"
export CONTAINER="${CONTAINER:-postgres-backups}"

{
  echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) scheduled off-site backup start ==="
  "$ROOT/scripts/backup/run-offsite-backup.sh"
  echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) scheduled off-site backup OK ==="
} >> "$LOG" 2>&1
