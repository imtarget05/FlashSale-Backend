# T10 — Container Scan Report

## Scope
- Tool: Trivy v0.74.0
- Target image: `flashsale/payment-service:local-96dc3d9 @ sha256:9cb5a5ede5331c3150ad57f7e3bcdf3102fc0fddd628a8ec6d9cf2b499905af8`
- FlashSale SHA: `96dc3d9`
- Command: `trivy image flashsale/payment-service:local-96dc3d9@sha256:9cb5a5ede5331c3150ad57f7e3bcdf3102fc0fddd628a8ec6d9cf2b499905af8`
- Raw report saved to: `/tmp/trivy-image.txt` (not committed; regenerate with command above)

## Summary by target

| Target | Type | Vulnerabilities | Secrets |
|--------|------|-----------------|---------|
| flashsale/payment-service:... (ubuntu 26.04) | ubuntu | 48 | - |
| app/app.jar | jar | 3 | - |
| usr/bin/pebble | gobinary | 0 | - |

## Severity totals
- CRITICAL: 0
- HIGH: 0
- MEDIUM: 44
- LOW: 4
- UNKNOWN: 0

## HIGH / CRITICAL findings
- None. No HIGH or CRITICAL vulnerabilities found.

## Notable findings (all MEDIUM unless noted)

Container scan notable findings:
- Ubuntu OS packages (dash, libc-bin/glibc, libexpat1, libgnutls30t64, libp11-kit0, libsasl2-2, libsystemd0, locales): MEDIUM/LOW CVEs (e.g. CVE-2026-18374, CVE-2026-89092, CVE-2025-66382, CVE-2026-67693, CVE-2026-88647, CVE-2026-88648, CVE-2026-95184, CVE-2026-95210, CVE-2026-13757, CVE-2026-107161). These are base-image (ubuntu 26.04) OS vulnerabilities, patched by OS upgrades, not application code.
- `com.rabbitmq:amqp-client 5.34.0` (app.jar): CVE-2026-106121 MEDIUM — Denial of service; fixed version 5.36.1.

## Disposition
- HIGH/CRITICAL: none — no action required.
- MEDIUM OS package CVEs: accepted risk (base image ubuntu 26.04); rebase to newer Ubuntu when available.
- `amqp-client 5.34.0` CVE-2026-106121: upgrade to 5.36.1+ in next dependency bump (tracked in t10-dependency-scan.md). Owner: platform team.
