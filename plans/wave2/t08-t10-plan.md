# Wave 2 (T08–T10) — STAGING_READY evidence plan

**Repos:** FlashSale-Backend (main @ 96dc3d9), AKS-SRE-Platform (main @ f064ad3)
**Goal:** Attempt T08 → T09 → T10 in order; report DONE/PARTIAL/BLOCKED with exact reasons.

## Constraints
- No Docker registry / Azure credentials. Skip push if no creds in env.
- Local kind / Docker Desktop staging simulation only. Record digest as local artifact (not live cloud deploy).
- No terraform apply/destroy; no billable Azure resources.
- Do not fabricate numbers; do not lower thresholds; do not skip tests.
- Commit small on feature branches; PR if push works, else report diff.

## Environment (verified)
- docker 29.6.1, kind 0.33.0, kubectl v1.36.1, trivy 0.74.0, mvn 3.9.16, java 17, ab (no k6).
- Registry creds in env: none (DOCKER_HUB_USERNAME/REGISTRY_USER/GHCR_TOKEN/AZURE_* all unset).
- Build context: src/payment-service-java, Dockerfile: src/payment-service-java/Dockerfile.

## T08
1. docker build -f src/payment-service-java/Dockerfile -t flashsale/payment-service:local-96dc3d9 . ; capture digest via docker inspect.
2. docker login only if creds in env (none → skip push); tag locally, record digest as local artifact.
3. kind create cluster; kind load docker-image.
4. Deploy to kind: postgres:15, redis:7, rabbitmq, payment-service manifests (k8s/ with image+secret replaced). Apply init-schema.sql.
5. Smoke test: curl /healthz; /api/payments login→success→replay→409 conflict→decline→timeout. Verify 2/2 Ready (2 replicas).
6. Rollback: image tag back to previous; verify service returns to Ready.
7. Record: digest, SHA, kind cluster name, smoke results, rollback result, timestamps, exit codes.

## T09
- Define SLIs/SLOs (error rate <1%, latency p95 <500ms, availability >99%) as targets only.
- Controlled outage on kind: stop rabbitmq or postgres pod; observe error/readiness; restart; measure TTD/TTR, RPO/RTO.
- Backup/restore drill: pg_dump postgres; simulate data loss; restore; verify row count matches.
- Record timeline, evidence, measured RPO/RTO, SLO met/not met.

## T10
- Justified load vs /api/payments on kind/local (ab or python concurrent script; real req/s, error rate, p95).
- Dependency scan: mvn dependency:tree; container scan: trivy image; IaC scan: trivy config on AKS terraform/k8s.
- OWASP API review of PaymentController (auth, rate limiting, input validation); document findings disposition.
- Record raw reports tied to SHA 96dc3d9 and image digest.
