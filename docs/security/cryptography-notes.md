# Security Notes — Cryptography (JWT, RSA, SHA-256)

## JWT Implementation

- **Algorithm:** HS256 (HMAC-SHA256, symmetric) via `jjwt` 0.12.3
- **Signing key:** `APP_JWT_SECRET` / `JWT_SIGNING_KEY` env var (minimum 32 bytes)
- **Token expiration:** 24 hours (`app.jwt.expiration-ms`)
- **Stateless:** `SessionCreationPolicy.STATELESS`, no server-side sessions
- **Password encoding:** BCrypt (`BCryptPasswordEncoder`)

> **Important distinction (interview talking point):** JWT payload is **signed, not encrypted**. Anyone with the token can read the payload (base64). Sensitive data (passwords, PII) must never be placed in the JWT payload. Use HS256 for simple same-service verification; use RS256 (asymmetric RSA) in production so the public key can verify without access to the private signing key.

## RSA

- RSA uses a **public key** and **private key** pair.
- Can be used for **digital signatures** (sign with private key, verify with public key) or **encryption** (encrypt with public key, decrypt with private key).
- **JWT RS256** = JWT signed using RSA + SHA-256. It does **not** mean the JWT payload is encrypted.

## SHA-256

- SHA-256 is a **one-way hash function**, not encryption. It cannot be decrypted/reversed.
- Produces a fixed 256-bit digest.
- **Passwords must NOT be stored as plain SHA-256.** Use a dedicated password hashing algorithm like **BCrypt** or **Argon2** (this service uses BCrypt).

## OWASP Coverage

| OWASP Risk | Mitigation in this service |
|------------|---------------------------|
| Broken Access Control | `@PreAuthorize("hasAnyRole(...)")` on endpoints; roles server-assigned |
| Injection | JPA parameterized queries; no string-concat SQL |
| Sensitive Data Exposure | Passwords BCrypt-hashed; JWT signed not encrypted; no secrets in logs |
| Security Misconfiguration | CSRF disabled (stateless JWT); secure defaults; health endpoints public |
| Identification & Auth Failures | BCrypt password hashing; JWT expiration; invalid credentials → 401 |
