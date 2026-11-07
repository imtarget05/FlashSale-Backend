#!/usr/bin/env bash
set -Eeuo pipefail

# Combined cloud operations helper.
# Use this on a machine that has valid Render and ArgoCD access.
# Secrets must be provided through environment variables or a secure secret manager.

usage() {
  cat <<'EOF'
Usage:
  ./scripts/cloud-ops.sh render-preview
  ./scripts/cloud-ops.sh render-production
  ./scripts/cloud-ops.sh argocd-sync
  ./scripts/cloud-ops.sh argocd-status

Environment variables:
  RENDER_API_KEY
  RENDER_PREVIEW_SERVICE_ID
  RENDER_PRODUCTION_SERVICE_ID
  ARGOCD_SERVER
  ARGOCD_USERNAME
  ARGOCD_PASSWORD
  APP_NAME (default: aks-sre-platform)
  REVISION (default: main)
EOF
}

cmd="${1:-}"
case "$cmd" in
  render-preview)
    RENDER_API_KEY="${RENDER_API_KEY:?Set RENDER_API_KEY before running render-preview.}"
    RENDER_PREVIEW_SERVICE_ID="${RENDER_PREVIEW_SERVICE_ID:?Set RENDER_PREVIEW_SERVICE_ID before running render-preview.}"
    curl -sS -X POST "https://api.render.com/v1/services/${RENDER_PREVIEW_SERVICE_ID}/deploys" \
      -H "Authorization: Bearer ${RENDER_API_KEY}" \
      -H "Content-Type: application/json" \
      -d '{"clearCache": false}'
    echo
    echo "Render preview deployment triggered."
    ;;
  render-production)
    RENDER_API_KEY="${RENDER_API_KEY:?Set RENDER_API_KEY before running render-production.}"
    RENDER_PRODUCTION_SERVICE_ID="${RENDER_PRODUCTION_SERVICE_ID:?Set RENDER_PRODUCTION_SERVICE_ID before running render-production.}"
    curl -sS -X POST "https://api.render.com/v1/services/${RENDER_PRODUCTION_SERVICE_ID}/deploys" \
      -H "Authorization: Bearer ${RENDER_API_KEY}" \
      -H "Content-Type: application/json" \
      -d '{"clearCache": false}'
    echo
    echo "Render production deployment triggered."
    ;;
  argocd-sync)
    APP_NAME="${APP_NAME:-aks-sre-platform}"
    REVISION="${REVISION:-main}"
    ARGOCD_SERVER="${ARGOCD_SERVER:?Set ARGOCD_SERVER before running argocd-sync.}"
    ARGOCD_USERNAME="${ARGOCD_USERNAME:?Set ARGOCD_USERNAME before running argocd-sync.}"
    ARGOCD_PASSWORD="${ARGOCD_PASSWORD:?Set ARGOCD_PASSWORD before running argocd-sync.}"
    argocd login "$ARGOCD_SERVER" \
      --username "$ARGOCD_USERNAME" \
      --password "$ARGOCD_PASSWORD" \
      --insecure
    argocd app set "$APP_NAME" --revision "$REVISION"
    argocd app sync "$APP_NAME" --retry-limit 5
    argocd app wait "$APP_NAME" --health --timeout 600
    argocd app status "$APP_NAME"
    ;;
  argocd-status)
    APP_NAME="${APP_NAME:-aks-sre-platform}"
    ARGOCD_SERVER="${ARGOCD_SERVER:?Set ARGOCD_SERVER before running argocd-status.}"
    ARGOCD_USERNAME="${ARGOCD_USERNAME:?Set ARGOCD_USERNAME before running argocd-status.}"
    ARGOCD_PASSWORD="${ARGOCD_PASSWORD:?Set ARGOCD_PASSWORD before running argocd-status.}"
    argocd login "$ARGOCD_SERVER" \
      --username "$ARGOCD_USERNAME" \
      --password "$ARGOCD_PASSWORD" \
      --insecure
    argocd app status "$APP_NAME"
    argocd app health "$APP_NAME"
    ;;
  -h|--help|help|"")
    usage
    exit 0
    ;;
  *)
    echo "Unknown command: $cmd" >&2
    usage >&2
    exit 1
    ;;
esac
