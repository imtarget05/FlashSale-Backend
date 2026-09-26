#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# INTERVIEW RELEASE v1.0 — one-command demo.
#
# Walks the auth surface added in Phases I-IV against the REAL API:
#
#   register -> login -> who am I -> browse a product -> order
#     -> wait for Completed -> my orders -> refresh (rotate + reject replay)
#     -> logout -> metrics -> OpenAPI document
#
# Everything runs locally: docker compose supplies PostgreSQL/Redis/RabbitMQ,
# the API runs in the Production environment exactly as the image does.
#
# Usage:
#   bash docs/interview-demo/demo.sh
#
# Notes:
#   * Requires Docker; the script starts and stops the containers it needs.
#   * DEMO_PORT / DEMO_TEARDOWN / JWT_SIGNING_KEY can be overridden in the env.
#   * Set DEMO_TEARDOWN=1 to also stop the containers on exit.
# ---------------------------------------------------------------------------
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DEMO_PORT="${DEMO_PORT:-5099}"
BASE="http://127.0.0.1:${DEMO_PORT}"
LOG_FILE="${TMPDIR:-/tmp}/flashsale-demo-api.log"

# Local demo key only. Production reads `jwt-signing-key` from the
# `flashsale-secrets` Kubernetes Secret (ADR-013 §2); the app refuses to start
# in Production without a >= 32-byte key, which is why this is set explicitly.
export Auth__Jwt__SigningKey="${JWT_SIGNING_KEY:-demo-only-insecure-signing-key-change-me}"

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="$BASE"
export ConnectionStrings__DefaultConnection="${DEMO_POSTGRES:-Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres}"
export ConnectionStrings__Redis="${DEMO_REDIS:-localhost:6379}"
export ConnectionStrings__RabbitMQ="${DEMO_RABBITMQ:-amqp://guest:guest@localhost:5672/}"

PASS=0
FAIL=0

step()  { printf '\n\033[1;36m== %s\033[0m\n' "$*"; }
info()  { printf '   %s\n' "$*"; }
ok()    { printf '   \033[32mPASS\033[0m %s\n' "$*"; PASS=$((PASS+1)); }
bad()   { printf '   \033[31mFAIL\033[0m %s\n' "$*"; FAIL=$((FAIL+1)); }

expect() { # expect <label> <expected> <actual>
  if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (expected $2, got $3)"; fi
}

expect_contains() { # expect_contains <label> <needle> <haystack>
  if printf '%s' "$3" | grep -q -- "$2"; then ok "$1"; else bad "$1 (missing '$2')"; fi
}

# Pretty-print JSON when a tool is available; otherwise show it raw rather than
# failing — the demo must not depend on jq being installed.
pretty() {
  if command -v jq >/dev/null 2>&1; then jq . 2>/dev/null || cat
  elif command -v python3 >/dev/null 2>&1; then python3 -m json.tool 2>/dev/null || cat
  else cat
  fi
}

