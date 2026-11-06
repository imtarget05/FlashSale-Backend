# FlashSale F04 — hardened payment smoke evidence

**Validation commit:** `a11697a` (included in local `main`)

**Run:** 2026-11-06, isolated local Docker Compose project on host port `18081`
**Environment:** demo-only configuration
**Result:** PASS

## Runtime hardening

The payment container started and its readiness endpoint returned `{"status":"UP"}`.
Docker inspection confirmed:

- UID/GID `65534:65534`
- read-only root filesystem
- all Linux capabilities dropped
- `no-new-privileges` enabled
- `/tmp` provided by a bounded 64 MiB `noexec,nosuid` tmpfs

## Functional smoke

`src/payment-service-java/scripts/smoke-local.sh` passed:

- demo login
- successful payment
- idempotent replay with the same transaction
- conflicting replay rejected with HTTP 409
- declined and timed-out payment outcomes
- confirmed MongoDB audit projection for terminal outcomes

## Image scan

Trivy 0.74.0 scanned the built payment image for vulnerabilities at HIGH and
CRITICAL severity. The Ubuntu runtime packages, application JAR, and `pebble`
binary each reported zero findings; the configured gate passed.

This was a vulnerability scan, not a general misconfiguration or secret scan.
The smoke used an isolated Compose project and disposable dependencies, which
were removed after validation. No shared container, cluster, cloud resource, or
production credential was used or changed. This does not establish a live AKS,
staging, or production deployment.
