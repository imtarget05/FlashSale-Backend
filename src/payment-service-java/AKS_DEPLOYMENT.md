# AKS deployment for payment-service-java

This module includes a Kubernetes manifest for a non-production AKS deployment. The goal is to validate the Java payment service in a public AKS cluster without claiming production go-live.

## Requirements

- Azure CLI logged in and cluster kubeconfig merged
- ACR login server and registry name available if images are pushed to Azure Container Registry
- A running AKS cluster and a reachable public API endpoint
- The service should be treated as demo/live validation only until all P0 gates are closed

## Files

- `k8s/payment-service-aks.yaml` — validation Deployment + Service + ConfigMap; it references an existing Secret and does not create one
- `scripts/deploy-aks.sh` — Build, tag, push, and apply the manifest

## Deploy

Create `payment-service-secrets` in the target namespace from a private env file
outside the repository. It must provide `POSTGRES_JDBC_URL`, `POSTGRES_USER`,
`POSTGRES_PASSWORD`, `RABBITMQ_USER`, `RABBITMQ_PASSWORD`, and
`JWT_SIGNING_KEY` (at least 32 bytes), and `DEMO_AUTH_PASSWORD` (at least 16
characters). Add `REDIS_PASSWORD` if the configured Redis requires authentication.
Do not copy the former placeholder
credentials into a live cluster. The manifest explicitly runs the `demo` profile
with mock identities and RabbitMQ as its only broker; it is for validation only.
This demo profile must never be enabled in production.
PostgreSQL, Redis, and RabbitMQ must be reachable for readiness to pass.

```bash
cd src/payment-service-java
export ACR_LOGIN_SERVER="<your-acr-login-server>"
export ACR_NAME="<your-acr-name>"
export IMAGE_TAG="$(git rev-parse --short HEAD)"

bash scripts/deploy-aks.sh
```

## Validate

```bash
kubectl get deploy,pods,svc -l app=payment-service
kubectl rollout status deployment/payment-service --timeout=180s
kubectl get svc payment-service
kubectl get --raw /api/v1/namespaces/default/services/http:payment-service:80/proxy/actuator/health/readiness
curl http://$(kubectl get svc payment-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}')/healthz
curl http://$(kubectl get svc payment-service -o jsonpath='{.status.loadBalancer.ingress[0].ip}')/health/live
```

## Important guardrails

- This is not production go-live
- Azure production mutation gates must remain closed
- Only use this path for non-production validation or demo deployment
