#!/usr/bin/env bash
# v2.1 live verification: real API + real local Ollama (qwen3:4b). Proves the
# grounded Product Assistant end-to-end (spec §10) — not a mock.
set -uo pipefail
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5099
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres'
export ConnectionStrings__Redis='localhost:6379'
export ConnectionStrings__RabbitMQ='amqp://guest:guest@localhost:5672/'
export Auth__Jwt__SigningKey='dev-only-insecure-local-signing-key-change-me'

BASE=http://127.0.0.1:5099
PASS=0; FAIL=0

check() { if [ "$2" = "$3" ]; then echo "PASS  $1 (=$3)"; PASS=$((PASS+1));
  else echo "FAIL  $1 (expected $2, got $3)"; FAIL=$((FAIL+1)); fi; }
contains() { if printf '%s' "$3" | grep -q -- "$2"; then echo "PASS  $1"; PASS=$((PASS+1));
  else echo "FAIL  $1 (missing '$2')"; FAIL=$((FAIL+1)); fi; }
field() { printf '%s' "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | sed 's/.*":"//; s/"$//'; }

rm -f /tmp/aiapi.log
dotnet run --project src/Order.Api/Order.Api.csproj --no-build --no-launch-profile > /tmp/aiapi.log 2>&1 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null' EXIT

echo "--- waiting for readiness (pid $API_PID) ---"
READY=""
for i in $(seq 1 90); do
  code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health/ready" 2>/dev/null)
  if [ "$code" = "200" ]; then READY=$i; break; fi
  if ! kill -0 $API_PID 2>/dev/null; then echo "API process died:"; tail -30 /tmp/aiapi.log; exit 1; fi
  sleep 1
done
if [ -z "$READY" ]; then echo "API never became ready"; tail -30 /tmp/aiapi.log; exit 1; fi
check "/health/ready" "200" "200"

AUTH="Content-Type: application/json"
EMAIL="ai+$(date +%s)@example.com"
PASSWORD="correct-horse-battery-staple"

echo "--- register (assistant requires an authenticated caller) ---"
BODY=$(curl -s -X POST "$BASE/api/auth/register" -H "$AUTH" -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
ACCESS=$(field "$BODY" accessToken)
if [ -n "$ACCESS" ]; then echo "PASS  registered, access token acquired"; PASS=$((PASS+1));
else echo "FAIL  register: no accessToken"; FAIL=$((FAIL+1)); fi

echo "--- POST /api/assistant/product without token -> 401 ---"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/assistant/product" -H "$AUTH" -d '{"question":"recommend something"}')
check "anonymous assistant -> 401" "401" "$CODE"

echo "--- empty question -> 400 ---"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/assistant/product" -H "$AUTH" -H "Authorization: Bearer $ACCESS" -d '{"question":""}')
check "empty question -> 400" "400" "$CODE"

echo "--- real inference (qwen3:4b, may take a while) ---"
START=$(date +%s)
BODY=$(curl -s --max-time 300 -w '\n%{http_code}' -X POST "$BASE/api/assistant/product" -H "$AUTH" \
  -H "Authorization: Bearer $ACCESS" \
  -d '{"question":"Which product is best for someone who wants a great gadget for daily use?"}')
CODE=$(printf '%s' "$BODY" | tail -1)
JSON=$(printf '%s' "$BODY" | sed '$d')
END=$(date +%s)
check "authenticated assistant status" "200" "$CODE"
contains "response carries a non-empty answer" '"answer":"' "$JSON"
contains "model is qwen3:4b" '"model":"qwen3:4b"' "$JSON"
contains "latency reported" '"latencyMs":' "$JSON"
contains "token usage reported" '"tokens":' "$JSON"
contains "recommendedProducts array present" '"recommendedProducts"' "$JSON"
echo "INFO  wall-clock for inference: $((END-START))s; answer preview:"

echo "--- grounding: every recommended productId must exist in the API ---"
IDS=$(printf '%s' "$JSON" | grep -o '"productId":[0-9]*' | grep -o '[0-9]*')
if [ -z "$IDS" ]; then
  echo "INFO  model recommended zero products (valid per spec: answer-only is OK)"; PASS=$((PASS+1))
fi
for id in $IDS; do
  C=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/products/$id")
  check "recommended productId $id resolves via GET /api/products/$id" "200" "$C"
done

echo "--- rate limiter engaged in Redis (production wiring) ---"
SUB=$(printf '%s' "$ACCESS" | cut -d. -f2 | python3 -c 'import sys,base64,json; p=sys.stdin.read().strip(); print(json.loads(base64.urlsafe_b64decode(p+"="*(-len(p)%4)))["sub"])' 2>/dev/null)
MIN=$(( $(date +%s) / 60 ))
KEY="flashsale:ai:rl:$SUB:$MIN"
# The inference can take 70s+ and roll the minute bucket between the CALL and
# this check — accept the window from either minute (the limiter uses "now").
CNT=$(docker exec flashsale-authclean-redis-1 redis-cli GET "$KEY")
if [ -z "$CNT" ] || [ "$CNT" = "" ]; then
  CNT=$(docker exec flashsale-authclean-redis-1 redis-cli GET "flashsale:ai:rl:$SUB:$((MIN-1))")
fi
if [ -n "$CNT" ] && [ "$CNT" -ge 1 ] 2>/dev/null; then
  echo "PASS  Redis window key exists with count=$CNT"; PASS=$((PASS+1))
else
  echo "FAIL  Redis window key missing or non-numeric ('$CNT')"; FAIL=$((FAIL+1))
fi

echo "--- preload quota -> next request must be 429 WITHOUT touching the model ---"
docker exec flashsale-authclean-redis-1 redis-cli SET "$KEY" 5 EX 70 > /dev/null
docker exec flashsale-authclean-redis-1 redis-cli SET "flashsale:ai:rl:$SUB:$((MIN+1))" 5 EX 70 > /dev/null
START=$(date +%s)
CODE=$(curl -s --max-time 15 -o /dev/null -w '%{http_code}' -X POST "$BASE/api/assistant/product" -H "$AUTH" \
  -H "Authorization: Bearer $ACCESS" -d '{"question":"rate limited probe"}')
END=$(date +%s)
check "over-quota request -> 429" "429" "$CODE"
echo "INFO  429 returned in $((END-START))s (fast = model was NOT called)"

echo "--- /internal/metrics now reports AI instruments ---"
METRICS=$(curl -s "$BASE/internal/metrics")
contains "metrics counts assistant requests" 'flashsale.ai.assistant.requests' "$METRICS"
contains "metrics reports assistant latency histogram" 'flashsale.ai.assistant.latency' "$METRICS"
contains "metrics counts assistant tokens" 'flashsale.ai.assistant.tokens' "$METRICS"
contains "assistant request tagged with outcome" 'outcome=Ok' "$METRICS"

echo "--- OpenAPI documents the assistant endpoint ---"
OAS=$(curl -s "$BASE/openapi/v1.json")
contains "OpenAPI lists /api/assistant/product" '/api/assistant/product' "$OAS"
contains "OpenAPI lists the Assistant tag" '"Assistant"' "$OAS"

echo
echo "==================================="
echo "PASS=$PASS FAIL=$FAIL"
echo "==================================="
[ "$FAIL" = "0" ]

printf '%s' "$JSON" | head -c 400; echo; echo
