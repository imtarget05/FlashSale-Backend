# ADR-013 — Authentication: PBKDF2 passwords, JWT access + rotating refresh

- Status: Accepted (Interview Release, Phase I)
- Context: the API is currently anonymous-only. The interview brief requires
  register/login/refresh, role-based product writes, and a per-user order view —
  **without** breaking the existing anonymous order flow that the concurrency
  evidence depends on.

## Decision

### 1. Password hashing — PBKDF2-HMAC-SHA256, versioned parameters

```text
algorithm   = PBKDF2-HMAC-SHA256
iterations  = 210_000
salt        = 32 random bytes (RandomNumberGenerator)
hash        = 32 bytes
implementation = Rfc2898DeriveBytes.Pbkdf2(...)   // one-shot static API
comparison  = CryptographicOperations.FixedTimeEquals(...)
```

The `Rfc2898DeriveBytes` **constructors are obsolete in .NET 10**; the one-shot
static `Pbkdf2` method is the supported API. The obsolete constructors are not
used anywhere.

Parameters are **stored per user**, not treated as eternal constants:

```text
PasswordHash = "pbkdf2-sha256$210000$<base64-salt>$<base64-hash>"
```

The encoded string carries algorithm + iteration count, so a future policy change
can rehash on next successful login without a flag day. This mirrors how
ASP.NET Core Identity exposes the iteration count as configuration rather than a
compile-time constant.

### 2. Tokens

| Token | Lifetime | Storage | Purpose |
|---|---|---|---|
| Access (JWT, HS256) | 15 minutes | client only | `Authorization: Bearer` |
| Refresh (JWT, HS256) | 7 days | client only | exchange for a new pair |

Access claims: `sub` (user id), `email`, `role`, `ver` (TokenVersion), `jti`.
Signing key comes from `Auth:JwtSigningKey`; a development fallback exists so
`docker compose` works, and the API **fails fast at startup in Production** when
the key is missing or shorter than 32 bytes.

### 3. Refresh rotation is ATOMIC (the race that matters)

A read-validate-write rotation is wrong: two concurrent refreshes both read
`TokenVersion = 5` and both succeed, so a stolen refresh token is replayable.

The rotation is a single conditional UPDATE:

```sql
UPDATE "Users"
SET    "TokenVersion" = "TokenVersion" + 1
WHERE  "Id" = @userId
  AND  "TokenVersion" = @presentedVersion
```

```text
first refresh  -> rows affected 1 -> SUCCESS (new pair issued)
replayed token -> rows affected 0 -> REJECT  (401)
```

The presented refresh token must also carry `ver` matching the row, so a token
minted before a rotation is rejected even if it is otherwise well-formed.
`AuthServiceTests` proves exactly one of two concurrent rotations wins.

### 4. Logout semantics (explicit, not implied)

```text
logout:
  refresh token is invalid IMMEDIATELY (TokenVersion is bumped)

existing access JWT:
  may remain valid until its own expiry (<= 15 minutes)
```

**No DB lookup is added to every access-token request.** A per-request
`TokenVersion` check would turn every API call into a database round-trip and
couple token validation to Postgres availability. For Interview v1 the 15-minute
access-token window is the accepted, documented trade-off. Shortening it, or
adding a revocation list, is a Phase-VIII concern.

### 5. Authorization — policies, not a CASL re-implementation

```text
CUSTOMER  -> read products, place orders, read own orders
STAFF     -> CUSTOMER + create/update products
ADMIN     -> STAFF + (future) user administration
```

| Endpoint | Rule |
|---|---|
| `POST /api/orders` | **anonymous allowed** (back-compat); `UserId = sub` when authenticated, `null` otherwise |
| `GET /orders/me` | authenticated; returns only `UserId == sub` |
| `POST/PATCH /api/products` | policy `StaffOrAdmin` |
| `GET /api/products*` | anonymous |

`/orders/me` is a simple `UserId == sub` filter — no resource-based handler is
introduced until an endpoint actually addresses a single order by id. Policies
are registered with `AddAuthorizationBuilder()`; a resource-based
`OwnerOrStaff` handler is deliberately **not** built speculatively.

### 6. Backward compatibility (non-negotiable)

- `Order.UserId` is **nullable**; existing rows and anonymous orders keep working.
- `POST /api/orders` keeps its current status codes and idempotency semantics.
- No existing test is modified to accommodate auth.

## Consequences

- **Positive:** no new cryptography is hand-rolled; PBKDF2 and JWT validation are
  BCL/framework primitives. Rotation is race-free by construction (one SQL
  statement), not by locking.
- **Positive:** the anonymous path stays intact, so the oversell/idempotency
  evidence remains valid and comparable.
- **Negative:** a logged-out access token is usable for up to 15 minutes. This is
  documented in `docs/interview-demo/` rather than hidden.
- **Negative:** `Microsoft.AspNetCore.Authentication.JwtBearer` is a real NuGet
  dependency (it is **not** in the `Microsoft.AspNetCore.App` shared framework —
  verified by compile probe). "Zero new packages" was a preference, not a gate;
  hand-rolling JWT validation to avoid one package would be strictly worse.
