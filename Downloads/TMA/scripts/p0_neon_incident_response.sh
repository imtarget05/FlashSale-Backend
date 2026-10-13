#!/bin/bash
# P0 Neon Credential Rotation — Runbook
# Execute: export NEON_API_KEY='your_new_key' && bash scripts/p0_neon_incident_response.sh

set -euo pipefail

# Configuration
TMA_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUPPORTDESK_DIR="$TMA_ROOT/SupportDesk"
SMARTDOC_DIR="$TMA_ROOT/Smart-Document-Chatbot"

echo "═══════════════════════════════════════════════════════════════════"
echo " P0 Neon Credential Rotation — Runbook"
echo "═══════════════════════════════════════════════════════════════════"

# ── PREREQUISITES ───────────────────────────────────────────────────────
if [[ -z "${NEON_API_KEY:-}" ]]; then
  echo "ERROR: NEON_API_KEY not set."
  echo "Please set it to a NEW key from Neon Console (Settings → API Keys):"
  echo "  export NEON_API_KEY='napi_yOUR_NEW_KEY'"
  exit 1
fi
echo "→ Using NEON_API_KEY: ${#NEON_API_KEY} chars (not displayed)"

# ── STEP 1: Discover project and branch ─────────────────────────────────
echo ""
echo "── Step 1: Discover Neon project/branch IDs"

PROJECTS_JSON=$(curl -sSf -H "Authorization: Bearer $NEON_API_KEY" \
  "https://api.neon.tech/api/v2/projects?limit=50")

NEON_PROJECT_ID=$(echo "$PROJECTS_JSON" | jq -r '.projects[0].id')
echo "  Project ID: $NEON_PROJECT_ID"

BRANCHES_JSON=$(curl -sSf -H "Authorization: Bearer $NEON_API_KEY" \
  "https://api.neon.tech/api/v2/projects/$NEON_PROJECT_ID/branches?limit=20")

NEON_BRANCH_ID=$(echo "$BRANCHES_JSON" | jq -r '.branches[0].id')
echo "  Branch ID:  $NEON_BRANCH_ID"

# ── STEP 2: Reset neondb_owner password ─────────────────────────────────
echo ""
echo "── Step 2: Reset neondb_owner password"

ROLE="neondb_owner"
RESET_RESP=$(curl -sSf -X POST \
  -H "Authorization: Bearer $NEON_API_KEY" \
  -H "Content-Type: application/json" \
  "https://api.neon.tech/api/v2/projects/$NEON_PROJECT_ID/branches/$NEON_BRANCH_ID/roles/$ROLE/reset_password")

NEW_PASSWORD=$(echo "$RESET_RESP" | jq -r '.password')

if [[ "$NEW_PASSWORD" == "null" || -z "$NEW_PASSWORD" ]]; then
  echo "ERROR: Password reset failed"
  echo "Response: $RESET_RESP"
  exit 1
fi
echo "  ✓ New password generated (not displayed for security)"

# ── STEP 3: Get connection details ────────────────────────────────────────
echo ""
echo "── Step 3: Get Neon connection endpoint"

BRANCH_RESP=$(curl -sSf -H "Authorization: Bearer $NEON_API_KEY" \
  "https://api.neon.tech/api/v2/projects/$NEON_PROJECT_ID/branches/$NEON_BRANCH_ID")

DB_HOST=$(echo "$BRANCH_RESP" | jq -r '.branch.endpoint')
DB_HOST_DIRECT="${DB_HOST%%-pooler*}"
if [[ "$DB_HOST_DIRECT" == "$DB_HOST" ]]; then
  DB_HOST_DIRECT="$DB_HOST"
fi

echo "  Host: $DB_HOST_DIRECT"
echo "  Database: neondb"

JDBC_URL="jdbc:postgresql://${DB_HOST_DIRECT}:5432/neondb?sslmode=require&channel_binding=disable"
echo ""
echo "  Connection strings:"
echo "  SPRING_DATASOURCE_URL = $JDBC_URL"
echo "  SPRING_DATASOURCE_USERNAME = neondb_owner"
echo "  SPRING_DATASOURCE_PASSWORD = <MASKED>"

PG_URL="postgresql://neondb_owner:\${NEW_PASSWORD}@\${DB_HOST_DIRECT}:5432/neondb?sslmode=require"

echo ""
echo "  Database URL for psycopg2:"
echo "  DATABASE_URL=$PG_URL"

