# Production approval checklist

Use this checklist before requesting production approval or deployment to a shared environment.

## 1. Local validation
- [ ] `docker compose up -d --wait postgres redis rabbitmq payment-service-java`
- [ ] `curl -fsS http://localhost:8081/healthz` returns `{"status":"healthy"}`
- [ ] `mvn -q test -Dtest='!*Integration*'` passes locally
- [ ] dockerized app starts without stale config or legacy .NET references

## 2. Security and secrets
- [ ] No secrets committed to the repo
- [ ] `JWT_SIGNING_KEY` is loaded from Render secrets or a secret manager
- [ ] DB credentials, RabbitMQ credentials, and Redis endpoints are environment-specific
- [ ] TLS is enforced on public endpoints

## 3. Functional validation
- [ ] Payment endpoint is idempotent
- [ ] Payment lookup by idempotency key works
- [ ] Auth/login flow returns a valid JWT
- [ ] Failed payment paths return clean error responses
- [ ] RabbitMQ connectivity is validated for the deployed service

## 4. Observability
- [ ] `/health/live` and `/healthz` are reachable
- [ ] logs are readable and structured
- [ ] application metrics are exposed
- [ ] alerting / dashboards are defined for availability and errors

## 5. Deployment config
- [ ] Render preview service (`flashsale-payment-api-preview`) is configured for branch `preview`
- [ ] Render production service (`flashsale-payment-api-prod`) is configured for branch `main`
- [ ] Cloudflare Pages env vars are set for preview and production
- [ ] Preview branch uses a non-production API target
- [ ] Production branch points to production service endpoints only
- [ ] Render secrets checklist has been reviewed: `docs/deployment/render-secrets-checklist.md`

## 6. Release gate / approvals
- [ ] Release summary is documented
- [ ] Known risks and rollback plan are listed
- [ ] Owner and approver are named
- [ ] Production access / infra approval is available before deployment
- [ ] Preview deployment has been smoke-tested and approved before production push
- [ ] Rollback command / revert path is documented for Render and Cloudflare

## 7. Final sign-off
- [ ] ready for preview
- [ ] ready for production request
- [ ] rollback plan prepared
- [ ] secrets checklist signed off by owner

## Required evidence to attach
- local test logs
- health check output
- deploy screenshots or console URLs
- environment variable checklist
- Render secret review evidence
- approval message / ticket reference
