# Acceptance Criteria — Commerce + OpsBridge (OpsBridge bounded context)

> Gherkin Given/When/Then. Mỗi scenario được map tới test case và evidence thực tế trong `traceability-matrix.md`.

## Feature: Dead-letter event replay (idempotent, conditional)

**Scenario: Successful replay of a dead-letter event**
  Given a dead-letter event exists in PostgreSQL with `IsReplayed = false`
  When an Operator calls `POST /api/v1/events/replay/{eventId}`
  Then the response status is `ReplayQueued`
  And exactly one `OutboxMessage` with `Status = Pending` is persisted
  And the dead-letter event is marked `IsReplayed = true` with `ReplayedBy` set

**Scenario: Duplicate replay returns AlreadyReplayed (no double effect)**
  Given a dead-letter event was already replayed (`IsReplayed = true`)
  When the same `eventId` is replayed again
  Then the response status is `AlreadyReplayed`
  And no additional `OutboxMessage` is created for that eventId

**Scenario: Concurrent replay of the same event**
  Given 12 concurrent Operator replay requests for the same `eventId`
  When all requests execute
  Then exactly 1 returns `ReplayQueued` and 11 return `AlreadyReplayed`
  And exactly 1 matching `OutboxMessage` exists in the database

**Scenario: Replay of a non-existent event**
  Given no dead-letter event with the given `eventId`
  When an Operator calls replay
  Then the response status is `NotFound` (HTTP 404)

## Feature: Three-way reconciliation and discrepancy review

**Scenario: PO–GR–Invoice match on rerun (reference scenario)**
  Given PO-1001, GR1, GR2 and invoice for the same items
  When reconciliation runs
  Then discrepancies are detected; after GR3 completes, rerun yields `Matched`
  And inventory allocation is consistent (no double allocation)

**Scenario: Approve discrepancy with tolerance**
  Given a discrepancy with price variance within tolerance
  When FinanceReviewer approves with tolerance
  Then invoice status = `ApprovedWithVariance`
  And goods receipt lines are allocated (consumed)
  And an audit event is recorded

**Scenario: Reject discrepancy releases inventory**
  Given a discrepancy with excessive variance
  When FinanceReviewer rejects
  Then invoice status = `Rejected`
  And goods receipt line `AvailableQuantity` is restored (no allocation)
  And an audit event is recorded

**Scenario: Auditor cannot review (read-only)**
  Given an Auditor calls the discrepancy review endpoint
  When the request is made
  Then the response is HTTP 403 Forbidden

## Feature: Authorization boundary (deny-by-default)

**Scenario: Anonymous access to staff route**
  Given no credentials
  When `GET /api/v1/connectors` is called
  Then HTTP 401 Unauthorized

**Scenario: Customer role cannot access OpsBridge staff route**
  Given a JWT with role `CUSTOMER`
  When `GET /api/v1/connectors` is called
  Then HTTP 403 Forbidden

**Scenario: Auditor read allowed, write denied**
  Given a JWT with role `Auditor`
  When `GET /api/v1/connectors` → 200; `POST /api/v1/connectors` → 403

**Scenario: Production demo login not exposed**
  Given environment = Production
  When `POST /api/v1/auth/login` with caller-selected role
  Then HTTP 404 Not Found (demo route not mapped)

**Scenario: Production requires dedicated signing key**
  Given Production without `Jwt:SigningKey`
  When application starts
  Then `InvalidOperationException` is thrown

## Feature: Webhook ingestion resilience

**Scenario: Webhook accepted when RabbitMQ is down**
  Given RabbitMQ is unavailable
  When a valid FactoryFlow webhook arrives
  Then HTTP 202 Accepted
  And the event is committed to PostgreSQL Outbox as `Pending`
  And no successful acknowledgement is faked if persistence fails

**Scenario: Duplicate webhook (same eventId) is deduped**
  Given an eventId already processed
  When the same webhook payload is resent
  Then HTTP 200 with `Duplicate` status
  And no duplicate business effect
