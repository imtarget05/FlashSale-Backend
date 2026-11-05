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

Then in Render, create or connect the service:

```bash
# Service name: flashsale-payment-api-preview
# Runtime: Docker
# Dockerfile path: ./src/payment-service-java/Dockerfile
# Docker context: ./src/payment-service-java
# Health check path: /health/live
# Branch: preview
```

Environment variables for preview:

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
SPRING_KAFKA_BOOTSTRAP_SERVERS=<preview-kafka-host>:9092
JWT_SIGNING_KEY=<32+ byte secret>
APP_JWT_SECRET=<32+ byte secret>
MANAGEMENT_ENDPOINTS_WEB_EXPOSURE_INCLUDE=health,info,metrics,prometheus
```

### Production branch (`main`)

```bash
git checkout main
git push origin main
```

In Render, use the separate production service:

```bash
# Service name: flashsale-payment-api-prod
# Runtime: Docker
# Dockerfile path: ./src/payment-service-java/Dockerfile
# Docker context: ./src/payment-service-java
# Health check path: /health/live
# Branch: main
```

Point environment variables to production DB/Redis/RabbitMQ values and keep preview isolated from production secrets.

## Frontend deployment on Cloudflare Pages

### Preview branch (`preview`)

```bash
cd frontend
cp .env.example .env
# set VITE_API_URL to the preview Render URL and VITE_ENV=preview
npm ci
npm run build
npx wrangler login
npx wrangler pages project create flashsale-frontend --production-branch main
npx wrangler pages deploy dist --project-name=flashsale-frontend --branch preview
```

Environment variables for preview:

```bash
VITE_API_URL=https://flashsale-payment-api-preview.onrender.com
VITE_ENV=preview
```

### Production branch (`main`)

```bash
git checkout main
git push origin main
npx wrangler pages deploy dist --project-name=flashsale-frontend --branch main
```

Production environment variables in Cloudflare Pages:

```bash
VITE_API_URL=https://flashsale-payment-api.onrender.com
VITE_ENV=production
```

For production, use the main branch and set `VITE_ENV=production`.

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

Do not claim production readiness until local validation, preview validation, secrets review, and approval are complete.
