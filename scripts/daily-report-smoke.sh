#!/usr/bin/env bash
# Phase 4 live verification (spec §7 / §17-C): trigger → DB metrics → stored
# report → upsert single row per day + audit trail.
set -uo pipefail
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

BASE=http://127.0.0.1:5099
PASS=0; FAIL=0
check() { if [ "$2" = "$3" ]; then echo "PASS  $1 (=$3)"; PASS=$((PASS+1));
  else echo "FAIL  $1 (expected $2, got $3)"; FAIL=$((FAIL+1)); fi; }
contains() { if printf '%s' "$3" | grep -qF -- "$2"; then echo "PASS  $1"; PASS=$((PASS+1));
  else echo "FAIL  $1 (missing '$2')"; FAIL=$((FAIL+1)); fi; }
numfield() { grep -o "\"$1\":[0-9.]*" | head -1 | sed 's/.*://'; }

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://127.0.0.1:5099
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres'
export ConnectionStrings__Redis='localhost:6379'
export ConnectionStrings__RabbitMQ='amqp://guest:guest@rabbitmq:5672/'  # placeholder overwritten below
export ConnectionStrings__RabbitMQ='amqp://guest:guest@localhost:5672/'
export Auth__Jwt__SigningKey='dev-only-insecure-local-signing-key-change-me'
export Messaging__Provider=RabbitMQ

psql_db() { docker exec flashsale-authclean-postgres-1 psql -U postgres -d FlashSaleDb -tAc "$1"; }

rm -f /tmp/reportapi.log
dotnet run --project src/Order.Api/Order.Api.csproj --no-build --no-launch-profile > /tmp/reportapi.log 2>&1 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null' EXIT
for i in $(seq 1 90); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health/ready")" = "200" ] && break
  kill -0 $API_PID 2>/dev/null || { echo "API died:"; tail -20 /tmp/reportapi.log; exit 1; }
  sleep 1
done
echo "API ready"

psql_db "DELETE FROM \"DailyReports\"" > /dev/null

echo "--- (1) trigger the daily report ---"
R1=$(curl -s -X POST "$BASE/internal/automation/daily-report")
echo "$R1" | head -c 400; echo
contains "report has reportDate" '"reportDate"' "$R1"
contains "report has totalOrders" '"totalOrders"' "$R1"
contains "report has revenue" '"revenue"' "$R1"
contains "report has averageOrderValue" '"averageOrderValue"' "$R1"
contains "report has topProducts list" '"topProductsJson"' "$R1"
contains "report has lowStock list" '"lowStockProductsJson"' "$R1"
contains "refundCount reported (0: no refund domain)" '"refundCount":0' "$R1"

TODAY=$(date -u +%Y-%m-%d)
check "one row persisted for today" "1" "$(psql_db "SELECT count(*) FROM \"DailyReports\" WHERE \"ReportDate\"='$TODAY'")"

echo "--- (2) rerun same day -> upsert (same row id) ---"
ID1=$(printf '%s' "$R1" | numfield id)
R2=$(curl -s -X POST "$BASE/internal/automation/daily-report")
ID2=$(printf '%s' "$R2" | numfield id)
check "rerun upserts the same report row" "$ID1" "$ID2"
check "still exactly one row for today" "1" "$(psql_db "SELECT count(*) FROM \"DailyReports\" WHERE \"ReportDate\"='$TODAY'")"

echo "--- (3) GET latest ---"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/internal/automation/daily-report/latest")
check "GET latest -> 200" "200" "$CODE"

echo "--- (4) audit trail (spec §11) ---"
AUD=$(psql_db "SELECT \"WorkflowName\"||'|'||\"TriggerType\"||'|'||\"Status\"||'|'||coalesce(\"ResultSummary\",'') FROM \"AutomationRuns\" WHERE \"WorkflowName\"='DailyReport' ORDER BY \"Id\" DESC LIMIT 2")
echo "$AUD"
contains "daily report audited" "DailyReport|manual|Success|date=$TODAY" "$AUD"

echo
echo "==================================="
echo "PASS=$PASS FAIL=$FAIL"
echo "==================================="
[ "$FAIL" = "0" ]
