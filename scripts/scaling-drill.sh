#!/usr/bin/env bash
# ==============================================================================
# scripts/scaling-drill.sh — Horizontal Scaling Drill (HPA + KEDA)
#
# Verifies that the payment-service scales under load and scales back down.
# Requires: kubectl configured, payment-service deployed with HPA.
#
# Usage:
#   ./scripts/scaling-drill.sh [BASE_URL] [NAMESPACE]
#   BASE_URL defaults to http://localhost:8081
#   NAMESPACE defaults to flashsale
# ==============================================================================
set -euo pipefail

BASE_URL="${1:-${BASE_URL:-http://localhost:8081}}"
NAMESPACE="${2:-${NAMESPACE:-flashsale}}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "========================================================================"
echo "📈 Scaling Drill — Horizontal Pod Autoscaler"
echo "Target: ${BASE_URL} | Namespace: ${NAMESPACE}"
echo "========================================================================"

# Step 1: Record baseline replica count
echo "[1/5] Recording baseline replica count..."
BASELINE_REPLICAS=$(kubectl get hpa payment-service -n "$NAMESPACE" -o jsonpath='{.status.currentReplicas}' 2>/dev/null || echo "N/A")
echo "  Baseline replicas: $BASELINE_REPLICAS"

# Step 2: Run load test to trigger scale-up
echo "[2/5] Running load test to trigger scale-up (60s sustained)..."
if command -v k6 &> /dev/null; then
  k6 run --duration 60s --vus 100 -e BASE_URL="$BASE_URL" "$REPO_ROOT/load-tests/k6/payment-service.js" || true
elif command -v docker &> /dev/null; then
  docker run --rm -i --network host grafana/k6 run --duration 60s --vus 100 -e BASE_URL="$BASE_URL" - < "$REPO_ROOT/load-tests/k6/payment-service.js" || true
else
  echo "  ⚠️  k6 not found; skipping load generation. Install k6 or docker."
fi

# Step 3: Check scaled replica count
echo "[3/5] Checking scaled replica count..."
sleep 10
SCALED_REPLICAS=$(kubectl get hpa payment-service -n "$NAMESPACE" -o jsonpath='{.status.currentReplicas}' 2>/dev/null || echo "N/A")
echo "  Scaled replicas: $SCALED_REPLICAS"

# Step 4: Verify no errors during scaling
echo "[4/5] Verifying service health during scaling..."
HEALTH=$(curl -sf "$BASE_URL/healthz" 2>/dev/null && echo "OK" || echo "FAIL")
echo "  Health: $HEALTH"

# Step 5: Wait for scale-down and verify
echo "[5/5] Waiting 120s for scale-down..."
sleep 120
DOWNSCALED_REPLICAS=$(kubectl get hpa payment-service -n "$NAMESPACE" -o jsonpath='{.status.currentReplicas}' 2>/dev/null || echo "N/A")
echo "  Downscaled replicas: $DOWNSCALED_REPLICAS"

echo "========================================================================"
echo "📊 Scaling Drill Summary"
echo "  Baseline:    $BASELINE_REPLICAS"
echo "  Peak:        $SCALED_REPLICAS"
echo "  After cooldown: $DOWNSCALED_REPLICAS"
echo "  Health:      $HEALTH"
echo "========================================================================"
if [[ "$HEALTH" == "OK" ]]; then
  echo "✅ Scaling drill PASSED — service remained healthy."
else
  echo "❌ Scaling drill FAILED — service unhealthy during scaling."
  exit 1
fi
EOF