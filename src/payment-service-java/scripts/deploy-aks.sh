#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

ACR_LOGIN_SERVER="${ACR_LOGIN_SERVER:-}"
ACR_NAME="${ACR_NAME:-}"
IMAGE_TAG="${IMAGE_TAG:-$(git rev-parse --short HEAD)}"
DEPLOY_NONPROD_AKS="${DEPLOY_NONPROD_AKS:-false}"
EXPECTED_KUBECTL_CONTEXT="${EXPECTED_KUBECTL_CONTEXT:-}"

if [[ -z "$ACR_LOGIN_SERVER" ]]; then
  echo "ACR_LOGIN_SERVER is required, for example: myregistry.azurecr.io" >&2
  exit 1
fi
if [[ ! "$ACR_LOGIN_SERVER" =~ ^[A-Za-z0-9.-]+$ ]]; then
  echo "ACR_LOGIN_SERVER must be a registry hostname" >&2
  exit 1
fi
if [[ -n "$ACR_NAME" && ! "$ACR_NAME" =~ ^[A-Za-z0-9]+$ ]]; then
  echo "ACR_NAME contains unsupported characters" >&2
  exit 1
fi
if [[ ! "$IMAGE_TAG" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "IMAGE_TAG contains unsupported characters" >&2
  exit 1
fi
if [[ "$DEPLOY_NONPROD_AKS" != "true" || -z "$EXPECTED_KUBECTL_CONTEXT" ]]; then
  echo "Set DEPLOY_NONPROD_AKS=true and EXPECTED_KUBECTL_CONTEXT to explicitly enable a non-production deploy." >&2
  exit 1
fi

CURRENT_KUBECTL_CONTEXT="$(kubectl config current-context)"
if [[ "$CURRENT_KUBECTL_CONTEXT" != "$EXPECTED_KUBECTL_CONTEXT" ]]; then
  echo "Active kubectl context does not match EXPECTED_KUBECTL_CONTEXT" >&2
  exit 1
fi

if ! kubectl get secret payment-service-secrets --namespace flashsale >/dev/null 2>&1; then
  echo "Create payment-service-secrets outside Git before deploying" >&2
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

kubectl kustomize k8s \
  | sed "s#REPLACE_WITH_ACR_LOGIN_SERVER#${ACR_LOGIN_SERVER}#g; s#REPLACE_WITH_SHA#${IMAGE_TAG}#g" \
  | kubectl apply -f -
kubectl rollout status deployment/payment-service --namespace flashsale --timeout=180s
kubectl get svc payment-service --namespace flashsale
