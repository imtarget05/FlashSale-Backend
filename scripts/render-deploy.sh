#!/usr/bin/env bash
set -Eeuo pipefail

# Render deploy automation for a service with a valid API key.
# Usage:
#   RENDER_API_KEY=... RENDER_PREVIEW_SERVICE_ID=... ./scripts/render-deploy.sh preview
#   RENDER_API_KEY=... RENDER_PRODUCTION_SERVICE_ID=... ./scripts/render-deploy.sh production
#
# The script intentionally does NOT store secrets in git.
# Credentials must come from environment variables or a secret manager.

TARGET="${1:-preview}"
if [[ "$TARGET" != "preview" && "$TARGET" != "production" ]]; then
  echo "Usage: $0 [preview|production]" >&2
  exit 1
fi

RENDER_API_KEY="${RENDER_API_KEY:?Set RENDER_API_KEY before running this script.}"

case "$TARGET" in
  preview)
    SERVICE_ID="${RENDER_PREVIEW_SERVICE_ID:?Set RENDER_PREVIEW_SERVICE_ID before running preview deploy.}"
    ;;
  production)
    SERVICE_ID="${RENDER_PRODUCTION_SERVICE_ID:?Set RENDER_PRODUCTION_SERVICE_ID before running production deploy.}"
    ;;
  *)
    echo "Unsupported target: $TARGET" >&2
    exit 1
    ;;
esac

curl -sS -X POST "https://api.render.com/v1/services/${SERVICE_ID}/deploys" \
  -H "Authorization: Bearer ${RENDER_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"clearCache": false}'

echo

echo "Triggered Render deploy for service ${SERVICE_ID} (${TARGET})."
echo "Monitor the deployment in Render dashboard logs and wait for the service to become healthy before promoting to production."