field() { printf '%s' "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | sed 's/.*":"//; s/"$//'; }

# HTTP status of a request whose body we do not need.
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

# JSON bodies are built into variables and passed as "$2" on purpose. Nesting
# escaped quotes inside a double-quoted "$( ... )" — e.g. -d "{\"email\":\"$X\"}"
# directly inside "$(status ...)" — is fragile and silently produced a body the
# server rejected with 400 while the same request via a variable returned 409.
post_json_status() { # post_json_status <path> <json> [extra curl args...]
  local path="$1" json="$2"; shift 2
  curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE$path" -H "$JSON_HEADER" -d "$json" "$@"
}

post_json() { # post_json <path> <json> [extra curl args...]
  local path="$1" json="$2"; shift 2
  curl -s -w '\n%{http_code}' -X POST "$BASE$path" -H "$JSON_HEADER" -d "$json" "$@"
}

API_PID=""
cleanup() {
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then
    kill "$API_PID" 2>/dev/null
    wait "$API_PID" 2>/dev/null
    info "stopped the API (pid $API_PID)"
  fi
  if [ "${DEMO_TEARDOWN:-0}" = "1" ]; then
    info "stopping containers (DEMO_TEARDOWN=1)"
    docker compose stop postgres redis rabbitmq >/dev/null 2>&1
  fi
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
step "0. Infrastructure"
# ---------------------------------------------------------------------------
if ! docker info >/dev/null 2>&1; then
  bad "Docker is not running — start Docker Desktop and re-run."
  exit 1
fi

docker compose up -d postgres redis rabbitmq >/dev/null 2>&1 || {
  bad "docker compose up failed"; exit 1; }

for i in $(seq 1 60); do
  pg=$(docker inspect --format='{{.State.Health.Status}}' flashsale-authclean-postgres-1 2>/dev/null)
  redis=$(docker inspect --format='{{.State.Health.Status}}' flashsale-authclean-redis-1 2>/dev/null)
  [ "$pg" = "healthy" ] && [ "$redis" = "healthy" ] && break
  sleep 2
done
expect "PostgreSQL is healthy" "healthy" "$pg"
expect "Redis is healthy"      "healthy" "$redis"

# ---------------------------------------------------------------------------
step "1. Migrations (schema from the migration history, not EnsureCreated)"
# ---------------------------------------------------------------------------
# --no-launch-profile matters: launchSettings.json would force Development,
# and the release runs as Production.
if dotnet run --project src/Order.Api/Order.Api.csproj --no-launch-profile -- --migrate 2>&1 | grep -qE 'exit code 0|Schema is now at head|already at head'; then
  ok "--migrate exited 0"
else
  bad "--migrate failed; see output above"
fi

# ---------------------------------------------------------------------------
step "2. Boot the API (Production environment, real Kestrel)"
# ---------------------------------------------------------------------------
rm -f "$LOG_FILE"
dotnet run --project src/Order.Api/Order.Api.csproj --no-launch-profile >"$LOG_FILE" 2>&1 &
API_PID=$!
info "API pid $API_PID (log: $LOG_FILE)"

ready=""
for i in $(seq 1 90); do
  [ "$(status "$BASE/health/ready")" = "200" ] && { ready=$i; break; }
  if ! kill -0 "$API_PID" 2>/dev/null; then
    bad "the API process exited during startup:"; tail -25 "$LOG_FILE"; exit 1
  fi
  sleep 1
done
if [ -n "$ready" ]; then ok "/health/ready -> 200 (after ${ready}s)"; else
  bad "the API never became ready"; tail -25 "$LOG_FILE"; exit 1; fi

expect "liveness" "200" "$(status "$BASE/health/live")"

EMAIL="demo+$(date +%s)@example.com"
PASSWORD="correct-horse-battery-staple"
JSON_HEADER='Content-Type: application/json'

# Built as variables (not inline escaped quotes) — see the helper comment above.
REGISTER_JSON=$(printf '{"email":"%s","password":"%s"}' "$EMAIL" "$PASSWORD")
LOGIN_JSON="$REGISTER_JSON"
WEAK_JSON='{"email":"weak@example.com","password":"short"}'

# ---------------------------------------------------------------------------
step "3. Register a CUSTOMER (PBKDF2 hash, never a stored password)"
# ---------------------------------------------------------------------------
BODY=$(post_json /api/auth/register "$REGISTER_JSON")
CODE=$(printf '%s' "$BODY" | tail -1); JSON=$(printf '%s' "$BODY" | sed '$d')
printf '%s' "$JSON" | pretty
expect "POST /api/auth/register -> 201 Created" "201" "$CODE"
expect_contains "registration returns a token pair" '"refreshToken"' "$JSON"
ACCESS=$(field "$JSON" accessToken)
REFRESH=$(field "$JSON" refreshToken)
info "user id is the JWT 'sub' claim: $(printf '%s' "$ACCESS" | cut -d. -f2 | tr '_-' '/+' | python3 -c 'import sys,base64,json;s=sys.stdin.read().strip();s+="="*(-len(s)%4);print(json.loads(base64.b64decode(s)).get("sub"))' 2>/dev/null || echo '(install python3 to decode)')"

DUP_BODY=$(post_json /api/auth/register "$REGISTER_JSON")
DUP_CODE=$(printf '%s' "$DUP_BODY" | tail -1)
[ "$DUP_CODE" = "409" ] || info "server said: $(printf '%s' "$DUP_BODY" | sed '$d')"
expect "duplicate email -> 409 (the unique index, not a pre-flight SELECT)" "409" "$DUP_CODE"
expect "weak password -> 400" "400" "$(post_json_status /api/auth/register "$WEAK_JSON")"

# ---------------------------------------------------------------------------
step "4. Login (unknown email and wrong password are indistinguishable)"
# ---------------------------------------------------------------------------
LOGIN_BODY=$(post_json /api/auth/login "$LOGIN_JSON")
LOGIN_CODE=$(printf '%s' "$LOGIN_BODY" | tail -1)
expect "POST /api/auth/login -> 200" "200" "$LOGIN_CODE"

WRONG_JSON=$(printf '{"email":"%s","password":"%s"}' "$EMAIL" "definitely-the-wrong-password")
UNKNOWN_JSON='{"email":"nobody@example.com","password":"correct-horse-battery-staple"}'
WRONG=$(post_json /api/auth/login "$WRONG_JSON" | sed '$d')
UNKNOWN=$(post_json /api/auth/login "$UNKNOWN_JSON" | sed '$d')
info "wrong password: $WRONG"
expect_contains "both failures use the same message (no enumeration oracle)" \
  "$(printf '%s' "$UNKNOWN" | sed 's/[&/\]/\\&/g')" "$WRONG"

expect "GET /api/auth/me without a token -> 401" "401" "$(status "$BASE/api/auth/me")"
BODY=$(curl -s "$BASE/api/auth/me" -H "Authorization: Bearer $ACCESS")
info "whoami: $BODY"
expect_contains "/api/auth/me reflects the token's identity" "$EMAIL" "$BODY"

# ---------------------------------------------------------------------------
step "5. Browse the catalogue, then order as the authenticated caller"
# ---------------------------------------------------------------------------
PRODUCT_ID=""
for pid in 1 2 3 4 5; do
  [ "$(status "$BASE/api/products/$pid")" = "200" ] && { PRODUCT_ID=$pid; break; }
done
if [ -n "$PRODUCT_ID" ]; then ok "found product $PRODUCT_ID"; else bad "no seeded product"; fi
info "product: $(curl -s "$BASE/api/products/$PRODUCT_ID")"

KEY="demo-$(date +%s)"
BODY=$(curl -s -w '\n%{http_code}' -X POST "$BASE/api/orders" -H "$JSON_HEADER" \
  -H "Authorization: Bearer $ACCESS" -H "Idempotency-Key: $KEY" \
  -d "{\"productId\":$PRODUCT_ID,\"quantity\":1}")
CODE=$(printf '%s' "$BODY" | tail -1); JSON=$(printf '%s' "$BODY" | sed '$d')
printf '%s' "$JSON" | pretty
case "$CODE" in
  202) ok "202 Accepted — queued, fulfilment is asynchronous (ADR-004)";;
  200) ok "200 OK — reservation tier unavailable, processed synchronously";;
  *)   bad "order rejected (HTTP $CODE)";;
