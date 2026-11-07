#!/usr/bin/env bash
set -Eeuo pipefail

# ArgoCD app sync automation.
# Usage:
#   export ARGOCD_SERVER=... ARGOCD_USERNAME=... ARGOCD_PASSWORD=...
#   export APP_NAME=aks-sre-platform REVISION=main
#   ./scripts/sync-argocd.sh
#
# The script expects valid cluster access and ArgoCD credentials to exist in the environment.

APP_NAME="${APP_NAME:-aks-sre-platform}"
REVISION="${REVISION:-main}"
ARGOCD_SERVER="${ARGOCD_SERVER:?Set ARGOCD_SERVER before running this script.}"
ARGOCD_USERNAME="${ARGOCD_USERNAME:?Set ARGOCD_USERNAME before running this script.}"
ARGOCD_PASSWORD="${ARGOCD_PASSWORD:?Set ARGOCD_PASSWORD before running this script.}"
ARGOCD_INSECURE="${ARGOCD_INSECURE:-true}"

if [[ "$ARGOCD_INSECURE" == "true" ]]; then
  argocd login "$ARGOCD_SERVER" \
    --username "$ARGOCD_USERNAME" \
    --password "$ARGOCD_PASSWORD" \
    --insecure
else
  argocd login "$ARGOCD_SERVER" \
    --username "$ARGOCD_USERNAME" \
    --password "$ARGOCD_PASSWORD"
fi

argocd app get "$APP_NAME" >/dev/null 2>&1 || {
  echo "Application '$APP_NAME' does not exist in ArgoCD." >&2
  exit 1
}

argocd app set "$APP_NAME" --revision "$REVISION"
argocd app sync "$APP_NAME" --retry-limit 5
argocd app wait "$APP_NAME" --health --timeout 600
argocd app status "$APP_NAME"
