#!/usr/bin/env bash
# Installs the off-site backup scheduler for macOS (launchd).
#
# WHY AN INSTALL STEP: macOS TCC blocks launchd agents from reading protected
# folders such as ~/Downloads, so the job runs from a relocated copy of the
# backup scripts under ~/Library/Application Support. The copy is refreshed on
# every install and is byte-identical to scripts/backup/ in this repo.
#
# Usage:
#   install-offsite-scheduler.sh --daily     # daily 02:30 portfolio schedule
#   install-offsite-scheduler.sh --test      # every 90s (evidence capture only)
#   install-offsite-scheduler.sh --unload    # remove everything
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
INSTALL_DIR="$HOME/Library/Application Support/flashsale-offsite-backup"
PLIST_DIR="$HOME/Library/LaunchAgents"
DAILY_LABEL="com.flashsale.offsite-backup"
TEST_LABEL="com.flashsale.offsite-backup.test"
MODE="${1:?usage: install-offsite-scheduler.sh --daily|--test|--unload}"

if [ "$MODE" = "--unload" ]; then
  for L in "$DAILY_LABEL" "$TEST_LABEL"; do
    launchctl unload -w "$PLIST_DIR/$L.plist" 2>/dev/null || true
    rm -f "$PLIST_DIR/$L.plist"
  done
  echo "UNINSTALLED: no off-site backup scheduler loaded"
  exit 0
fi

echo "==> staging backup scripts → $INSTALL_DIR/repo"
mkdir -p "$INSTALL_DIR/repo/scripts" "$INSTALL_DIR/repo/backups" "$INSTALL_DIR/repo/logs"
rsync -a --delete "$REPO_ROOT/scripts/backup/" "$INSTALL_DIR/repo/scripts/backup/"
[ -f "$REPO_ROOT/.env" ] && cp "$REPO_ROOT/.env" "$INSTALL_DIR/repo/.env" && chmod 600 "$INSTALL_DIR/repo/.env"
chmod +x "$INSTALL_DIR"/repo/scripts/backup/*.sh "$INSTALL_DIR"/repo/scripts/backup/scheduler/*.sh

if [ "$MODE" = "--test" ]; then
  LABEL="$TEST_LABEL"; TEMPLATE="$REPO_ROOT/scripts/backup/scheduler/com.flashsale.offsite-backup.test.plist"
  launchctl unload -w "$PLIST_DIR/$DAILY_LABEL.plist" 2>/dev/null || true; rm -f "$PLIST_DIR/$DAILY_LABEL.plist"
else
  LABEL="$DAILY_LABEL"; TEMPLATE="$REPO_ROOT/scripts/backup/scheduler/com.flashsale.offsite-backup.plist"
  launchctl unload -w "$PLIST_DIR/$TEST_LABEL.plist" 2>/dev/null || true; rm -f "$PLIST_DIR/$TEST_LABEL.plist"
fi

sed "s|__INSTALL_DIR__|$INSTALL_DIR|g" "$TEMPLATE" > "$PLIST_DIR/$LABEL.plist"
launchctl unload -w "$PLIST_DIR/$LABEL.plist" 2>/dev/null || true
launchctl load -w "$PLIST_DIR/$LABEL.plist"
echo "INSTALLED ($MODE): $LABEL → $INSTALL_DIR/repo/scripts/backup/run-offsite-backup.sh"
echo "Logs: $INSTALL_DIR/repo/logs/offsite-backup.log"
