#!/bin/sh
# Phase 2 one-shot experiment runner.
# Usage: ./load-tests/concurrency/run_experiment.sh [API_BASE_URL]
# Assumes local PostgreSQL container from docker-compose is running.
set -eu

API_BASE="${1:-http://localhost:5065}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUT_FILE="$REPO_ROOT/docs/benchmarks/phase2-oversell-raw-output.txt"

echo "== Phase 2: overselling experiment ($API_BASE) ==" | tee "$OUT_FILE"
echo "# Executed: $(date -u +%Y-%m-%dT%H:%M:%SZ)" | tee -a "$OUT_FILE"

python3 "$SCRIPT_DIR/oversell_demo.py" \
  --base-url "$API_BASE" \
  --stock 10 \
  --requests 50 \
  2>&1 | tee -a "$OUT_FILE"

echo
echo "Raw output saved to: docs/benchmarks/phase2-oversell-raw-output.txt"