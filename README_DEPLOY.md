# FlashSale deploy guide

This repo is structured for a local Java-first stack and a simple preview deployment path using Render for the backend and Cloudflare for the frontend.

## Local run

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend

docker compose up -d --wait postgres redis rabbitmq payment-service-java
curl -fsS http://localhost:8081/healthz
```

Expected output:

```json
{"status":"healthy"}
```

## Backend deployment on Render

The repo now declares two Render web services in [render.yaml](./render.yaml):

- `flashsale-payment-api-preview` → branch `preview`
- `flashsale-payment-api-prod` → branch `main`

### Preview branch (`preview`)

```bash
git checkout -b preview
git push origin preview
```

Create or connect the Render Blueprint from `render.yaml`. The preview service
has automatic deploy disabled so that only the successful GitHub Actions deploy
job can trigger it. Configure the `preview` GitHub Environment with:

- Secret `RENDER_DEPLOY_HOOK_PREVIEW`: the preview service's Render deploy-hook URL
- Variable `RENDER_PREVIEW_URL`: the service base URL, without a trailing slash
- Secrets `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`

Render's Blueprint prompts for these preview service environment variables:

```bash
PORT=8080
SERVER_PORT=8080
SPRING_PROFILES_ACTIVE=production
SPRING_DATASOURCE_URL=jdbc:postgresql://<preview-db-host>:5432/flashsale
SPRING_DATASOURCE_USERNAME=<db-user>
SPRING_DATASOURCE_PASSWORD=<db-password>
SPRING_DATA_REDIS_HOST=<preview-redis-host>
SPRING_DATA_REDIS_PORT=6379
SPRING_RABBITMQ_HOST=<preview-rabbitmq-host>
SPRING_RABBITMQ_PORT=5672
SPRING_RABBITMQ_USERNAME=<rabbitmq-user>
SPRING_RABBITMQ_PASSWORD=<rabbitmq-password>
JWT_SIGNING_KEY=<32+ byte secret>
MANAGEMENT_ENDPOINTS_WEB_EXPOSURE_INCLUDE=health,info,metrics,prometheus
```

Provision the preview database, Redis, and RabbitMQ separately and use only
preview credentials. The app does not require Kafka for this deployment.

### Production branch (`main`)

Production deploys are manual and require approval in the GitHub `production`
Environment. Configure:

- Secret `RENDER_DEPLOY_HOOK_PRODUCTION`: the production service's Render deploy-hook URL
- Variable `RENDER_PRODUCTION_URL`: the service base URL, without a trailing slash
- Secrets `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`
- The same runtime variables listed above, using production-only resources and secrets

## Frontend deployment on Cloudflare Pages

The Pages project is named `flashsale-frontend`; the production branch is
`main`. `wrangler.toml` points to the Vite output at `frontend/dist`. Vite
variables are injected at build time (they are not Cloudflare runtime bindings).

```bash
npx wrangler login
npx wrangler pages project create flashsale-frontend --production-branch main
```

Build and publish a preview from the repository root:

```sh
npm ci --prefix frontend
VITE_API_URL=https://flashsale-payment-api-preview.onrender.com \
  VITE_ENV=preview npm run build --prefix frontend
npx wrangler pages deploy frontend/dist \
  --project-name=flashsale-frontend --branch=preview
```

Build and publish production only after approval:

```bash
npm ci --prefix frontend
VITE_API_URL=https://flashsale-payment-api-prod.onrender.com \
  VITE_ENV=production npm run build --prefix frontend
npx wrangler pages deploy frontend/dist \
  --project-name=flashsale-frontend --branch=main
```

## GitHub Actions deployment

`.github/workflows/cloud-deploy.yml` runs tests and builds on PRs and pushes to
`preview`/`main`. A push to `preview` deploys the preview Render and Pages
targets. Production requires manually dispatching the workflow on `main`,
selecting `deploy_to_production`, and passing the `production` Environment
approval. No production deploy occurs on a normal push.

Configure Render deploy hooks and Cloudflare credentials only in the approved,
environment-scoped GitHub Environments. The workflows do not create or modify
cloud resources; provision and review the Render and Pages projects first.

## Preview helper script

Run the helper script:

```bash
./scripts/deploy-preview.sh preview
```

This script validates the frontend build and prints the next deployment instructions.

## Production gate

Before moving from preview to production, complete the checklist in:

```text
docs/deployment/production-approval-checklist.md
```

Also review the secret-specific checklist:

```text
docs/deployment/render-secrets-checklist.md
```

Prepare the full approval package here:

```text
docs/deployment/release-approval-package.md
```

Do not claim production readiness until local validation, preview validation, secrets review, and approval are complete.

## Rollback procedure

### Roll back the Render backend

```bash
# In Render dashboard:
# 1) Open the affected service
# 2) Choose the last healthy deployment
# 3) Redeploy the previous image / previous commit
# 4) Keep the same environment variables unless the secret set is invalid
# 5) Recheck /health/live and /healthz
```

### Roll back the Cloudflare Pages frontend

```bash
# In Cloudflare Pages dashboard:
# 1) Open the project
# 2) Select the last known-good deployment
# 3) Promote it back to production or redeploy the previous build
# 4) Verify the public app is healthy and the API target is still correct
```

### Release evidence to retain

- last stable commit hash
- URL of the rollback target
- health check output after rollback
- incident summary and owner