# ── STEP 4: Update local .env files ───────────────────────────────────────
echo ""
echo "── Step 4: Update local .env files"

SD_ENV="$SMARTDOC_DIR/.env"
if [[ -f "$SD_ENV" ]]; then
  echo "  Updating $SD_ENV"
  sed -i.bak "s|^SPRING_DATASOURCE_URL=.*$|SPRING_DATASOURCE_URL=$JDBC_URL|" "$SD_ENV"
  sed -i.bak "s|^NEON_DATABASE_URL=.*$|NEON_DATABASE_URL=$JDBC_URL|" "$SD_ENV"
  sed -i.bak "s|^POSTGRES_USER=.*$|POSTGRES_USER=neondb_owner|" "$SD_ENV"
  sed -i.bak "s|^POSTGRES_PASSWORD=.*$|POSTGRES_PASSWORD=\$NEW_PASSWORD|" "$SD_ENV"
  sed -i.bak "s|^NEON_POSTGRES_HOST=.*$|NEON_POSTGRES_HOST=$DB_HOST_DIRECT|" "$SD_ENV"
  sed -i.bak "s|^POSTGRES_DB=.*$|POSTGRES_DB=neondb|" "$SD_ENV"
  rm -f "${SD_ENV}.bak"
  echo "  ✓ Smart-Document-Chatbot .env updated"
fi

echo ""
echo "  SupportDesk .env (local) uses SQLite — no changes needed."
echo "  Update Render dashboard for SupportDesk production (kh02) with:"
echo "    DATABASE_URL=$PG_URL"

# ── STEP 5: Render dashboard instructions ───────────────────────────────────
echo ""
echo "── Step 5: Update Render secrets (MANUAL) ───────────────────────────"
echo ""
echo "  1. SupportDesk (kh02):"
echo "     - Render Dashboard → supportdesk-api → Environment"
echo "     - Set DATABASE_URL = $PG_URL"
echo ""
echo "  2. SmartDoc (2hhz):"
echo "     - Render Dashboard → smart-doc-backend → Environment"
echo "     - SPRING_DATASOURCE_URL = $JDBC_URL"
echo "     - SPRING_DATASOURCE_USERNAME = neondb_owner"
echo "     - SPRING_DATASOURCE_PASSWORD = <paste new password>"
echo ""
echo "  3. Click 'Restart' on both services after updating"

# ── STEP 6: Smoke tests ───────────────────────────────────────────────────
echo ""
echo "── Step 6: Smoke tests (after restart) ────────────────────────────────"
echo "  Waiting 90 seconds for services to restart..."
sleep 90

echo ""
echo "  Testing SupportDesk kh02 health..."
KH02=$(curl -sf --max-time 15 "https://supportdesk-api-kh02.onrender.com/api/health" 2>/dev/null || echo "UNREACHABLE")
echo "    → $KH02"

echo ""
echo "  Testing SmartDoc h4mt health..."
H4MT=$(curl -sf --max-time 15 "https://smart-doc-backend-h4mt.onrender.com/api/actuator/health" 2>/dev/null || echo "UNREACHABLE")
echo "    → $H4MT"

if echo "$KH02" | grep -q '"status":"ok"'; then
  echo "  ✓ SupportDesk kh02: HEALTHY"
else
  echo "  ⚠ SupportDesk kh02: Check Render logs"
fi

if echo "$H4MT" | grep -q '"UP"'; then
  echo "  ✓ SmartDoc h4mt: HEALTHY (boot + Flyway + DB OK)"
else
  echo "  ⚠ SmartDoc h4mt: Check Render logs"
fi

# ── STEP 7: Secret scan ─────────────────────────────────────────────────────
echo ""
echo "── Step 7: Secret scan ───────────────────────────────────────────────"

echo "  Scanning for leaked Neon API key prefix 'napi_s4fgl'..."
if grep -r "napi_s4fgl" "$TMA_ROOT" --include="*.env" 2>/dev/null; then
  echo "  ⚠ Found leaked key prefix in .env files - ensure it's revoked in Neon Console"
else
  echo "  ✓ No leaked Neon API key prefix found in .env files"
fi

echo ""
echo "═══════════════════════════════════════════════════════════════════"
echo " P0 Incident Response COMPLETE"
echo ""
echo "Next actions:"
echo "  1. Cloudflare Pages fix for SupportDesk"
echo "  2. Repo cleanup / dead-file audit"
echo "═══════════════════════════════════════════════════════════════════"