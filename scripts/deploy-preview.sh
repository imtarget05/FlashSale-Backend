#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/frontend"
DEPLOY_ENV="${1:-preview}"
RENDER_SERVICE="${RENDER_SERVICE:-flashsale-payment-api}"
CLOUDFLARE_PROJECT="${CLOUDFLARE_PROJECT:-flashsale-frontend}"

cd "$REPO_ROOT"

if [[ ! -d "$FRONTEND_DIR" ]]; then
  echo "Frontend app not found at $FRONTEND_DIR"
  exit 1
fi

# Frontend build
cd "$FRONTEND_DIR"
if [[ -f package.json ]]; then
  npm ci
  VITE_API_URL="${VITE_API_URL:-http://localhost:8081}" npm run build
fi

cd "$REPO_ROOT"

echo "Preview build complete. Next steps:"
echo "  1) Render: git push main and deploy the web service for $RENDER_SERVICE"
echo "  2) Cloudflare Pages: create a project named $CLOUDFLARE_PROJECT and upload dist/ or connect the repo"
echo "  3) Set env vars:"
echo "     - VITE_API_URL=https://<render-service>.onrender.com"
echo "     - VITE_ENV=$DEPLOY_ENV"
echo ""
