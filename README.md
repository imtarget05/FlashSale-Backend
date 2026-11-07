# FlashSale-Backend — Payment Service (Demo)

Portfolio demo: Java/Spring Boot payment microservice with idempotency, JWT auth, RabbitMQ outbox. **Demo only** — simulated PSP, not a real payment processor.

## Evidence (verified 2026-11-07)

- Tested backend SHA: `65a8ce13cf4812c6ce61d55190f2e86eb09e244d` (Render `preview` branch)
- Java CI: https://github.com/imtarget05/FlashSale-Backend/actions/runs/42336764210 (success)
- PR #25 carried CI + Terraform gate fixes to preview; Render deploy `dep-dbnab1qjnfac73bqhg6g` reached Live
- Dedicated Render Free PostgreSQL `flashsale_payment_db_preview`, Render Free KV (TLS), CloudAMQP Little Lemur `flashsale-interview-demo` (AMQPS); Flyway validated 2 migrations (schema v2); RabbitMQ connected in logs
- `/actuator/health/readiness` -> 200 UP; `/health/live` -> 200
- Synthetic payment journey PASS: 10.01 USD -> Succeeded; replay -> same payment ID; GET by idempotency key -> Succeeded
- Negative paths: 10.02 USD -> Declined (success=false); 10.03 USD -> 504 TimedOut (success=false). No real PSP charge.
- Full evidence: see `INTERVIEW_CLOUD_EVIDENCE_2026-11-07.md`

## Open (not claimed)

Release-SHA reconciliation after frontend deploy, Cloudflare Pages verification, backup/restore & rollback drills, load/rate-limit evidence, external IdP, business sign-off. Production go-live NO.

## Stack

Java 17, Spring Boot, PostgreSQL, Redis, RabbitMQ, Flyway, Docker, GitHub Actions.

## Run locally

See `src/payment-service-java`, `frontend/`, `scripts/ci-gate.sh`.
