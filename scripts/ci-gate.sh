#!/usr/bin/env bash
# Local CI gate for FlashSale payment-service (Java).
# Runs before pushing: unit tests, compile, Docker image build, k8s manifest validation.
# Does NOT require cloud credentials. Does NOT deploy.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
JAVA_MODULE="$REPO_ROOT/src/payment-service-java"
K8S_DIR="$JAVA_MODULE/k8s"

echo "=== FlashSale local CI gate ==="
echo "Repo root: $REPO_ROOT"

# 1. Compile Java module
echo "[1/4] Compiling Java module..."
(cd "$JAVA_MODULE" && mvn -B compile -q)

# 2. Run unit tests (no Docker required)
echo "[2/4] Running unit tests (no Docker)..."
(cd "$JAVA_MODULE" && mvn -B test -Dtest='!*Integration*' -q)

# 3. Build Docker image (no push)
echo "[3/4] Building Docker image (no push)..."
docker build -t flashsale/payment-service:local-ci -f "$JAVA_MODULE/Dockerfile" "$JAVA_MODULE"

# 4. Validate k8s manifests (YAML parse only)
echo "[4/4] Validating k8s manifests..."
for f in "$K8S_DIR"/*.yaml; do
  echo "  -> $f"
  python3 -c "import yaml,sys; list(yaml.safe_load_all(open('$f')))" || {
    echo "FAIL: invalid YAML in $f" >&2
    exit 1
  }
done

echo "=== Local CI gate PASSED ==="
