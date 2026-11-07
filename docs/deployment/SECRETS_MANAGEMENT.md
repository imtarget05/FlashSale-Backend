# FlashSale Secrets Management
# ===========================
# This file documents all secrets required for production deployment.
# NEVER commit actual secret values. Use GitHub Secrets, Azure Key Vault,
# or Render environment variables.

## Required Secrets (GitHub Actions Environments)

### Preview Environment
| Secret | Description | Source |
|--------|-------------|--------|
| `RENDER_DEPLOY_HOOK_PREVIEW` | Render deploy hook URL for preview service | Render Dashboard |
| `RENDER_PREVIEW_URL` | Preview service base URL (variable, not secret) | Render Dashboard |
| `CLOUDFLARE_API_TOKEN` | Cloudflare API token with Pages deploy permission | Cloudflare Dashboard |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account ID | Cloudflare Dashboard |

### Production Environment
| Secret | Description | Source |
|--------|-------------|--------|
| `RENDER_DEPLOY_HOOK_PRODUCTION` | Render deploy hook URL for production service | Render Dashboard |
| `RENDER_PRODUCTION_URL` | Production service base URL (variable, not secret) | Render Dashboard |
| `CLOUDFLARE_API_TOKEN` | Cloudflare API token with Pages deploy permission | Cloudflare Dashboard |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account ID | Cloudflare Dashboard |

## Required Runtime Secrets (Render / AKS)

### Backend (payment-service)
| Variable | Description | Example |
|----------|-------------|---------|
| `SPRING_DATASOURCE_URL` | PostgreSQL JDBC URL | `jdbc:postgresql://host:5432/flashsale` |
| `SPRING_DATASOURCE_USERNAME` | DB username | `flashsale` |
| `SPRING_DATASOURCE_PASSWORD` | DB password | (secret) |
| `SPRING_DATA_REDIS_HOST` | Redis host | `redis.example.com` |
| `SPRING_DATA_REDIS_PORT` | Redis port | `6379` |
| `SPRING_DATA_REDIS_PASSWORD` | Redis password | (secret) |
| `SPRING_RABBITMQ_HOST` | RabbitMQ host | `rabbitmq.example.com` |
| `SPRING_RABBITMQ_PORT` | RabbitMQ port | `5672` |
| `SPRING_RABBITMQ_USERNAME` | RabbitMQ username | `guest` |
| `SPRING_RABBITMQ_PASSWORD` | RabbitMQ password | (secret) |
| `APP_JWT_SECRET` | JWT signing key (min 32 bytes) | (secret, generated via `openssl rand -base64 48`) |
| `OIDC_ISSUER_URI` | OIDC issuer URL (HTTPS) | `https://keycloak.example.com/realms/flashsale` |
| `OIDC_AUDIENCE` | OIDC audience | `payment-service` |
| `OIDC_AUTHORITIES_CLAIM` | Authorities claim name | `roles` |
| `APP_SECURITY_ALLOWED_ORIGINS` | CORS allowed origins (comma-separated) | `https://api.example.com` |
| `STRIPE_API_KEY` | Stripe API key (optional, enables real PSP) | `sk_live_...` or `sk_test_...` |
| `DEMO_AUTH_PASSWORD` | Demo auth password (demo profile only) | (secret, min 16 chars) |

## Azure Key Vault Integration (optional)

For AKS deployment, use Azure Key Vault CSI driver:

```yaml
# k8s/overlays/production/secret-provider-class.yaml
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: payment-service-secrets
  namespace: flashsale
spec:
  provider: azure
  parameters:
    usePodIdentity: "false"
    useVMManagedIdentity: "true"
    userAssignedIdentityID: "<managed-identity-client-id>"
    keyvaultName: "kv-flashsale-prod"
    cloudName: ""
    objects: |
      array:
        - |
          objectName: postgres-password
          objectType: secret
        - |
          objectName: redis-password
          objectType: secret
        - |
          objectName: rabbitmq-password
          objectType: secret
        - |
          objectName: jwt-signing-key
          objectType: secret
        - |
          objectName: stripe-api-key
          objectType: secret
  secretObjects:
    - secretName: payment-service-secrets
      type: Opaque
      data:
        - objectName: postgres-password
          key: POSTGRES_PASSWORD
        - objectName: redis-password
          key: REDIS_PASSWORD
        - objectName: rabbitmq-password
          key: RABBITMQ_PASSWORD
        - objectName: jwt-signing-key
          key: JWT_SIGNING_KEY
        - objectName: stripe-api-key
          key: STRIPE_API_KEY
```

#

## 🚫 Blockers — Owner Action Required

The following secret values **cannot** be generated or provided by the repository maintainer.
They must be supplied by the project owner via GitHub Secrets, Azure Key Vault, or the
Render dashboard before any cloud deployment or live PSP integration can proceed:

