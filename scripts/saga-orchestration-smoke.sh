#!/usr/bin/env bash
# Phase 11 & LOCAL v2 Live Verification:
# Distributed Checkout Saga Orchestration & Automated Compensating Transactions.
# Tested live against Envoy Gateway on kind-local-platform.
set -euo pipefail

GATEWAY="${GATEWAY_URL:-http://127.0.0.1:8088}"
HOST_HEADER="Host: flashsale.local"
PASS=0
FAIL=0

check() {
  if [ "$2" = "$3" ]; then
    echo "  PASS: $1 (=$3)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $1 (expected $2, got $3)"
    FAIL=$((FAIL + 1))
  fi
}

contains() {
  if printf '%s' "$3" | grep -q -- "$2"; then
    echo "  PASS: $1 (contains '$2')"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $1 (missing '$2' in response: $3)"
    FAIL=$((FAIL + 1))
  fi
}

field() {
  grep -o "\"$1\":[^,}]*" | head -1 | sed 's/.*://; s/"//g'
}

echo "=== LOCAL v2 / Phase 11: Checkout Saga Live Verification ==="
echo "Target Gateway: $GATEWAY (Host: flashsale.local)"

# 0. Health Pre-flight
HEALTH_CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "$HOST_HEADER" "$GATEWAY/health/live" || echo "000")
check "Envoy Gateway Health" "200" "$HEALTH_CODE"
if [ "$HEALTH_CODE" != "200" ]; then
  echo "Envoy Gateway not reachable on $GATEWAY. Ensure port-forward is running."
  exit 1
fi

TS=$(date +%s)

# --- Scenario 1: Happy Path (cents .01) ---
echo ""
echo "--- Scenario 1: Happy Path (.01 -> Success -> Confirmed) ---"
KEY1="saga-live-happy-$TS"
RESP1=$(curl -s -X POST -H "$HOST_HEADER" -H "Content-Type: application/json" \
  -d "{\"productId\":2,\"quantity\":1,\"amount\":5490000.01,\"idempotencyKey\":\"$KEY1\"}" \
  "$GATEWAY/api/saga/checkout")

STATUS1=$(printf '%s' "$RESP1" | field "status")
SUCCESS1=$(printf '%s' "$RESP1" | field "success")
check "Scenario 1 Success Flag" "true" "$SUCCESS1"
check "Scenario 1 Status (3=Completed)" "3" "$STATUS1"

SAGA1=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/saga/$KEY1")
INV1=$(printf '%s' "$SAGA1" | field "inventoryStatus")
PAY1=$(printf '%s' "$SAGA1" | field "paymentStatus")
check "Scenario 1 InventoryStatus (1=Reserved)" "1" "$INV1"
check "Scenario 1 PaymentStatus (1=Successful)" "1" "$PAY1"

# --- Scenario 2: Payment Decline (.02) with Automated Compensations ---
echo ""
echo "--- Scenario 2: Payment Decline (.02 -> Declined -> Compensated) ---"
KEY2="saga-live-decline-$TS"
BEFORE_STOCK2=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/products/2" | field "availableStock")

HTTP_CODE2=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "$HOST_HEADER" -H "Content-Type: application/json" \
  -d "{\"productId\":2,\"quantity\":2,\"amount\":5490000.02,\"idempotencyKey\":\"$KEY2\"}" \
  "$GATEWAY/api/saga/checkout")
check "Scenario 2 HTTP Status (422 Unprocessable)" "422" "$HTTP_CODE2"

SAGA2=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/saga/$KEY2")
STATUS2=$(printf '%s' "$SAGA2" | field "status")
INV2=$(printf '%s' "$SAGA2" | field "inventoryStatus")
PAY2=$(printf '%s' "$SAGA2" | field "paymentStatus")
REASON2=$(printf '%s' "$SAGA2" | field "compensationReason")

check "Scenario 2 Status (5=Compensated)" "5" "$STATUS2"
check "Scenario 2 InventoryStatus (2=Released)" "2" "$INV2"
check "Scenario 2 PaymentStatus (2=Declined)" "2" "$PAY2"
contains "Scenario 2 Compensation Reason" "insufficient funds" "$REASON2"

