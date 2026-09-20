# Incident Postmortem: Flash-Sale Overselling (Simulated)

> This is a simulated incident derived from our own Phase 2 experiment. It documents the
> failure exactly as a production postmortem would, so the fix can be traced back to a
> real, reproducible failure — not a hypothetical one.

## Summary
During a simulated flash sale, 50 customers attempted to buy a product with 10 units in
stock **at the same moment**. All 50 received "Order placed successfully". The inventory
system only decremented stock by 1. 49 orders were accepted for inventory that does not exist.

## Impact
- 40 customers would have received cancellation/refund emails after already being told
  "success" (trust damage, support load).
- Revenue/accounting records (Orders table) inconsistent with stock records (Products table).

## Timeline (experiment clock, seconds)
- **T+0.00** — 50 requests released simultaneously by the test harness barrier.
- **T+0.05–0.32** — All requests execute: read stock (10) → check passes → write 9.
- **T+0.32** — p95 latency point; all 50 responses `200 OK` returned.
- **T+0.5** — Audit query: `AvailableStock = 9`, `Orders` count = 50. **Incident detected.**

## Root Cause
The order endpoint performs a **non-atomic read-check-write**:

```csharp
var product = await db.Products.FindAsync(request.ProductId); // READ
if (product.AvailableStock < request.Quantity) ...            // CHECK
product.AvailableStock -= request.Quantity;                   // WRITE (in memory)
await db.SaveChangesAsync();                                  // WRITE (commit)
```

Concurrent transactions interleave between READ and WRITE (PostgreSQL default
READ COMMITTED), so each transaction's decrement overwrites the others: a **lost update**.

## Contributing Factors
- No explicit transaction isolation or row-level locking.
- No atomic conditional write (`UPDATE ... WHERE stock >= qty`).
- No concurrency test existed before Phase 2 — the bug was invisible in single-request testing.

## Resolution (Phase 3)
Replace the in-memory decrement with a single **atomic conditional UPDATE** guarded by a
transaction (`UPDATE Products SET stock = stock - @qty WHERE Id = @id AND stock >= @qty`),
then insert the order and commit. Re-run the same experiment to verify.

## Action Items
| # | Action | Status |
|---|---|---|
| 1 | Make stock decrement atomic (Phase 3) | Done in Phase 3 |
| 2 | Add concurrency experiment to the repo so regressions are reproducible | Done (`load-tests/concurrency/`) |
| 3 | Add idempotency so client retries cannot double-order | Phase 5 |
| 4 | Add load tests + monitoring before real sale events | Phase 6/10 |
