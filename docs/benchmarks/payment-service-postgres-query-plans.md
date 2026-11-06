# Payment Service PostgreSQL query-plan experiment

## Question

Do the current schema indexes support the two database paths used by the Java
payment service: idempotency-key lookup and polling due outbox events?

## Reproduction

Run from the repository root:

```bash
bash src/payment-service-java/scripts/benchmark-query-plans.sh \
  > docs/benchmarks/payment-service-query-plan-raw.txt
```

The script creates a disposable PostgreSQL instance bound to `127.0.0.1` on a
random port. It loads the checked-in `init-schema.sql`, inserts 100,000 fake
payments and 100,000 fake outbox events, runs `ANALYZE`, and uses
`EXPLAIN (ANALYZE, BUFFERS)` for the exact predicates in
`PaymentRepository.findByIdempotencyKey` and
`PaymentOutboxRepository.lockDueEvents`. Of the outbox events, 10,000 are
unpublished and due. The script drops the partial outbox index **only inside
that disposable database** to observe the alternative plan; it then stops the
server and removes its validated temporary directory. No service or developer
database is changed.

## Observed result — 2026-11-06

PostgreSQL 16.15 (Homebrew), macOS, local disposable database. The complete
output is in [payment-service-query-plan-raw.txt](payment-service-query-plan-raw.txt).

| Query | Observed plan | Execution time in this run |
| --- | --- | ---: |
| Payment lookup by `idempotency_key` | `Index Scan using payment_records_pkey`, 1 row, 4 shared buffer hits | 0.022 ms |
| Due outbox batch with `ix_payment_outbox_pending` | `Bitmap Index Scan` → `Bitmap Heap Scan` → sort by `created_at` → `LockRows`, 50 returned | 4.879 ms |
| Same outbox query after dropping the partial index in the temporary database | `Seq Scan` of 100,000 rows → sort → `LockRows`, 50 returned | 10.201 ms |

The primary key directly serves the idempotency lookup. The partial index
limits the outbox scan to unpublished due events, but the query still sorts
10,000 matching rows because the index begins with `next_attempt_at` while
the query orders by `created_at`. That sort is a measured follow-up candidate
if backlog size or relay latency becomes a real bottleneck. No index or query
was changed in this task.

These are single-run local observations, **not a production latency target or
a statistically valid speedup**. A separate rerun on the same laptop produced
different outbox timings (25.663 ms with the index and 22.206 ms without it),
which demonstrates why one comparison cannot establish a reliable speedup.
Cache state, row distribution, row width, concurrent locks, PostgreSQL settings
and instance size can change the plan. Before altering the outbox index,
measure representative backlog/lock patterns and compare multiple warm and
cold runs against the same workload.
