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
- `OIDC_ISSUER_URI` (HTTPS issuer URL for the Identity Provider)
- `OIDC_AUDIENCE` (API identifier/audience for the payment-service resource)
- `OIDC_AUTHORITIES_CLAIM` (typically `roles`)
- `DEMO_AUTH_PASSWORD` only when the demo profile is intentionally used

The payment container runs as non-root UID/GID 65534 with privilege escalation
disabled and a read-only root filesystem. Only `/tmp` is writable, through a
64 MiB `emptyDir`.

The sample production profile expects the OIDC variables to be present and valid.
`SPRING_PROFILES_ACTIVE` in `k8s/configmap.yaml` is set to `production` and the
service is behind a TLS/HTTPS ingress. Configure the actual issuer URL, audience,
and DNS host before deploying to a public cluster.

For TLS and ingress, ensure cert-manager is installed and `ClusterIssuer`
`letsencrypt-prod` exists. Update the host in `k8s/ingress.yaml` (currently
`api.quan-ly-kho.tanmainguyenbinh.workers.dev`) and the allowed origins list in the ConfigMap to
match your real domain before applying the manifests.

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
