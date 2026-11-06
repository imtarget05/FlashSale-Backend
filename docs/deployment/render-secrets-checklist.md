# Render environment secrets checklist

Use this checklist before enabling a Render service for preview or production. The goal is to keep preview and production separated, avoid committing secrets, and make rollback/approval explicit.

## Service mapping

- Preview service: `flashsale-payment-api-preview`
- Production service: `flashsale-payment-api-prod`
- Branch mapping:
  - `preview` -> preview service
  - `main` -> production service

## Required secret/env variables

Set these as Environment Variables in Render and keep them secret. Do not put them in the repo or in plain config files.

### Shared app settings

- [ ] `PORT=8080`
- [ ] `SERVER_PORT=8080`
- [ ] `SPRING_PROFILES_ACTIVE=production`
- [ ] `MANAGEMENT_ENDPOINTS_WEB_EXPOSURE_INCLUDE=health,info,metrics,prometheus`

### Database (PostgreSQL)

- [ ] `SPRING_DATASOURCE_URL=jdbc:postgresql://<host>:5432/<database>`
- [ ] `SPRING_DATASOURCE_USERNAME=<preview-or-prod-db-user>`
- [ ] `SPRING_DATASOURCE_PASSWORD=<secret>`

### Redis

- [ ] `SPRING_DATA_REDIS_HOST=<host>`
- [ ] `SPRING_DATA_REDIS_PORT=6379`

### RabbitMQ

- [ ] `SPRING_RABBITMQ_HOST=<host>`
- [ ] `SPRING_RABBITMQ_PORT=5672`
- [ ] `SPRING_RABBITMQ_USERNAME=<user>`
- [ ] `SPRING_RABBITMQ_PASSWORD=<secret>`

### Production identity (OIDC)

- [ ] `OIDC_ISSUER_URI=<approved HTTPS issuer>`
- [ ] `OIDC_AUDIENCE=<payment-service audience>`
- [ ] `OIDC_AUTHORITIES_CLAIM=roles` (or the claim name approved by the issuer)

`JWT_SIGNING_KEY` is only required by non-production HS256 profiles. Production
authentication uses OIDC; local `/api/auth/**` login is disabled.

## Preview-specific checklist

- [ ] Preview service points to `preview` branch only
- [ ] Preview DB, Redis and RabbitMQ values are separate from production
- [ ] Preview uses non-production credentials and non-production hostnames
- [ ] Preview health check is reachable at `/health/live`
- [ ] Preview app returns `{"status":"healthy"}` on `/healthz`
- [ ] Preview authentication matches its explicitly selected profile and identity provider

## Production-specific checklist

- [ ] Production service points to `main` branch only
- [ ] Production secrets are injected from Render environment variables, not committed to git
- [ ] Production endpoints are different from preview endpoints
- [ ] Production database and queue credentials are restricted to production access only
- [ ] Production deploy is performed only after preview validation passes
- [ ] Production rollback plan is prepared before push

## Required before deploy

- [ ] No secret value appears in git diff, logs, screenshots, or issue comments
- [ ] Secret rotation plan exists for any reused keys
- [ ] App boots successfully with env vars loaded
- [ ] `/health/live` and `/healthz` respond 200
- [ ] OIDC bearer-token smoke succeeds with the configured issuer and audience
- [ ] Payment API `/api/payments` returns expected status under the resolved environment

## Rollback plan

1. Revert the Render service to the last stable image or previous commit.
2. Restore previous environment variables if the current secret set is invalid.
3. Confirm `/health/live` is healthy before resuming traffic.
4. Re-run smoke checks for login and payment API after rollback.
5. Record the incident in the release notes and attach the rollback evidence.

## Approval gate

Do not request production approval until all boxes above are checked and release evidence is attached:

- Render service logs
- health check output
- JWT login smoke result
- payment smoke result
- rollback plan recorded
