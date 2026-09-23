#!/usr/bin/env bash
# Phase 5 live verification (spec §8/§9/§17-D) with REAL qwen3:4b inference:
# content: authZ -> generate -> queue -> publish blocked -> approve -> publish;
# triage: grounded + human-review rules.
set -uo pipefail
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

BASE=http://127.0.0.1:5099
PASS=0; FAIL=0
check() { if [ "$2" = "$3" ]; then echo "PASS  $1 (=$3)"; PASS=$((PASS+1));
  else echo "FAIL  $1 (expected $2, got $3)"; FAIL=$((FAIL+1)); fi; }
contains() { if printf '%s' "$3" | grep -qF -- "$2"; then echo "PASS  $1"; PASS=$((PASS+1));
  else echo "FAIL  $1 (missing '$2')"; FAIL=$((FAIL+1)); fi; }

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5099
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres'
export ConnectionStrings__Redis='localhost:6379'
export ConnectionStrings__RabbitMQ='amqp://guest:guest@localhost:5672/'
export Auth__Jwt__SigningKey='dev-only-insecure-local-signing-key-change-me'
export Messaging__Provider=RabbitMQ

psql_db() { docker exec flashsale-authclean-postgres-1 psql -U postgres -d FlashSaleDb -tAc "$1"; }
AUTH="Content-Type: application/json"

rm -f /tmp/contentapi.log
dotnet run --project src/Order.Api/Order.Api.csproj --no-build --no-launch-profile > /tmp/contentapi.log 2>&1 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null' EXIT
for i in $(seq 1 90); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health/ready")" = "200" ] && break
  kill -0 $API_PID 2>/dev/null || { echo "API died:"; tail -20 /tmp/contentapi.log; exit 1; }
  sleep 1
done
echo "API ready"

TS=$(date +%s)
register() { curl -s -X POST "$BASE/api/auth/register" -H "$AUTH" -d "{\"email\":\"$1\",\"password\":\"correct-horse-battery-staple\"}"; }
token_of() { python3 -c 'import sys,json; print(json.load(sys.stdin).get("accessToken",""))' <<< "$1"; }

echo "--- staff provisioning (login seeded STAFF account) ---"
# Seeded by DatabaseInitializer (idempotent); no psql sidecar needed.
STAFF_EMAIL="staff@flashsale.local"
LRAW=$(curl -s --max-time 30 -X POST "$BASE/api/auth/login" -H "$AUTH" \
  -d "{\"email\":\"$STAFF_EMAIL\",\"password\":\"correct-horse-battery-staple\"}")
if ! printf '%s' "$LRAW" | python3 -c 'import sys,json; json.load(sys.stdin)' 2>/dev/null; then
  echo "ABORT: staff login returned non-JSON: $(printf '%s' "$LRAW" | head -c 200)"
  tail -20 /tmp/contentapi.log
  exit 1
fi
STAFF=$(token_of "$LRAW")
CUSTOMER=$(token_of "$(register "cust+$TS@example.com")")
[ -n "$STAFF" ] && [ -n "$CUSTOMER" ] && { echo "PASS  two tokens acquired"; PASS=$((PASS+1)); } \
  || { echo "FAIL  token acquisition (STAFF_LEN=${#STAFF} CUST_LEN=${#CUSTOMER})"; FAIL=$((FAIL+1)); }

echo "--- authZ guards ---"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/products/1/content/generate")
check "anonymous generate -> 401" "401" "$CODE"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/products/1/content/generate" \
  -H "Authorization: Bearer $CUSTOMER")
check "CUSTOMER generate -> 403" "403" "$CODE"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/support/triage" \
  -H "$AUTH" -d '{"message":"hi"}')
check "anonymous triage -> 401" "401" "$CODE"

echo "--- generate with REAL model (may take ~1 min) ---"
S=$(date +%s)
BODY=$(curl -s --max-time 300 -w '\n%{http_code}' -X POST "$BASE/api/products/1/content/generate" \
  -H "Authorization: Bearer $STAFF")
CODE=$(printf '%s' "$BODY" | tail -1)
JSON=$(printf '%s' "$BODY" | sed '$d')
E=$(date +%s)
check "generate -> 200" "200" "$CODE"
contains "draft is ReviewRequired" '"status":"ReviewRequired"' "$JSON"
contains "model is qwen3:4b" '"model":"qwen3:4b"' "$JSON"
contains "short description present" '"shortDescription"' "$JSON"
echo "INFO  inference wall-clock: $((E-S))s"
DRAFT=$(printf '%s' "$JSON" | grep -o '"draftId":[0-9]*' | head -1 | sed 's/.*://')
SHORT=$(printf '%s' "$JSON" | grep -o '"shortDescription":"[^"]*"' | head -1 | sed 's/.*":"//; s/"$//')
echo "INFO  draftId=$DRAFT short='$SHORT'"