esac

# ---------------------------------------------------------------------------
step "6. Poll until Completed (202 is a promise, not a fact)"
# ---------------------------------------------------------------------------
FINAL="processing"
for i in $(seq 1 40); do
  # The order was placed AS this customer, so the poll presents the same token:
# GET /api/orders/{key} is authenticated and owner-scoped (the key is
# client-supplied, so it was never a capability).
S=$(curl -s "$BASE/api/orders/$KEY" -H "Authorization: Bearer $ACCESS")
  if printf '%s' "$S" | grep -q '"status":"completed"'; then FINAL="completed"; break; fi
  sleep 1
done
info "final status payload: $(curl -s "$BASE/api/orders/$KEY" -H "Authorization: Bearer $ACCESS")"
expect "order reached Completed" "completed" "$FINAL"

# ---------------------------------------------------------------------------
step "7. /orders/me — the caller's own history"
# ---------------------------------------------------------------------------
BODY=$(curl -s -w '\n%{http_code}' "$BASE/orders/me" -H "Authorization: Bearer $ACCESS")
CODE=$(printf '%s' "$BODY" | tail -1); JSON=$(printf '%s' "$BODY" | sed '$d')
printf '%s' "$JSON" | pretty
expect "GET /orders/me -> 200" "200" "$CODE"
expect_contains "the new order is listed" '"count":1' "$JSON"
expect "GET /orders/me without a token -> 401" "401" "$(status "$BASE/orders/me")"

# ---------------------------------------------------------------------------
step "8. Refresh rotates the generation — a replay is rejected (ADR-013 §3)"
# ---------------------------------------------------------------------------
REFRESH_JSON=$(printf '{"refreshToken":"%s"}' "$REFRESH")
BODY=$(post_json /api/auth/refresh "$REFRESH_JSON")
CODE=$(printf '%s' "$BODY" | tail -1); JSON=$(printf '%s' "$BODY" | sed '$d')
expect "first refresh -> 200" "200" "$CODE"
ROTATED=$(field "$JSON" refreshToken)
NEW_ACCESS=$(field "$JSON" accessToken)
if [ -n "$ROTATED" ] && [ "$ROTATED" != "$REFRESH" ]; then
  ok "a NEW refresh token was issued (generation bumped)"
