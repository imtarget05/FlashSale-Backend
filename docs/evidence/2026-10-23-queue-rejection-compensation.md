# Queue rejection and compensation: one release per reservation attempt

## Business failure being controlled

When the order queue rejects a flash-sale request after Redis has reserved stock,
the API must return a retryable failure and release the reservation. A retrying
repair worker must not emit a second `inventory.released` event: downstream
inventory projections could add back the same unit twice. This is a correctness
gate, not a throughput benchmark.

## Invariants and design

For one **reservation attempt** after queue rejection: no order is persisted,
PostgreSQL stock is unchanged, one `inventory.reserved` and one
`inventory.released` event exist, and the broker receives each once after
outbox dispatch. The attempt's correlation ID is persisted on the reserve row.
A 503 can let the same HTTP key reserve again, so the release event ID is now
derived from both that key and the attempt correlation ID. PostgreSQL's unique
`MessageId` index settles concurrent current-version repairs. Before writing,
the repair checks all outbox states by event type, aggregate key **and attempt
correlation ID**; this covers pre-upgrade release rows with random IDs,
including rows already published or dead-lettered.

The read is a migration compatibility guard; the unique index remains the race
barrier for new writes. It opens a fresh DbContext so a failed `SaveChanges`
cannot make a tracked but uncommitted row look durable. A 23505 is translated
to a duplicate only for `IX_OutboxMessages_MessageId`, and a duplicate is
considered repaired only after a fresh committed-row lookup confirms it. The
failed Added entity is detached before retry so the next `SaveChanges` cannot
repeat the old error. A dead-lettered release must be requeued by an operator,
not cloned by repair.

## Reproduction and local verification

- Base checkout: `bbe47b947e3fd8593eaabd9d7d41f578707e1d30` on
  `codex/ops-phase1-delivery`; changes in this note were uncommitted at test time.
- Environment: local .NET 10, Docker Testcontainers with PostgreSQL 15,
  Redis 7 and RabbitMQ 3; synthetic data only.
- RED: `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj --no-restore --filter FullyQualifiedName~RepeatedCompensationRepair_EmitsOneReleaseForTheSameRequest --verbosity quiet`
  returned 1 failed / 0 passed before the fix. The pre-upgrade random-ID
  regression also returned 1 failed / 0 passed before the compatibility guard.
- RED: `SameHttpKeyAfterTwoRejectedQueueAttempts_HasOneReleasePerReservationAttempt`
  returned 1 failed / 0 passed with key-only event identity; the second
  rejected reservation had no balancing release.
- RED: `K_FirstReleaseSaveFails_TrackedFailedRowDoesNotPoisonRetry` failed
  against real PostgreSQL: expected one release row, observed zero. The first
  release insert exceeded the database's `EventType` length. EF retained that
  failed Added entity in the same DbContext and poisoned later retries.
- RED: `LookupFailsAfterRedisRelease_ReturnsSystemBusyAndRecordsFailureMarker`
  threw the lookup exception to the caller, while
  `DuplicateErrorWithoutCommittedRelease_DoesNotClaimCompensationSucceeded`
  produced no marker. Both now pass with bounded retry and committed-row
  verification. A complete PostgreSQL outage still prevents a durable marker;
  the already committed reserve row must be reconciled after storage recovers.
- GREEN: the same project filtered to `SubmitOrderCompensationFailureTests`
  returned 7 passed / 0 failed after the corrective fix.
- Real dependency gate: `dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~OutboxCrashWindowTests.H_|FullyQualifiedName~OutboxCrashWindowTests.I_' --verbosity quiet`
  returned 2 passed / 0 failed before the attempt-scoped refinement. The final
  focused command also included `J_` and returned 3 passed / 0 failed. H
  asserted zero orders, unchanged PostgreSQL stock 10, exactly one reserve and
  one release outbox row, two broker deliveries with those types, and no third
  ready message. I seeded a pre-upgrade random-ID release row and asserted
  repair left one DB row and one broker delivery. J issued two queue-rejected
  attempts with the same HTTP key: Redis and PostgreSQL stock both remained 10,
  zero orders were persisted, and the outbox contained two distinct correlation
  IDs, each with one reserve and one release; RabbitMQ delivered four messages
  and had no fifth ready message.
- `K_` now passes against real PostgreSQL after detaching the failed entity;
  it asserts one durable release, zero false failure markers, and the lookup
  index on a fresh schema. `L_` migrates an existing schema with a historical
  release row, checks that the index appears, and that the row remains
  queryable. `M_` forces a different unique constraint (outbox primary key):
  it remains a `DbUpdateException`, not a false message-ID duplicate.
- Final `dotnet build AzureFlashSale.slnx --no-restore --verbosity quiet`:
  `Build succeeded`, 0 errors, 3 existing `NU1603` warnings.
- Final `dotnet test tests/UnitTests/FlashSale.UnitTests.csproj --no-build --no-restore --verbosity quiet`:
  192 passed / 0 failed.
- Final `dotnet test tests/IntegrationTests/FlashSale.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~OutboxCrashWindowTests --verbosity quiet`:
  13 passed / 0 failed on PostgreSQL, Redis and RabbitMQ Testcontainers.

The build emitted existing `NU1603` warnings because NuGet resolved
Testcontainers 4.0.0 in place of requested 3.12.0. They did not fail the gate;
dependency pinning is separate work.

## Related 50/10 capacity gate and limits

The earlier 50-contender/10-stock harness on `main` verifies exact 10 accepted,
40 rejected, 10 persisted orders and final stock 0 (CI run 39982588513,
completed success on SHA `ef1f827ca6036a324cc23ac2f6dd310689332a52`,
concurrency-harness job success; see `load-tests/concurrency/oversell_demo.py`).
This compensation change is a new
uncommitted delta and must pass CI on its eventual commit SHA before claiming a
merged gate. The queue-rejection test exercises a deliberately rejecting queue
port with real PostgreSQL, Redis and RabbitMQ outbox delivery; it does not
simulate a RabbitMQ network partition or prove production SLOs. The repair
method is currently callable but no scheduled repair worker was verified in
this slice; its automatic operation is a separate gate.

## Mixed-version rollout gate

Older API instances write random release IDs. A rolling deployment that lets
those writers race with new instances cannot guarantee one release for an
attempt. Apply the lookup-index migration first, stop accepting new orders on
old instances, drain their in-flight requests and verify zero old writers,
then start the new version. If that gate is not observed, do not claim
mixed-version safety. The local tests cover existing historical rows and
new-version concurrency only; they do not prove an overlapping deployment.
