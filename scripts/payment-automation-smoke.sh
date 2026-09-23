#!/usr/bin/env bash
# Phase 2 live verification (spec §5/§17-A): payment timeout → reminder →
# cancellation → stock release → automation audit. Two API boots because
# options bind at startup: grace=60 (reminder path), then grace=0 (cancel path).
set -uo pipefail
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

BASE=http://127.0.0.1:5099
PASS=0; FAIL=0

check() { if [ "$2" = "$3" ]; then echo "PASS  $1 (=$3)"; PASS=$((PASS+1));
  else echo "FAIL  $1 (expected $2, got $3)"; FAIL=$((FAIL+1)); fi; }
contains() { if printf '%s' "$3" | grep -q -- "$2"; then echo "PASS  $1"; PASS=$((PASS+1));
  else echo "FAIL  $1 (missing '$2')"; FAIL=$((FAIL+1)); fi; }
# key as $1, JSON on stdin (auth_e2e's two-arg variant confused the callers)
field() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | sed 's/.*":"//; s/"$//'; }
numfield() { grep -o "\"$1\":[0-9]*" | head -1 | sed 's/.*://'; }

export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres'
export ConnectionStrings__Redis='localhost:6379'
export ConnectionStrings__RabbitMQ='amqp://guest:guest@localhost:5672/'
export Auth__Jwt__SigningKey='dev-only-insecure-local-signing-key-change-me'
export Automation__Payment__TimeoutMinutes=0
export Automation__Payment__MaxPaymentReminders=3
export Messaging__Provider=RabbitMQ

boot_api() { # boot_api <graceMinutes>
  export Automation__Payment__GracePeriodMinutes="$1"
  export ASPNETCORE_URLS=http://127.0.0.1:5099
  rm -f /tmp/payapi.log
  dotnet run --project src/Order.Api/Order.Api.csproj --no-build --no-launch-profile > /tmp/payapi.log 2>&1 &
  API_PID=$!
  for i in $(seq 1 90); do
    code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health/ready" 2>/dev/null)
    if [ "$code" = "200" ]; then echo "API ready (grace=$1) after ${i}s"; return 0; fi
    if ! kill -0 $API_PID 2>/dev/null; then echo "API died:"; tail -20 /tmp/payapi.log; exit 1; fi
    sleep 1
  done
  echo "API never ready"; tail -20 /tmp/payapi.log; exit 1
}
stop_api() { kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null; }

stock() { curl -s "$BASE/api/products/$1" | numfield availableStock; }
place_order() { # place_order <key>
  curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/orders" \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $1" \
    -d '{"productId":1,"quantity":1}'
}
wait_row() { # wait_row <key> — legacy endpoint flips processing -> completed
  for i in $(seq 1 20); do
    s=$(curl -s "$BASE/api/orders/$1" | field status)
    [ "$s" = "completed" ] && return 0
    sleep 1
  done
  return 1
}
scan() { curl -s -X POST "$BASE/internal/automation/payment-timeout-scan"; }

TS=$(date +%s)
KEY_A="payA$TS"; KEY_B="payB$TS"; KEY_C="payC$TS"

echo "=== PHASE 1: grace=60 (reminder path) ==="
boot_api 60
S0=$(stock 1)   # baseline AFTER boot: curl needs the API up (was empty before)
echo "stock before any orders: $S0"

CODE=$(place_order "$KEY_A"); check "order A accepted" "202" "$CODE"
check "order A row persisted (legacy wording)" "1" "$(wait_row "$KEY_A" && echo 1 || echo 0)"

BODY=$(curl -s -w '\n%{http_code}' -X POST "$BASE/api/orders/$KEY_A/pay" \
  -H 'Content-Type: application/json' -d '{"outcome":"completed"}')
check "pay completed -> 200" "200" "$(printf '%s' "$BODY" | tail -1)"
contains "pay response confirms status" '"status":"confirmed"' "$(printf '%s' "$BODY" | head -1)"

CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/orders/$KEY_A/pay" \
  -H 'Content-Type: application/json' -d '{"outcome":"completed"}')
check "duplicate pay -> 409 (guarded exactly-once)" "409" "$CODE"

CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/orders/$KEY_A/pay" \
  -H 'Content-Type: application/json' -d '{"outcome":"bogus"}')
check "invalid outcome -> 400" "400" "$CODE"

CODE=$(place_order "$KEY_B"); check "order B accepted" "202" "$CODE"
check "order B row persisted" "1" "$(wait_row "$KEY_B" && echo 1 || echo 0)"
BODY=$(curl -s -w '\n%{http_code}' -X POST "$BASE/api/orders/$KEY_B/pay" \
  -H 'Content-Type: application/json' -d '{"outcome":"failed"}')
check "pay failed -> 200" "200" "$(printf '%s' "$BODY" | tail -1)"
contains "failed keeps pending wording" '"status":"payment_failed_recorded"' "$(printf '%s' "$BODY" | head -1)"

# Legacy status contract must be untouched (v1.0 smoke depends on it)
contains "legacy GET shows completed for A (row exists)" '"status":"completed"' "$(curl -s "$BASE/api/orders/$KEY_A")"

RESP=$(scan)
check "scan 1: reminded B exactly once" "1" "$(printf '%s' "$RESP" | numfield reminded)"
check "scan 1: cancelled nothing (inside grace)" "0" "$(printf '%s' "$RESP" | numfield cancelled)"

CODE=$(place_order "$KEY_C"); check "order C accepted" "202" "$CODE"
check "order C row persisted" "1" "$(wait_row "$KEY_C" && echo 1 || echo 0)"
stop_api

echo "=== PHASE 2: grace=0 (cancel + stock release) ==="
boot_api 0
RESP=$(scan)
check "scan 2: cancelled B and C" "2" "$(printf '%s' "$RESP" | numfield cancelled)"
S1=$(stock 1)
check "stock released for B+C (only A stays consumed)" "$((S0-1))" "$S1"
contains "A confirmed survives the scan (not pending)" '"status":"completed"' "$(curl -s "$BASE/api/orders/$KEY_A")"
stop_api

echo "=== AUDIT ROWS (spec §11, via psql) ==="
ROWS=$(docker exec flashsale-authclean-postgres-1 psql -U postgres -d FlashSaleDb -tAc \
  "SELECT \"WorkflowName\"||'|'||\"TriggerType\"||'|'||\"Status\"||'|'||coalesce(\"ResultSummary\",'') FROM \"AutomationRuns\" ORDER BY \"Id\"")
echo "$ROWS"
contains "payment_timeout audit exists" "PaymentTimeout|manual|Success" "$ROWS"
contains "scan2 audit records cancellations" "cancelled=2" "$ROWS"
contains "pay call audited (trigger=api)" "OrderProcessing|api|Success" "$ROWS"

echo
echo "==================================="
echo "PASS=$PASS FAIL=$FAIL"
echo "==================================="
[ "$FAIL" = "0" ]
