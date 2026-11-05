# Payment Service — Security Notes

## Authentication

- **JWT (HS256)** issued by `POST /api/auth/login`.
- Signing key configured via `APP_JWT_SECRET` / `JWT_SIGNING_KEY` environment variable (minimum 32 bytes).
- Token expiration: 24 hours (configurable via `app.jwt.expiration-ms`).
- Stateless sessions (`SessionCreationPolicy.STATELESS`).
- Password encoding: **BCrypt** (Spring Security `BCryptPasswordEncoder`).

> **Note on JWT RS256 vs HS256**: This implementation uses HS256 (symmetric) for simplicity. In production, RS256 (asymmetric RSA) is recommended so the public key can verify tokens without access to the private signing key. JWT payload is **not encrypted** — it is signed only. Sensitive data must not be placed in the JWT payload.

## Authorization (RBAC)

| Role | Endpoints |
|------|-----------|
| `CUSTOMER` | `GET /api/payments/{idempotencyKey}` (own payments) |
| `STAFF` | `GET /api/payments/{idempotencyKey}`, `GET /api/payments` (list) |
| `ADMIN` | `GET /api/payments/{idempotencyKey}`, `GET /api/payments` (list) |
| All roles | `POST /api/payments`, `POST /api/auth/login`, health endpoints |

- `@PreAuthorize("hasAnyRole(...)")` on controller methods.
- CSRF disabled (stateless JWT API).
- Roles are server-assigned; client cannot self-assign (mirrors .NET ADR-013 §5).

## Input Validation

- `@Valid` on `@RequestBody` in `PaymentController`.
- Bean Validation constraints: `@NotBlank`, `@NotNull`, `@Size`, `@DecimalMin`, `@Email`, `@Size(min=8)`.
- `MethodArgumentNotValidException` handled globally → 400 with field-level error map.
- Idempotency key max length 128 chars (matches DB column).

## OWASP Considerations

| OWASP Risk | Mitigation |
|------------|-----------|
| Broken Access Control | `@PreAuthorize` on all mutating/read endpoints; roles server-assigned |
| Injection | JPA parameterized queries; no string concatenation in SQL |
| Sensitive Data Exposure | JWT not encrypted; passwords BCrypt-hashed; no secrets in logs |
| Security Misconfiguration | CSRF disabled for stateless API; secure defaults; health endpoints public only |
| Identification & Authentication Failures | BCrypt password hashing; JWT expiration; invalid credentials → 401 |

## Secrets Management

- `JWT_SIGNING_KEY` must be set via environment variable (never committed).
- `.env.example` provided; `.env` is gitignored.
- Production: use Docker secrets or platform secret manager (Azure Key Vault, etc.).

## Error Handling

- Global `@RestControllerAdvice` catches all exceptions.
- No stack traces leaked to client.
- Error responses use generic messages (`INTERNAL_ERROR`, `VALIDATION_ERROR`, `NOT_FOUND`).
