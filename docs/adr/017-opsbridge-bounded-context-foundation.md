# ADR-017: OpsBridge Bounded Context and API Security Boundary

## Status
Accepted

## Context

OpsBridge is an operations platform integrated with the FlashSale order system and FactoryFlow. The two applications have different write ownership and must not share a database context or let operational workflows mutate authoritative order state directly.

The existing OpsBridge API also includes a local persona-login convenience for the demo UI. It accepts a caller-selected role and is not credential authentication, so exposing it in Production would let an unauthenticated caller mint privileged tokens.

## Decision

### Ownership

| Context | Authoritative data and commands | Explicitly not owned |
|---|---|---|
| FlashSale | Orders, inventory reservations, payment state, order lifecycle | Supplier reconciliation and staff review workflow |
| OpsBridge | FactoryFlow ingestion, procurement snapshots, reconciliation allocations/discrepancies, staff decisions, DLQ replay audit | FlashSale order/payment/inventory tables |

OpsBridge persists only through its `OpsBridgeDbContext`. API endpoints depend on Application use cases and repository ports; EF Core adapters stay in Infrastructure. A FlashSale change must be requested through a versioned contract or an owner-controlled command, never a cross-context database write.

### Roles and route scopes

`CUSTOMER` belongs to the customer-facing FlashSale API and is not an OpsBridge staff role. OpsBridge accepts `Operator`, `Admin`, `Auditor`, and `FinanceReviewer` claims from a trusted JWT.

| Scope | Roles |
|---|---|
| Read operational records, reconciliation results, audit, and DLQ | Admin, Operator, Auditor |
| Create/update connector and procurement snapshots; run reconciliation; replay a DLQ event | Admin, Operator |
| Approve/reject reconciliation discrepancies | Admin, FinanceReviewer |
| Liveness probe | Public |
| FactoryFlow webhook | Public bearer-wise, but requires the configured HMAC/Svix signature and replay-window validation |

Unauthenticated requests to staff routes return 401; authenticated callers outside the route policy return 403. Responses never include connector secrets.

### Authentication boundary

`POST /api/v1/auth/login` is a local demo persona selector only. It is mapped only in Development and Test. Production requires a dedicated `Jwt:SigningKey` of at least 32 UTF-8 bytes and rejects the built-in development key. This repository does not implement credential verification or an external identity provider; the demo login is not production authentication.

Production tokens must be issued by an explicitly trusted identity system and satisfy the configured issuer, audience, signature, and lifetime validation. No Production identity-provider integration is claimed by this ADR.

### Integration contract and errors

FactoryFlow publishes `GoodsReceiptConfirmed` schema version `1.0`, identified by `eventId` and correlated by `correlationId`. The JSON Schema under `docs/contracts/` is the wire contract. HMAC/Svix verification and durable inbox deduplication happen before the event and outbox record are committed; event delivery is at-least-once, not exactly-once.

Known webhook failures use typed domain exceptions and stable HTTP error codes: signature failure 401, expired timestamp or invalid contract 400, conflicting payload for a reused event ID 409, and other domain validation failures 422. Transport and persistence failures are not converted into successful acknowledgements.

### Dead-letter replay

Replay state transition and outbox insertion are performed by one Infrastructure repository adapter behind an Application port. A conditional update permits only one concurrent request to enqueue a replay; subsequent requests return the existing `AlreadyReplayed` outcome. The API preserves the existing status and response shape.

## Alternatives considered

1. Let OpsBridge query or update FlashSale tables directly. Rejected because it bypasses the owner transaction, schema, and invariant boundaries.
2. Keep EF Core access in minimal API handlers. Rejected because endpoint code would own persistence details and undermine the Application port boundary.
3. Keep caller-selected persona login in Production. Rejected because it is equivalent to unauthenticated role issuance.
4. Introduce separate microservices and databases for every OpsBridge workflow now. Rejected; the bounded context is a modular monolith until measured scaling, availability, or team-ownership needs justify extraction.

## Consequences

- Assembly and project-reference tests guard Clean Architecture direction and forbid API endpoint references to `OpsBridgeDbContext`.
- HTTP integration tests cover anonymous/customer/auditor/operator scopes, Production login absence, and signing-key requirements.
- Replay is idempotent under sequential and concurrent requests while retaining at-least-once broker delivery semantics.
- This ADR does not claim real customer authentication, a Production identity-provider integration, or exactly-once message processing.