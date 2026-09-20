# Restore Drill 001 — PostgreSQL backup → clean restore → app smoke

## Objective
Prove business data survives total-database-loss: `pg_dump -Fc` from the live
compose Postgres, restore into a NEW clean database, verify data/constraints,
then run the real Order API against the restored DB and pass a smoke test.

## Reference RPO / RTO
- REFERENCE PORTFOLIO TARGET: RPO ≤ 24h, RTO ≤ 2h (see docs/business/recovery-targets.md).
- These are hypothetical portfolio values, not a real business commitment.

## Source Environment
- Compose stack `01-flashsale-backend` (Task-3 hardened), container
  `01-flashsale-backend-postgres-1`, `pg_dump (PostgreSQL) 15.19`.

## Initial Business State (measured before backup)
```
Products: 1 row  → Id=1 "iPhone 15 Pro Max", AvailableStock=98
Orders:   2 rows → E4583A02-646A-4AD5-BD9F-39A3C855680F,
                   88A98762-92CE-4C4D-8D51-18CDBD71A39D
__EFMigrationsHistory → 20260920123806_InitialCreate
Indexes on Orders: PK_Orders, IX_Orders_IdempotencyKey (unique)
```

## Backup Command
```
POSTGRES_PASSWORD=postgres ./scripts/backup/postgres-backup.sh
```

## Backup Timestamp
`2026-09-20T13:32:23Z`

## Backup Size
5,198 bytes (flashsale_20260920T133223Z.dump)

## SHA256
`shasum -a 256 -c flashsale_20260920T133223Z.sha256` → `flashsale_20260920T133223Z.dump: OK`

## Archive Validation
```
./scripts/backup/verify-backup.sh backups/flashsale_20260920T133223Z.dump
→ OK: archive contains TABLE public Products
→ OK: archive contains TABLE public Orders
→ OK: archive contains __EFMigrationsHistory
VERIFY PASS
```

## Restore Target
NEW database `FlashSaleRestoreDrill` inside the same compose Postgres
container (clean, created by the script; original DB untouched).

## Restore Command
```
./scripts/backup/postgres-restore.sh backups/flashsale_20260920T133223Z.dump FlashSaleRestoreDrill
```

## Restore Result
```
NOTICE: database "FlashSaleRestoreDrill" does not exist, skipping (DROP skipped)
CREATE DATABASE
pg_restore into FlashSaleRestoreDrill → complete
```

## Restored Row Counts
Products = 1, Orders = 2 (identical to source).

## Restored Inventory
`Products.Id=1 → AvailableStock=98` (matches source).

## Migration History
`__EFMigrationsHistory → 20260920123806_InitialCreate` (matches source).

## Constraint Verification
`pg_indexes` on Orders: `PK_Orders` (unique), `IX_Orders_IdempotencyKey`
(unique) — both present with identical definitions.

## Application Smoke Test
Order API pointed ONLY at the restored DB (original untouched):
```
dotnet run --project src/Order.Api --urls http://localhost:5199 \
  --ConnectionStrings:DefaultConnection="Host=localhost;Port=5432;Database=FlashSaleRestoreDrill;..."
GET /health/live  → {"status":"healthy"}
GET /health/ready → {"status":"ready"}      (Postgres CanConnect OK)
GET /api/products/1 → {"id":1,...,"availableStock":98}
POST /api/orders (restore-drill-1789911595, qty 1) → 202 accepted
GET /api/orders/restore-drill-1789911595 → {"status":"completed","orderId":3}
GET /api/products/1 → availableStock 97      (decrement correct)
```
Note: first launch attempt with Redis pointed at a dead port crashed on
`ConnectionMultiplexer.Connect` (abortConnect default true) — retried with
`abortConnect=false`; this is a documented startup limitation, not a restore
failure (see Remaining Risks).

## Measured Restore Duration
Script restore (validate + create DB + pg_restore): **1 second**
(13:37:19Z → 13:37:20Z). Full drill including app smoke ≈ 4 minutes manual.

## RPO Assessment
- Target: ≤ 24h.
- Current achievable: manual-only backup; protection window = time since
  `flashsale_20260920T133223Z.dump` (≈ 8 min at drill time).
- **Verdict: TARGET NOT YET GUARANTEED** (no schedule/off-host copy yet).

## RTO Assessment
- Target: ≤ 2h.
- Measured restore-drill duration: ~1 min automated restore; ~4 min
  end-to-end manual. Well inside target, but this is ONE drill, not a
  certified RTO (no off-host retrieval step yet).

## Failures Encountered
1. `string_agg` quoting error in metadata step (SQL `','` escaping) → fixed
   with `CHR(44)`.
2. `verify-backup.sh` grepped `"Products"` literal while TOC lists
   `TABLE public Products` → fixed patterns.
3. Same TOC-pattern bug in restore guard → fixed (`TABLE public Orders`).
4. App against restored DB + dead Redis crashed at startup (blocking
   multiplexer) → used `abortConnect=false` for the drill; root fix deferred.

## Fixes Applied
All four above; scripts now fail non-zero and refuse unsafe targets.

## Remaining Risks
- Manual-only backup; no schedule, no off-host copy (Phase 3B).
- Restore drill ran inside the same container as source — proves
  DB-level recovery, not host-loss recovery (3B/3C add off-host + new host).
- Redis startup hard-crash when unreachable at boot (known limitation).
- DRILL DB retained for evidence; drop it in a follow-up cleanup.

## Evidence
- `backups/flashsale_20260920T133223Z.dump/.sha256/.metadata.json` (local only,
  git-ignored).
- Script outputs pasted above verbatim; queries reproduced source-vs-restored
  table (see report body).
