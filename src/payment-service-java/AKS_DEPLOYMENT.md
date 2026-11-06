# AKS validation manifests for payment-service

These manifests support an explicitly opted-in, non-production deployment. They
do not provision an AKS cluster, database, cache, broker, or ingress controller.
The deployment uses the `demo` Spring profile and is not suitable for production
or evidence of banking compliance.

## Manifest and secret handling

The Kustomize base is `k8s/`. The deployment expects an externally created
`payment-service-secrets` Secret in the `flashsale` namespace. Secret values are
intentionally not stored in this repository. Create the Secret from a private,
access-controlled env file containing:

- `POSTGRES_JDBC_URL`, `POSTGRES_USER`, `POSTGRES_PASSWORD`
- `RABBITMQ_USER`, `RABBITMQ_PASSWORD`
- `REDIS_PASSWORD` when required by the Redis instance
- `JWT_SIGNING_KEY` (at least 32 bytes)
- `DEMO_AUTH_PASSWORD` (at least 16 characters)

The payment container runs as non-root UID/GID 65534 with privilege escalation
disabled and a read-only root filesystem. Only `/tmp` is writable, through a
64 MiB `emptyDir`.

Do not use the removed placeholder credentials or the demo profile in
production. gRPC is disabled, no gRPC port is published, and the Kubernetes
Service is `ClusterIP`; use a deliberate TLS ingress design before exposing any
endpoint outside the cluster.

## Validate locally without applying

```bash
kubectl kustomize k8s
```

The deployment script builds and pushes an image, then applies manifests. It
refuses to proceed unless both `DEPLOY_NONPROD_AKS=true` and
`EXPECTED_KUBECTL_CONTEXT` are set and the active context matches. It has not
been run as part of this repository work.

## Explicit non-production deploy

Only run this after independently confirming the cluster, image registry,
secret, and budget. Create the Secret directly in the target cluster from a
private env file; never commit or paste its values into Git:

```bash
kubectl create namespace flashsale --dry-run=client -o yaml | kubectl apply -f -
kubectl create secret generic payment-service-secrets \
  --namespace flashsale \
  --from-env-file="$K8S_SECRET_FILE"

cd src/payment-service-java
export ACR_LOGIN_SERVER="<your-acr-login-server>"
export ACR_NAME="<your-acr-name>"
export IMAGE_TAG="$(git rev-parse --short HEAD)"
export EXPECTED_KUBECTL_CONTEXT="<confirmed-nonproduction-context>"
export DEPLOY_NONPROD_AKS=true
bash scripts/deploy-aks.sh
```

Use `kubectl port-forward` for local-only validation rather than adding a public
load balancer. Azure provisioning and production mutation are out of scope.
