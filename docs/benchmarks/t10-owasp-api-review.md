# T10 — OWASP API Security Review

## Scope
- Files reviewed:
  - `src/payment-service-java/src/main/java/com/flashsale/payment/controller/PaymentController.java`
  - `src/payment-service-java/src/main/java/com/flashsale/payment/security/SecurityConfig.java`
  - `src/payment-service-java/src/main/java/com/flashsale/payment/security/JwtUtil.java`
- FlashSale SHA: `96dc3d9`
- Framework: Spring Boot 4.0.9 / Spring Framework 7.0.10

## Review findings

### PaymentController (PaymentController.java:27–91)

| OWASP API # | Finding | Severity | Disposition |
|-------------|---------|----------|-------------|
| API1 Broken Object Level Authorization | `GET /api/payments/{idempotencyKey}` checks role (CUSTOMER/STAFF/ADMIN) but does NOT verify the authenticated user owns the idempotency key. A CUSTOMER could read another customer's payment by guessing the key. | MEDIUM | Accepted risk (idempotency keys are unguessable UUIDs); owner: backend team; no expiry. Add ownership check in PaymentService.getPayment. |
| API4 Unrestricted Resource Consumption | No rate limiting on any endpoint (including `/api/auth/login` and `/api/payments`). | HIGH | Accepted risk for staging; add rate limiting (bucket4j/Resilience4j) before production. Owner: platform team. |
| API8 Security Misconfiguration | No security response headers (HSTS, CSP, X-Content-Type-Options). CORS uses defaults (`withDefaults()`). | LOW | Accepted risk; add security headers filter before production. |
| API5 Broken Function Level Authorization | Correctly gated: POST /payments (STAFF/ADMIN), GET /payments (STAFF/ADMIN), GET /payments/{key} (CUSTOMER/STAFF/ADMIN), /api/auth/** permitAll (demo) / denyAll (prod OIDC). | — | Pass. |
| Input validation | `@Valid @RequestBody ProcessPaymentRequest` on POST; `@PathVariable` on GET. | — | Pass. |
| Error handling | Returns 200/400/504 with DTO body; auth errors return 401 via HttpStatusEntryPoint. | — | Pass. |

### SecurityConfig (SecurityConfig.java:27–93)

| OWASP API # | Finding | Severity | Disposition |
|-------------|---------|----------|-------------|
| API2 Broken Authentication | Demo mode uses HS256 JWT (JwtUtil) with `app.jwt.secret`; production uses OIDC resource server. CSRF disabled (appropriate for stateless API). Session STATELESS. | — | Pass for demo; production relies on OIDC (configured via profile). |
| API8 Security Misconfiguration | No explicit security headers (HSTS, etc.). `csrf.disable()` acceptable for stateless JWT API. | LOW | Accepted risk; add headers filter. |
| AuthZ | authorizeHttpRequests with role-based matchers; anyRequest().authenticated() fallback. | — | Pass. |

### JwtUtil (JwtUtil.java:14–96)

| OWASP API # | Finding | Severity | Disposition |
|-------------|---------|----------|-------------|
| API2 Broken Authentication | HS256 signing key validated (min 32 bytes, non-blank). Token expiration configurable (`app.jwt.expiration-ms`, default 86400000ms = 24h). `validateToken`/`isTokenValid` catch JwtException. | — | Pass; HS256 acceptable for demo. Production should use OIDC (configured via SecurityConfig profile). |
| No algorithm agility | Hardcoded HS256 via `Keys.hmacShaKeyFor`. | LOW | Accepted; algorithm fixed by design. |

## Dependency cross-reference (from t10-dependency-scan / t10-container-scan)
- `com.rabbitmq:amqp-client 5.34.0` → CVE-2026-106121 MEDIUM DoS (fixed 5.36.1). Accepted risk; upgrade in next bump.

## Overall disposition
- No CRITICAL findings.
- HIGH: rate limiting missing (API4) — backlog for production.
- MEDIUM: BOLA ownership check (API1) — backlog.
- LOW: security headers, algorithm agility — accepted risk.

## Evidence
- Source files at SHA `96dc3d9` (FlashSale-Backend).
- Image digest: `sha256:9cb5a5ede5331c3150ad57f7e3bcdf3102fc0fddd628a8ec6d9cf2b499905af8`.
