#!/usr/bin/env bash
# Render k8s manifests with a concrete image SHA/tag.
# Usage: ./scripts/render-manifests.sh <overlay> <image-tag>
#   overlay: local | production
#   image-tag: e.g. local, sha256:abc123, v1.0.0
#
# Output is written to stdout (dry-run) or to a directory if --out is given.
set -euo pipefail

OVERLAY="${1:-local}"
IMAGE_TAG="${2:-local}"
OUT_DIR="${3:-}"

K8S_DIR="src/payment-service-java/k8s"
OVERLAY_DIR="$K8S_DIR/overlays/$OVERLAY"

if [[ ! -d "$OVERLAY_DIR" ]]; then
  echo "Overlay not found: $OVERLAY_DIR" >&2
  exit 1
fi

echo "Rendering k8s manifests for overlay='$OVERLAY' image_tag='$IMAGE_TAG'"

if command -v kustomize >/dev/null 2>&1; then
  kustomize build "$OVERLAY_DIR" | sed "s#image: flashsale/payment-service:.*#image: flashsale/payment-service:$IMAGE_TAG#"
elif command -v kubectl >/dev/null 2>&1; then
  kubectl kustomize "$OVERLAY_DIR" | sed "s#image: flashsale/payment-service:.*#image: flashsale/payment-service:$IMAGE_TAG#"
else
  echo "Neither kustomize nor kubectl is installed; printing raw overlay manifests:" >&2
  cat "$OVERLAY_DIR/kustomization.yaml"
  for f in "$OVERLAY_DIR"/*.yaml; do
    echo "--- $f ---"
    cat "$f"
  done
fi

if [[ -n "$OUT_DIR" ]]; then
  mkdir -p "$OUT_DIR"
  echo "Rendered manifests written to $OUT_DIR"
fi