AFTER_STOCK2=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/products/2" | field "availableStock")
check "Scenario 2 Stock Restored After Compensation" "$BEFORE_STOCK2" "$AFTER_STOCK2"

# --- Scenario 3: Payment Timeout (.03) with Bounded Retry & Compensation ---
echo ""
echo "--- Scenario 3: Payment Timeout (.03 -> Retries Exhausted -> Compensated) ---"
KEY3="saga-live-timeout-$TS"
BEFORE_STOCK3=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/products/2" | field "availableStock")

HTTP_CODE3=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "$HOST_HEADER" -H "Content-Type: application/json" \
  -d "{\"productId\":2,\"quantity\":1,\"amount\":5490000.03,\"idempotencyKey\":\"$KEY3\"}" \
  "$GATEWAY/api/saga/checkout")
check "Scenario 3 HTTP Status (422 Unprocessable)" "422" "$HTTP_CODE3"

SAGA3=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/saga/$KEY3")
STATUS3=$(printf '%s' "$SAGA3" | field "status")
INV3=$(printf '%s' "$SAGA3" | field "inventoryStatus")
PAY3=$(printf '%s' "$SAGA3" | field "paymentStatus")
check "Scenario 3 Status (5=Compensated)" "5" "$STATUS3"
check "Scenario 3 InventoryStatus (2=Released)" "2" "$INV3"
check "Scenario 3 PaymentStatus (3=TimedOut)" "3" "$PAY3"

AFTER_STOCK3=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/products/2" | field "availableStock")
check "Scenario 3 Stock Restored After Compensation" "$BEFORE_STOCK3" "$AFTER_STOCK3"

# --- Scenario 4: Fast Fail Inventory Rejection (Out of Stock) ---
echo ""
echo "--- Scenario 4: Fast-Fail Inventory SoldOut (No Payment Invocation) ---"
KEY4="saga-live-soldout-$TS"
HTTP_CODE4=$(curl -s -o /dev/null -w '%{http_code}' -X POST -H "$HOST_HEADER" -H "Content-Type: application/json" \
  -d "{\"productId\":1,\"quantity\":99999,\"amount\":49.01,\"idempotencyKey\":\"$KEY4\"}" \
  "$GATEWAY/api/saga/checkout")
check "Scenario 4 HTTP Status (400 Bad Request)" "400" "$HTTP_CODE4"

SAGA4=$(curl -s -H "$HOST_HEADER" "$GATEWAY/api/saga/$KEY4")
STATUS4=$(printf '%s' "$SAGA4" | field "status")
INV4=$(printf '%s' "$SAGA4" | field "inventoryStatus")
check "Scenario 4 Status (6=Failed)" "6" "$STATUS4"
check "Scenario 4 InventoryStatus (3=Rejected)" "3" "$INV4"

# --- Scenario 5: Duplicate Idempotent Replay ---
echo ""
echo "--- Scenario 5: Duplicate Idempotency Replay Guard ---"
REPLAY1=$(curl -s -X POST -H "$HOST_HEADER" -H "Content-Type: application/json" \
  -d "{\"productId\":2,\"quantity\":1,\"amount\":5490000.01,\"idempotencyKey\":\"$KEY1\"}" \
  "$GATEWAY/api/saga/checkout")
REPLAY_SUCCESS1=$(printf '%s' "$REPLAY1" | field "success")
REPLAY_MSG1=$(printf '%s' "$REPLAY1" | field "message")
check "Scenario 5 Replay Success" "true" "$REPLAY_SUCCESS1"
contains "Scenario 5 Idempotent Response" "idempotently" "$REPLAY_MSG1"

echo ""
echo "=========================================================="
echo "Saga Live Verification Result: $PASS Passed, $FAIL Failed"
echo "=========================================================="

if [ "$FAIL" -gt 0 ]; then
  exit 1
fi
exit 0
