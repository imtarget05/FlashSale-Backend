# Release approval package

## 1) Brief summary

Project: FlashSale Java backend

Scope:
- Java-first backend service in `src/payment-service-java`
- Local validation against Postgres + Redis + RabbitMQ
- Render preview/prod deployment separated by branch
- Cloudflare Pages frontend kept as a thin preview shell

Current status:
- Java unit and Testcontainers integration tests: 35/35 pass locally
- Docker image build, health/JWT/payment/idempotency smoke checks, and k6 thresholds pass locally
- Render blueprint parses and the deployment workflow passes `actionlint`
- Frontend production build succeeds
- Preview/prod service separation is configured in `render.yaml`
- No cloud deployment has been performed or verified
- GitHub Actions is being rerun after fixing the Docker API version used by Testcontainers
- Production deployment remains gated by approval and valid runtime secrets

Target deployment model:
- Preview: branch `preview` -> Render service `flashsale-payment-api-preview`
- Production: branch `main` -> Render service `flashsale-payment-api-prod`

## 2) Preview evidence

### Local validation commands executed

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend/src/payment-service-java
mvn -B test
```

Result:
- `BUILD SUCCESS`
- 35 tests, 0 failures, 0 errors, 0 skipped, including Testcontainers integration tests

### Frontend build

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend/frontend
npm run build
```

Result:
- Vite production build completed successfully

### Deployment configuration validation

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend
actionlint .github/workflows/*.yml
ruby -e 'require "yaml"; YAML.load_file("render.yaml"); puts "render.yaml parsed"'
```

Result:
- Render blueprint parses successfully and defines separate `preview`/`main` services
- Deploy workflow passes `actionlint`

### Health smoke check path

- Health endpoint: `/health/live`
- Liveness/readiness endpoint: `/healthz`

Expected result:
```json
{"status":"healthy"}
```

### Preview readiness checklist

- [ ] Docker build for the Java service succeeds
- [ ] App responds to `/health/live`
- [ ] `/api/auth/login` works with valid credentials
- [ ] `/api/payments` accepts a signed JWT and returns expected status payload
- [ ] Preview secrets are isolated from production

## 3) Production risk

### Key risks

1. Runtime secrets not yet validated in a real shared environment
   - `JWT_SIGNING_KEY`
   - database credentials
   - Redis and RabbitMQ endpoints

2. Production access is not approved yet
   - The deploy workflow has not been run against live Render or Cloudflare resources.
   - GitHub Actions credentials and environment approvals must be configured on a writable repository.

3. GitHub CI confirmation is pending
   - The previous run failed because Testcontainers used Docker API 1.32. Surefire now pins API 1.44, and the complete 35-test suite passes locally; the next GitHub Actions run must confirm the fix.

4. Public traffic routing is separate from local validation
   - Preview and production must remain isolated until environment owners confirm endpoints and secrets.

### Risk rating

- Severity: Medium
- Reason: core backend logic and auth paths are valid locally, but environment-specific runtime configuration and network exposure still need approver confirmation.

## 4) Rollback plan

### Render backend rollback

1. Open the affected Render service.
2. Select the last known-good deployment.
3. Redeploy the previous image or previous commit.
4. Confirm `/health/live` returns healthy.
5. Re-run a quick login + payment smoke check.

### Cloudflare Pages rollback

1. Open the Cloudflare Pages project.
2. Promote the last known-good deployment back to production.
3. Confirm the frontend still resolves to the correct API URL.
4. Recheck preview/prod env variable separation.

### Rollback evidence to collect

- last stable commit hash
- previous successful deployment URL
- health response after rollback
- owner / approver note for change window

## 5) Sign-off checklist for approver

### Release metadata

- [ ] Release summary attached
- [ ] Commit SHA recorded
- [ ] Preview URL recorded
- [ ] Production URL recorded (if applicable)
- [ ] Approver name recorded

### Functional validation

- [ ] `/health/live` returns healthy
- [ ] `/healthz` returns healthy
- [ ] `/api/auth/login` emits JWT for valid credentials
- [ ] invalid credentials return `401`
- [ ] `/api/payments` accepts valid JWT and returns expected payment status
- [ ] idempotency key behavior is validated

### Security and config

- [ ] Render preview and production secrets are not mixed
- [ ] `JWT_SIGNING_KEY` is set in the correct environment
- [ ] DB / Redis / RabbitMQ settings are environment-specific
- [ ] No secret values are committed to the repo

### Approval decision

- [ ] Approve for preview
- [ ] Approve for production
- [ ] Hold for follow-up changes
- [ ] Reject / needs additional validation

### Approver sign-off

Name: ____________________
Role: ____________________
Date: ____________________
Approval status: ____________________
Comments: ____________________

## 6) Suggested release note template

Title:
`FlashSale Java backend preview validation and production-readiness gate`

Summary:
- Validated Java payment backend locally
- Confirmed Render preview/prod branch separation
- Confirmed GitHub workflow and Render config parse correctly
- Production deployment pending secret validation and approver sign-off

## 7) Decision recommendation

Recommendation:
- Approve for preview deployment only, unless environment owners confirm production secrets and runtime connectivity.
- Do not claim ready-for-production until the sign-off checklist above is complete and live environment validation passes.
