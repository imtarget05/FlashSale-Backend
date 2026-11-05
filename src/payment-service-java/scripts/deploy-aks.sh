#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

ACR_LOGIN_SERVER="${ACR_LOGIN_SERVER:-}"
ACR_NAME="${ACR_NAME:-}"
IMAGE_TAG="${IMAGE_TAG:-$(git rev-parse --short HEAD)}"

if [[ -z "$ACR_LOGIN_SERVER" ]]; then
  echo "ACR_LOGIN_SERVER is required, for example: myregistry.azurecr.io" >&2
  exit 1
fi

IMAGE="${ACR_LOGIN_SERVER}/flashsale/payment-service:${IMAGE_TAG}"

# Build the Java image from the module directory.
docker build -t "flashsale/payment-service:${IMAGE_TAG}" .

if [[ -n "$ACR_NAME" ]]; then
  az acr login --name "$ACR_NAME"
fi

docker tag "flashsale/payment-service:${IMAGE_TAG}" "$IMAGE"
docker push "$IMAGE"

# Replace manifest placeholders before applying.
cp k8s/payment-service-aks.yaml /tmp/payment-service-aks.yaml
sed -i.bak "s#REPLACE_WITH_ACR_LOGIN_SERVER#${ACR_LOGIN_SERVER}#g; s#REPLACE_WITH_SHA#${IMAGE_TAG}#g" /tmp/payment-service-aks.yaml
kubectl apply -f /tmp/payment-service-aks.yaml
kubectl rollout status deployment/payment-service --timeout=180s
kubectl get svc payment-service
