#!/usr/bin/env bash
# LOCAL v2 interview demo: saga compensation + outbox + observability in ~2 min.
# Requires: kind-local-platform cluster up, gateway port-forward on 8088
#   kubectl -n envoy-gateway-system port-forward svc/envoy-platform-gateway-portfolio-gateway-d6017b10 8088:80
set -uo pipefail
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

GATEWAY="${GATEWAY_URL:-http://127.0.0.1:8088}"
H="Host: flashsale.local"
TS=$(date +%s)

echo "=== 1/4 kind workloads ==="
kubectl --context kind-local-platform -n flashsale get pods -o custom-columns=NAME:.metadata.name,READY:.status.containerStatuses[0].ready 2>/dev/null | head -n 12

echo ""
echo "=== 2/4 saga happy-path + decline compensation ==="
KEY1="demo-happy-$TS"
curl -s -H "$H" -X POST "$GATEWAY/api/saga/checkout" -H 'Content-Type: application/json' \
  -d "{\"idempotencyKey\":\"$KEY1\",\"productId\":1,\"quantity\":1,\"amount\":99000.01}" --max-time 60 | head -c 300; echo
KEY2="demo-decline-$TS"
curl -s -H "$H" -X POST "$GATEWAY/api/saga/checkout" -H 'Content-Type: application/json' \
  -d "{\"idempotencyKey\":\"$KEY2\",\"productId\":2,\"quantity\":1,\"amount\":99000.02}" --max-time 60 | head -c 300; echo

echo ""
echo "=== 3/4 outbox drained, nothing stuck ==="
curl -s -H "$H" "$GATEWAY/api/outbox/pending" --max-time 15; echo
curl -s -H "$H" "$GATEWAY/api/outbox/stuck" --max-time 15 | head -c 120; echo

echo ""
echo "=== 4/4 observability (Grafana Loki+Tempo, KEDA) ==="
kubectl --context kind-local-platform get scaledobject -n flashsale 2>/dev/null | head -n 3
kubectl --context kind-local-platform -n monitoring get pods -l app.kubernetes.io/name=loki,app.kubernetes.io/name=tempo 2>/dev/null | head -n 4
kubectl --context kind-local-platform -n monitoring get pods 2>/dev/null | grep -E "loki-0|tempo-0" | head -n 3
echo "Grafana: port-forward svc/kps-grafana 3000:80 (datasources Loki+Tempo healthy)"
echo "Demo complete."
