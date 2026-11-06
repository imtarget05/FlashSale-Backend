# Payment Service query-plan regression test

## Scope

`QueryOptimizationTest` loads 1,001 synthetic payment rows into a disposable
PostgreSQL 15 Alpine Testcontainers instance and captures
`EXPLAIN (ANALYZE, BUFFERS)` for SQL shapes corresponding to:

- Lookup by the `idempotency_key` primary key.
- The list query ordered by `idempotency_key` with a page size of 20.

The test submits these SQL statements directly through `JdbcTemplate`; it does
not capture Hibernate-generated SQL from a live repository method. It verifies
both plans use `payment_records_pkey` and does not assert latency thresholds;
timings vary by machine, container load, and PostgreSQL state.

## Reproduction

From the repository root:

```bash
mvn -f src/payment-service-java/pom.xml -Dtest=QueryOptimizationTest test
```

The test writes the raw plans to
`src/payment-service-java/target/query-plan-actual.txt`.

## Observed result

Run against FlashSale `main` commit `96dc3d9` on 2026-11-06 with local
Testcontainers:

| Query | Plan | Planning | Execution | Buffers |
| --- | --- | ---: | ---: | ---: |
| Lookup by idempotency key | `Index Scan using payment_records_pkey` | 0.078 ms | 0.062 ms | 3 shared hits |
| First 20 payments ordered by idempotency key | `Limit` over `Index Scan using payment_records_pkey` | 0.070 ms | 0.038 ms | 3 shared hits |

`mvn -B clean verify` passed 113 tests with zero failures, errors, or skips on
the same commit. These measurements establish only that the planner selected
the expected primary-key index for this small synthetic dataset. They are not
production latency measurements and make no performance-improvement claim.

For the larger 100,000-row indexed/partial-index experiment and its limitations,
see [payment-service-postgres-query-plans.md](payment-service-postgres-query-plans.md).
