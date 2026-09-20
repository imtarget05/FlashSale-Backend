# Recovery Targets (Portfolio Mode)

> REFERENCE PORTFOLIO TARGET — hypothetical, for design and drill comparison only.
> No real business selected these values.

## Definitions

- **RPO** (Recovery Point Objective): maximum acceptable data loss measured in
  time. RPO 24h means after recovery the database may be up to 24h behind.
- **RTO** (Recovery Time Objective): maximum acceptable time to restore service
  after disaster is declared.

## Portfolio reference targets

| Target | Value | Meaning |
|---|---|---|
| RPO | ≤ 24 hours | one successful logical backup per day is the minimum rhythm |
| RTO | ≤ 2 hours | from "DB host declared dead" to "app smoke test green on restored DB" |

## Current (Phase 3A) reality

- Backups are **manual** (`scripts/backup/*.sh`, run by a human).
- Achievable RPO = time since last manual backup → **TARGET NOT YET GUARANTEED**.
- Scheduling + off-host copy belong to Phase 3B.
- This drill measures one manual restore duration and reports it as
  MEASURED RESTORE DRILL DURATION (not a certified RTO).