else
  bad "the refresh token did not change"
fi

ROTATED_JSON=$(printf '{"refreshToken":"%s"}' "$ROTATED")
ACCESS_JSON=$(printf '{"refreshToken":"%s"}' "$NEW_ACCESS")

expect "REPLAYING the old refresh token -> 401" "401" \
  "$(post_json_status /api/auth/refresh "$REFRESH_JSON")"
expect "rotated token still works -> 200" "200" \
  "$(post_json_status /api/auth/refresh "$ROTATED_JSON")"

# Both are HS256 JWTs from the same key, so only the `typ` claim separates them.
expect "an access token cannot be used as a refresh token -> 401" "401" \
  "$(post_json_status /api/auth/refresh "$ACCESS_JSON")"

# ---------------------------------------------------------------------------
step "9. Logout invalidates the refresh token"
# ---------------------------------------------------------------------------
expect "POST /api/auth/logout -> 204" "204" \
  "$(status -X POST "$BASE/api/auth/logout" -H "Authorization: Bearer $NEW_ACCESS")"
expect "refresh after logout -> 401" "401" \
  "$(post_json_status /api/auth/refresh "$ROTATED_JSON")"
info "documented trade-off: the access token keeps working for the rest of its <= 15m life"
expect "access token survives logout until it expires" "200" \
  "$(status "$BASE/api/auth/me" -H "Authorization: Bearer $NEW_ACCESS")"

# ---------------------------------------------------------------------------
step "10. Metrics (Phase III) — real Meter instruments, JSON snapshot"
# ---------------------------------------------------------------------------
# /internal/metrics is an ops read (it counts auth successes and failures), so it
# is read as STAFF. The API seeds that account from Bootstrap:StaffPassword.
STAFF_JSON=$(curl -s -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
  -d "$(printf '{"email":"%s","password":"%s"}' "${DEMO_STAFF_EMAIL:-staff@flashsale.local}" "${DEMO_STAFF_PASSWORD:?set DEMO_STAFF_PASSWORD to the Bootstrap:StaffPassword value}")")
STAFF_ACCESS=$(field "$STAFF_JSON" accessToken)
expect_contains "STAFF login returns a token pair" '"accessToken"' "$STAFF_JSON"
BODY=$(curl -s -w '\n%{http_code}' "$BASE/internal/metrics" -H "Authorization: Bearer $STAFF_ACCESS")
CODE=$(printf '%s' "$BODY" | tail -1); METRICS=$(printf '%s' "$BODY" | sed '$d')
expect "GET /internal/metrics as STAFF -> 200" "200" "$CODE"
expect "GET /internal/metrics anonymously -> 401" "401" \\
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/internal/metrics")"
printf '%s' "$METRICS" | pretty | head -40
expect_contains "counts registrations"        'flashsale.auth.registrations' "$METRICS"
expect_contains "counts auth failures"        'reason=invalid_credentials'   "$METRICS"
expect_contains "counts replay attempts"      'reason=invalid_refresh_token' "$METRICS"
expect_contains "counts accepted orders"      'flashsale.orders.accepted'    "$METRICS"
expect_contains "duration tagged by route"    'flashsale.http.request.duration' "$METRICS"
expect_contains "in-flight gauge present"     'flashsale.http.in_flight'     "$METRICS"

# ---------------------------------------------------------------------------
step "11. OpenAPI document (Phase III)"
# ---------------------------------------------------------------------------
OAS=$(curl -s "$BASE/openapi/v1.json")
expect "GET /openapi/v1.json -> 200" "200" "$(status "$BASE/openapi/v1.json")"
expect_contains "documents /api/auth/register"  '/api/auth/register' "$OAS"
expect_contains "documents /orders/me"          '/orders/me'         "$OAS"
expect_contains "declares the Bearer scheme"    '"bearer"'           "$OAS"
expect_contains "declares bearerFormat JWT"     'bearerFormat'       "$OAS"

# ---------------------------------------------------------------------------
printf '\n\033[1m=========================================\n'
printf ' DEMO RESULT:  PASS=%s  FAIL=%s\n' "$PASS" "$FAIL"
printf '=========================================\033[0m\n'

if [ "$FAIL" = "0" ]; then
  printf '\nNext: read docs/adr/013-auth-design.md for the reasoning behind\n'
  printf 'each decision this script just exercised.\n'
fi

[ "$FAIL" = "0" ]