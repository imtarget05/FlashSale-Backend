# ADR-006: Database Migration Strategy (Migrations over EnsureCreated)

- **Status**: Accepted
- **Date**: 2026-09-20

## Context
`DatabaseInitializer` called `EnsureCreatedAsync()` on every API boot. The
Worker never called it (initialization race avoided by luck, not design).
`EnsureCreated` bypasses the migrations history table, so once a migration
exists the two mechanisms conflict (`42P07 relation already exists` when
running `database update` against an EnsureCreated database).

## Problem
Schema must be reproducible from zero (fresh container → migrated schema →
unique `Orders.IdempotencyKey` index → seed → API usable), reviewable as code,
and safe when API and Worker boot concurrently.

## Options
1. **Keep EnsureCreated** — simple, but no history, no rollforward/rollback
   story, diverges from production practice.
2. **MigrateAsync on startup (chosen for local/dev)** — both API and Worker
   call `DatabaseInitializer.InitializeAsync` → `MigrateAsync` → idempotent
   seed → Redis mirror. Concurrent `MigrateAsync` is serialized by Postgres
   via the `__EFMigrationsHistory` insert; verified safe in tests.
3. **Migration-only init container / CI job (production)** — run
   `dotnet ef database update` (or a migrate bundle) separately from app
   startup; app boots with `MigrateOnlyAsync` skipped. Supported by the
   split design but not wired yet (Task 4).

## Decision
- `InitialCreate` migration (`Products`, `Orders` + unique
  `IX_Orders_IdempotencyKey`) + `AppDbContextFactory` (env-var connection).
- Runtime uses `MigrateAsync`; `EnsureCreated/EnsureDeleted` banned from
  runtime code (tests use `EnsureDeleted` only to reset Testcontainers DBs).
- Seed stays idempotent (`if (!Any)`) and never duplicates on restart.

## Trade-offs
- Positive: clean-DB verification green (`FlashSaleMigrateVerify` from zero,
  unique index present, history row present); restart keeps schema/orders/stock.
- Negative: startup still couples migration to app boot (acceptable locally;
  production should move to option 3 during Task 4 deploy work).
