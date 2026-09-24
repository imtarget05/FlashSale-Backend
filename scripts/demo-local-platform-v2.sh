#!/usr/bin/env bash
# LOCAL v2 interview demo: saga compensation + outbox + observability.
# Requires: kind-local-platform cluster up. Set GATEWAY_URL to a port-forward of
# the Envoy data-plane Service; the script discovers and validates that path itself.
set -uo pipefail
REPO_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$REPO_DIR"

GATEWAY="${GATEWAY_URL:-http://127.0.0.1:8088}"
H="Host: flashsale.local"
TS=$(date +%s)

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

json_contains() {
  printf '%s' "$1" | grep -q -- "$2"
}

HEALTH_CODE=$(curl -s -o /dev/null -w '%{http_code}' -H "$H" "$GATEWAY/health/live" --max-time 5 || true)
[ "$HEALTH_CODE" = "200" ] || fail "Gateway health expected 200, got $HEALTH_CODE"


echo "=== 1/4 kind workloads ==="
kubectl --context kind-local-platform -n flashsale get pods \
  -l 'app in (kafka,order-api,order-worker,payment-service,postgres,redis,rabbitmq)' \
  -o custom-columns=NAME:.metadata.name,READY:.status.containerStatuses[0].ready,PHASE:.status.phase 2>/dev/null | head -n 12
kubectl --context kind-local-platform -n flashsale get jobs \
  -o custom-columns=NAME:.metadata.name,COMPLETE:.status.succeeded,PHASE:.status.conditions[-1].type 2>/dev/null | head -n 8

echo ""
echo "=== 2/4 saga happy-path + decline compensation ==="
KEY1="demo-happy-$TS"
RESP1=$(curl -sS -H "$H" -X POST "$GATEWAY/api/saga/checkout" -H 'Content-Type: application/json' \
  -d "{\"idempotencyKey\":\"$KEY1\",\"productId\":2,\"quantity\":1,\"amount\":5490000.01}" --max-time 60)
json_contains "$RESP1" '"success":true' || fail "Happy-path Saga response did not report success: $RESP1"
json_contains "$RESP1" '"status":3' || fail "Happy-path Saga did not complete: $RESP1"
printf '%s\n' "$RESP1"
KEY2="demo-decline-$TS"
HTTP2=$(curl -sS -o /tmp/demo-saga-decline.json -w '%{http_code}' -H "$H" -X POST "$GATEWAY/api/saga/checkout" -H 'Content-Type: application/json' \
  -d "{\"idempotencyKey\":\"$KEY2\",\"productId\":2,\"quantity\":2,\"amount\":5490000.02}" --max-time 60)
[ "$HTTP2" = "422" ] || fail "Decline compensation expected HTTP 422, got $HTTP2"
SAGA2=$(curl -sS -H "$H" "$GATEWAY/api/saga/$KEY2" --max-time 15)
json_contains "$SAGA2" '"status":5' || fail "Decline Saga was not compensated: $SAGA2"
json_contains "$SAGA2" '"inventoryStatus":2' || fail "Inventory was not released: $SAGA2"
printf 'compensation: %s\n' "$SAGA2"

echo ""
echo "=== 3/4 outbox drained, nothing stuck ==="
PENDING=$(curl -sS -H "$H" "$GATEWAY/api/outbox/pending" --max-time 15)
STUCK=$(curl -sS -H "$H" "$GATEWAY/api/outbox/stuck" --max-time 15)
json_contains "$PENDING" '"count":0' || fail "Outbox is not drained: $PENDING"
json_contains "$STUCK" '"count":0' || fail "Outbox has stuck/dead-lettered rows: $STUCK"
printf 'pending: %s\nstuck: %s\n' "$PENDING" "$STUCK"

echo ""
echo "=== 4/4 observability (Grafana Loki+Tempo, KEDA) ==="
SCALED=$(kubectl --context kind-local-platform -n flashsale get scaledobject order-worker -o json 2>/dev/null || true)
printf '%s' "$SCALED" | python3 -c '
import json, sys
obj = json.load(sys.stdin)
conditions = {c.get("type"): c.get("status") for c in obj.get("status", {}).get("conditions", [])}
metrics = obj.get("status", {}).get("externalMetricNames", [])
if conditions.get("Ready") != "True" or conditions.get("Active") != "True":
    raise SystemExit("KEDA ScaledObject is not Ready/Active: " + repr(conditions))
if "s0-kafka-orders-events" not in metrics:
    raise SystemExit("KEDA is not reading Kafka lag: " + repr(metrics))
print("KEDA ready=True active=True metrics=" + ",".join(metrics))
' || fail "KEDA Ready/Active or Kafka lag metric verification failed"
kubectl --context kind-local-platform -n monitoring get pods 2>/dev/null | grep -E "loki-0|tempo-0" | head -n 3
echo "Grafana: provision with kubectl port-forward svc/kps-grafana 3000:80; Loki/Tempo datasource health is verified in the V2 evidence."
echo "Demo complete."