| Secret | Why owner-provided | Where to set |
|--------|-------------------|--------------|
|  (live: ) | Real Stripe account + API key required; no live Stripe transactions possible without it | Stripe Dashboard → GitHub Secret / Azure Key Vault / Render env var |
|  (production) | Production DB credentials belong to the infrastructure owner ||  (production) | Production DB credentials beloduction JWT signing key must be rolled by the owner and never shared | Azure Key Vault / Render dashboard |
|  (production) | Production RabbitMQ credentials | Azure Key Vault / Render dashboard |
|  / Render deploy hooks | Cloud deploy requires owner's Render credentials | GitHub Secrets () |
| Azure AKS credentials | Real deploy to AKS requires owner's Azure subscription + 
     /    /  \    _____   _ _  ___ _
   / /\ \  |_  / | | | '__/ _  / ____ \  / /| |_| | | |  __/
 /_/    \_\/___|\__,_|_|  \___|


Welcome to the cool new Azure CLI!

Use `az --version` to display the current version.
Here are the base commands:


Group
    az

Subgroups:
    account
    acr
    ad
    advisor
    afd
    aks
    ams
    apim
    appconfig
    appservice
    aro
    backup
    batch
    batchai
    bicep
    billing
    bot
    cache
    capacity
    cdn
    cloud
    cognitiveservices
    compute-fleet
    compute-recommender
    config
    connection
    consumption
    container
    containerapp
    cosmosdb
    costmanagement
    data-boundary
    databoxedge
    deployment
    deployment-scripts
    disk
    disk-access
    disk-encryption-set
    dls
    dms
    eventgrid
    eventhubs
    extension
    feature
    functionapp
    group
    hdinsight
    identity
    image
    iot
    keyvault
    lab
    lock
    logicapp
    managed-cassandra
    managedapp
    managedservices
    maps
    mariadb
    monitor
    mysql
    netappfiles
    network
    policy
    postgres
    ppg
    private-link
    provider
    quota
    redis
    relay
    resource
    resourcemanagement
    restore-point
    role
    search
    security
    servicebus
    sf
    sig
    signalr
    snapshot
    sql
    sshkey
    stack
    stack-whatif
    staticwebapp
    storage
    synapse
    tag
    term
    ts
    vm
    vmss
    webapp

Commands:
    configure
    feedback
    find                : Entry point for `az find`.
    interactive
    login               : Log in to access Azure subscriptions.
    logout              : Log out to remove access to Azure subscriptions.
    rest
    self-test
    survey
    upgrade
    version login | Azure CLI / GitHub OIDC |

### What is NOT blocked

- Local development:  files (gitignored) can use locally generated values
- : deterministic local simulation works without any Stripe key
- Unit tests: all run without secrets (mocked )
- Docker build: completes without secrets
- Stripe test mode (): can be added later when the owner provides a Stripe test key

### Verification gate

No cloud deploy, no live Stripe transaction, and no production JWT issuance should be
claimed until the owner has provided the values above and they have been verified in the
target environment.



## 🚫 Blockers — Owner Action Required

The following secret values **cannot** be generated or provided by the repository maintainer.
They must be supplied by the project owner via GitHub Secrets, Azure Key Vault, or the
Render dashboard before any cloud deployment or live PSP integration can proceed:

| Secret | Why owner-provided | Where to set |
|--------|-------------------|--------------|
| `STRIPE_API_KEY` (live: `sk_live_...`) | Real Stripe account + API key required; no live Stripe transactions possible without it | Stripe Dashboard → GitHub Secret / Azure Key Vault / Render env var |
| `SPRING_DATASOURCE_PASSWORD` (production) | Production DB credentials belong to the infrastructure owner | Azure K| `SPRING_DATASOURCE_PASSWORD` (production) | Production DB credentials beloJWT signing key must be rolled by the owner and never shared | Azure Key Vault / Render dashboard |
| `SPRING_RABBITMQ_PASSWORD` (production) | Production RabbitMQ credentials | Azure Key Vault / Render dashboard |
| `RENDER_API_KEY` / Render deploy hooks | Cloud deploy requires owner's Render credentials | GitHub Secrets (`RENDER_API_KEY`) |
| Azure AKS credentials | Real deploy to AKS requires owner's Azure subscription + `az` login | Azure CLI / GitHub OIDC |

### What is NOT blocked

- Local development: `.env` files (gitignored) can use locally generated values
- `SimulatedPaymentProvider`: deterministic local simulation works without any Stripe key
- Unit tests: all run without secrets (mocked `PaymentProvider`)
- Docker build: completes without secrets
- Stripe test mode (`sk_test_...`): can be added later when the owner provides a Stripe test key

### Verification gate

No cloud deploy, no live Stripe transaction, and no production JWT issuance should be
claimed until the owner has provided the values above and they have been verified in the
target environment.

# Secret Rotation

See `docs/security/SECRET_ROTATION_GUIDE.md` for rotation procedures.

## Generating Secrets

```bash
# JWT signing key (min 32 bytes)
openssl rand -base64 48

# Demo auth password (min 16 chars)
openssl rand -base64 24

# Database password
openssl rand -base64 32
```
