#!/usr/bin/env bash
# Deploy FlashSale payment-service to Render (backend) + Cloudflare Pages (frontend).
# Requires environment variables:
#   RENDER_DEPLOY_HOOK_PREVIEW   - Render deploy hook URL for preview
#   RENDER_DEPLOY_HOOK_PRODUCTION - Render deploy hook URL for production
#   RENDER_PREVIEW_URL           - Preview service base URL
#   RENDER_PRODUCTION_URL        - Production service base URL
#   CLOUDFLARE_API_TOKEN          - Cloudflare API token
#   CLOUDFLARE_ACCOUNT_ID        - Cloudflare account ID
#   VITE_API_URL                 - API URL for frontend build
#
# Usage:
#   ./scripts/deploy.sh preview   - Deploy to preview
#   ./scripts/deploy.sh production - Deploy to production (requires approval)
set -euo pipefail

ENV="${1:-preview}"

if [[ "$ENV" != "preview" && "$ENV" != "production" ]]; then
  echo "Usage: $0 {preview|production}" >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "=== FlashSale deploy: $ENV ==="

# 1. Run local CI gate first
echo "[1/4] Running local CI gate..."
"$REPO_ROOT/scripts/ci-gate.sh"

# 2. Build frontend
echo "[2/4] Building frontend..."
cd "$REPO_ROOT"
npm ci --prefix frontend
if [[ "$ENV" == "preview" ]]; then
  VITE_API_URL="${VITE_API_URL:-https://flashsale-payment-api-preview.onrender.com}" \
    VITE_ENV=preview npm run build --prefix frontend
else
  VITE_API_URL="${VITE_API_URL:-https://flashsale-payment-api-prod.onrender.com}" \
    VITE_ENV=production npm run build --prefix frontend
fi

# 3. Deploy backend to Render
echo "[3/4] Deploying backend to Render..."
if [[ "$ENV" == "preview" ]]; then
  if [[ -z "${RENDER_DEPLOY_HOOK_PREVIEW:-}" ]]; then
    echo "RENDER_DEPLOY_HOOK_PREVIEW is not set" >&2
    exit 1
  fi
  curl -fsS -X POST "$RENDER_DEPLOY_HOOK_PREVIEW"
  echo "Preview backend deploy triggered. URL: ${RENDER_PREVIEW_URL:-not set}"
else
  if [[ -z "${RENDER_DEPLOY_HOOK_PRODUCTION:-}" ]]; then
    echo "RENDER_DEPLOY_HOOK_PRODUCTION is not set" >&2
    exit 1
  fi
  echo "Production deploy requires manual approval in GitHub Environments."
  echo "Triggering deploy hook..."
  curl -fsS -X POST "$RENDER_DEPLOY_HOOK_PRODUCTION"
  echo "Production backend deploy triggered. URL: ${RENDER_PRODUCTION_URL:-not set}"
fi

# 4. Deploy frontend to Cloudflare Pages
echo "[4/4] Deploying frontend to Cloudflare Pages..."
if [[ -z "${CLOUDFLARE_API_TOKEN:-}" || -z "${CLOUDFLARE_ACCOUNT_ID:-}" ]]; then
  echo "CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID must be set" >&2
  exit 1
fi
npx wrangler pages deploy frontend/dist \
  --project-name=flashsale-frontend \
  --branch="$ENV" \
  --commit-dirty=true

echo "=== Deploy complete: $ENV ==="
echo "Backend: ${RENDER_PREVIEW_URL:-${RENDER_PRODUCTION_URL:-not set}}"
echo "Frontend: https://flashsale-frontend.pages.dev"
