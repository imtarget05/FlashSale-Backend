# Production approval checklist

Use this checklist before requesting production approval or deployment to a shared environment.

## 1. Local validation
- [ ] `docker compose up -d --wait postgres redis rabbitmq payment-service-java`
- [ ] `curl -fsS http://localhost:8081/healthz` returns `{"status":"healthy"}`
- [ ] `mvn -q test -Dtest='!*Integration*'` passes locally
- [ ] dockerized app starts without stale config or legacy .NET references

## 2. Security and secrets
- [ ] No secrets committed to the repo
- [ ] `JWT_SIGNING_KEY` and `APP_JWT_SECRET` are loaded from Render/Cloudflare secrets or a secret manager
- [ ] DB credentials, RabbitMQ credentials, and Redis endpoints are environment-specific
- [ ] TLS is enforced on public endpoints

## 3. Functional validation
- [ ] Payment endpoint is idempotent
- [ ] Payment lookup by idempotency key works
- [ ] Auth/login flow returns a valid JWT
- [ ] Failed payment paths return clean error responses
- [ ] RabbitMQ/Kafka consumers are healthy and not silently stopped

## 4. Observability
- [ ] `/health/live` and `/healthz` are reachable
- [ ] logs are readable and structured
- [ ] application metrics are exposed
- [ ] alerting / dashboards are defined for availability and errors

## 5. Deployment config
- [ ] Render backend environment variables are set in the service config
- [ ] Cloudflare Pages env vars are set for preview and production
- [ ] Preview branch uses a non-production API target
- [ ] Production branch points to production service endpoints only

## 6. Release gate / approvals
- [ ] Release summary is documented
- [ ] Known risks and rollback plan are listed
- [ ] Owner and approver are named
- [ ] Production access / infra approval is available before deployment

## 7. Final sign-off
- [ ] ready for preview
- [ ] ready for production request
- [ ] rollback plan prepared

## Required evidence to attach
- local test logs
- health check output
- deploy screenshots or console URLs
- environment variable checklist
- approval message / ticket reference
