#!/usr/bin/env bash
# Phase 3 live verification (spec §6 / §17-B): reduce inventory → threshold
# detected → LOW_STOCK alert → notification hook (log+audit) + dedupe proof.
set -uo pipefail
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

BASE=http://127.0.0.1:5099
PASS=0; FAIL=0
check() { if [ "$2" = "$3" ]; then echo "PASS  $1 (=$3)"; PASS=$((PASS+1));
  else echo "FAIL  $1 (expected $2, got $3)"; FAIL=$((FAIL+1)); fi; }
contains() { if printf '%s' "$3" | grep -qF -- "$2"; then echo "PASS  $1"; PASS=$((PASS+1));
  else echo "FAIL  $1 (missing '$2')"; FAIL=$((FAIL+1)); fi; }
numfield() { grep -o "\"$1\":[0-9]*" | head -1 | sed 's/.*://'; }

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5099
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres'
export ConnectionStrings__Redis='localhost:6379'
export ConnectionStrings__RabbitMQ='amqp://guest:guest@localhost:5672/'
export Auth__Jwt__SigningKey='dev-only-insecure-local-signing-key-change-me'
export Messaging__Provider=RabbitMQ
export Automation__Inventory__DefaultReorderThreshold=5

# The /internal/** and /api/outbox/** runbook endpoints are STAFF/ADMIN only.
# Export a STAFF access token before running, e.g.:
#   export OPS_TOKEN=$(curl -s -X POST "$BASE/api/auth/login" \
#     -H 'Content-Type: application/json' \
#     -d '{"email":"staff@flashsale.local","password":"<Bootstrap:StaffPassword>"}' \
#     | grep -o '"accessToken":"[^"]*' | cut -d'"' -f4)
OPS_TOKEN=${OPS_TOKEN:?set OPS_TOKEN to a STAFF access token; the runbook endpoints require STAFF/ADMIN}
opsauth=(-H "Authorization: Bearer $OPS_TOKEN")psql_db() { docker exec flashsale-authclean-postgres-1 psql -U postgres -d FlashSaleDb -tAc "$1"; }
set_stock() { psql_db "UPDATE \"Products\" SET \"AvailableStock\"=$1 WHERE \"Id\"=1" > /dev/null; }
scan() { curl -s "${opsauth[@]}" -X POST "$BASE/internal/automation/low-stock-scan"; }

rm -f /tmp/lowstockapi.log
dotnet run --project src/Order.Api/Order.Api.csproj --no-build --no-launch-profile > /tmp/lowstockapi.log 2>&1 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null' EXIT
for i in $(seq 1 90); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health/ready")" = "200" ] && break
  kill -0 $API_PID 2>/dev/null || { echo "API died:"; tail -20 /tmp/lowstockapi.log; exit 1; }
  sleep 1
done
echo "API ready"

# clean slate for the alert state
psql_db "DELETE FROM \"StockAlerts\"" > /dev/null

echo "--- (1) healthy stock (10 > threshold 5) -> nothing raised ---"
set_stock 10
RESP=$(scan)
check "healthy: scanned=0" "0" "$(printf '%s' "$RESP" | numfield scanned)"
check "healthy: alertsCreated=0" "0" "$(printf '%s' "$RESP" | numfield alertsCreated)"

echo "--- (2) reduce inventory to threshold (5) -> LOW_STOCK alert ---"
set_stock 5
RESP=$(scan)
check "low: scanned=1" "1" "$(printf '%s' "$RESP" | numfield scanned)"
check "low: alertsCreated=1" "1" "$(printf '%s' "$RESP" | numfield alertsCreated)"
contains "low: product id reported" '"productIds":[1]' "$RESP"

ROWS=$(psql_db "SELECT \"ProductId\"||'|'||\"AvailableStock\"||'|'||\"ReorderThreshold\"||'|'||\"Status\" FROM \"StockAlerts\"")
contains "StockAlerts row is Open at 5/5" "1|5|5|Open" "$ROWS"

echo "--- (3) rescan -> deduped (no duplicate alert) ---"
RESP=$(scan)
check "rescan: alertsCreated=0 (dedupe)" "0" "$(printf '%s' "$RESP" | numfield alertsCreated)"
check "exactly one alert row" "1" "$(psql_db "SELECT count(*) FROM \"StockAlerts\"")"

echo "--- (4) audit trail (spec §11) ---"
AUD=$(psql_db "SELECT \"WorkflowName\"||'|'||\"TriggerType\"||'|'||\"Status\"||'|'||coalesce(\"ResultSummary\",'') FROM \"AutomationRuns\" WHERE \"WorkflowName\"='InventoryAutomation' ORDER BY \"Id\" DESC LIMIT 3")
echo "$AUD"
contains "low-stock audit run success" "InventoryAutomation|manual|Success" "$AUD"
contains "audit records the created alert" "alerts_created=1" "$AUD"

echo "--- (5) notification hook evidence (structured log, email/webhook PLANNED) ---"
grep -q "LOW_STOCK alert" /tmp/lowstockapi.log && { echo "PASS  LOW_STOCK log emitted"; PASS=$((PASS+1)); } \
  || { echo "FAIL  LOW_STOCK log missing"; FAIL=$((FAIL+1)); }

echo
echo "==================================="
echo "PASS=$PASS FAIL=$FAIL"
echo "==================================="
[ "$FAIL" = "0" ]
