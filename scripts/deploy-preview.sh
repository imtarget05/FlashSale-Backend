#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY_ENV="${1:-preview}"
CLOUDFLARE_PROJECT="${CLOUDFLARE_PROJECT:-flashsale-frontend}"

case "$DEPLOY_ENV" in
  preview)
    BRANCH=preview
    API_URL="${VITE_API_URL:-https://flashsale-payment-api-preview.onrender.com}"
    ;;
  production)
    BRANCH=main
    API_URL="${VITE_API_URL:-https://flashsale-payment-api-prod.onrender.com}"
    ;;
  *)
    echo "Usage: $0 [preview|production]" >&2
    exit 2
    ;;
esac

cd "$REPO_ROOT"
npm ci --prefix frontend
VITE_API_URL="$API_URL" VITE_ENV="$DEPLOY_ENV" npm run build --prefix frontend

echo "Build complete for $DEPLOY_ENV."
echo "To deploy Cloudflare Pages, run:"
echo "  npx wrangler pages deploy frontend/dist --project-name=$CLOUDFLARE_PROJECT --branch=$BRANCH"
echo "This script only builds; it does not deploy Render or Cloudflare resources."