echo "--- review queue + publish blocked ---"
contains "queue shows draft $DRAFT" "\"id\":$DRAFT" "$(curl -s "$BASE/api/content/review-queue" -H "Authorization: Bearer $STAFF")"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/content/$DRAFT/publish" \
  -H "Authorization: Bearer $STAFF")

check "publish unapproved -> 409" "409" "$CODE"

echo "--- approve, double-approve, publish ---"
BODY=$(curl -s -w '\n%{http_code}' -X POST "$BASE/api/content/$DRAFT/approve" -H "Authorization: Bearer $STAFF")
check "approve -> 200" "200" "$(printf '%s' "$BODY" | tail -1)"
contains "approved status" '"status":"Approved"' "$(printf '%s' "$BODY" | head -1)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/content/$DRAFT/approve" -H "Authorization: Bearer $STAFF")
check "double approve -> 409" "409" "$CODE"
BODY=$(curl -s -w '\n%{http_code}' -X POST "$BASE/api/content/$DRAFT/publish" -H "Authorization: Bearer $STAFF")
check "publish after approval -> 200" "200" "$(printf '%s' "$BODY" | tail -1)"
contains "published status" '"status":"Published"' "$(printf '%s' "$BODY" | head -1)"
contains "product description updated with approved copy" "$SHORT" "$(curl -s "$BASE/api/products/1")"

echo "--- reject flow + unknown product ---"
BODY2=$(curl -s --max-time 300 -X POST "$BASE/api/products/1/content/generate" -H "Authorization: Bearer $STAFF")
DRAFT2=$(printf '%s' "$BODY2" | grep -o '"draftId":[0-9]*' | head -1 | sed 's/.*://')
BODY=$(curl -s -w '\n%{http_code}' -X POST "$BASE/api/content/$DRAFT2/reject" \
  -H "$AUTH" -H "Authorization: Bearer $STAFF" -d '{"reason":"tone is off"}')
check "reject with reason -> 200" "200" "$(printf '%s' "$BODY" | tail -1)"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/products/999999/content/generate" \
  -H "Authorization: Bearer $STAFF")
check "generate unknown product -> 404" "404" "$CODE"

echo "--- support triage (real order, real model) ---"
# Top up stock: earlier scenarios drain AvailableStock to 0 → order would 409
# and grounding facts would never exist. Re-seed BOTH tiers: PostgreSQL (truth)
# and the Redis reservation hash stock:1 (mirror) — SubmitOrderUseCase only
# re-seeds Redis on UnknownProduct, not on OutOfStock, so a stale zero in the
# hash keeps rejecting orders even after the DB is restocked.
docker exec flashsale-authclean-postgres-1 psql -U postgres -d FlashSaleDb -tAc \
  "UPDATE \"Products\" SET \"AvailableStock\" = 50 WHERE \"Id\" = 1" >/dev/null
docker exec flashsale-authclean-redis-1 redis-cli HSET stock:1 qty 50 >/dev/null
TKEY="triage$TS"
curl -s -o /dev/null -X POST "$BASE/api/orders" -H "$AUTH" \
  -H "Idempotency-Key: $TKEY" -d '{"productId":1,"quantity":1}'
for i in $(seq 1 15); do
  [ "$(curl -s "$BASE/api/orders/$TKEY" | grep -o '"status":"[^"]*"' | head -1 | sed 's/.*":"//; s/"$//')" = "completed" ] && break
  sleep 1
done
TBODY=$(curl -s --max-time 300 -X POST "$BASE/api/support/triage" \
  -H "$AUTH" -H "Authorization: Bearer $CUSTOMER" \
  -d "{\"message\":\"Where is my order?\",\"orderKey\":\"$TKEY\"}")
contains "triage carries real order facts" "$TKEY" "$TBODY"
contains "triage has a draft reply" '"draftResponse"' "$TBODY"

TBODY2=$(curl -s --max-time 300 -X POST "$BASE/api/support/triage" \
  -H "$AUTH" -H "Authorization: Bearer $CUSTOMER" \
  -d '{"message":"I want a refund right now"}')
contains "refund triage demands human review" '"requiresHumanReview":true' "$TBODY2"

CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/support/triage" \
  -H "$AUTH" -H "Authorization: Bearer $CUSTOMER" -d '{"message":""}')
check "empty triage message -> 400" "400" "$CODE"

echo "--- OpenAPI documents content + support ---"
OAS=$(curl -s "$BASE/openapi/v1.json")
contains "OpenAPI lists content generate" '/api/products/{id}/content/generate' "$OAS"
contains "OpenAPI lists support triage" '/api/support/triage' "$OAS"

echo
echo "==================================="
echo "PASS=$PASS FAIL=$FAIL"
echo "==================================="
[ "$FAIL" = "0" ]
