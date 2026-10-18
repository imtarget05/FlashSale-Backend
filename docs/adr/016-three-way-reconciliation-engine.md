# ADR 016: Three-Way Accounts Payable Reconciliation Engine

## Status
Accepted

## Context
In enterprise supply-chain and manufacturing platforms (such as OpsBridge integrating with FactoryFlow), accounts payable reconciliation validates supplier claims before payment authorization.
A purchase order (PO) represents the commercial contract, goods receipts (GR) record physical deliveries at the warehouse, and supplier invoices demand payment.
Without automated reconciliation, retailers suffer from:
1. **Over-invoicing & double-payments**: Vendors billing for undelivered goods or billing multiple times for the same delivery.
2. **Delivery fragmentation**: Large purchase orders arriving across multiple partial shipments ($1 \text{ PO} \rightarrow N \text{ GRs}$).
3. **Discrepancies in unit price, unit of measure (UOM), and taxes**: Invoices using different pack sizes (e.g. BOX vs PCS) or inflating unit costs beyond agreed contract tolerances.
4. **Concurrency races**: Parallel automated invoice processing attempting to allocate against the same warehouse receipt stock simultaneously.

## Decision
1. **Line-by-Line Deterministic 3-Way Matching**:
   - Match by normalized SKU and line number.
   - Normalization layer handles SKU aliases and UOM conversions (e.g., $1 \text{ BOX} = 10 \text{ PCS}$) with explicit conversion factors.
   - Strictly compare pre-tax unit prices: `|UnitPrice_{inv} - UnitPrice_{po}| / UnitPrice_{po} <= Tolerance_{price}`. Tax, discounts, and additional charges are treated as separate document components and never blended into unit price matching.
   - Single-currency constraint: currency codes must match exactly (e.g., VND == VND); any cross-currency mismatch creates a `CurrencyMismatch` discrepancy.
2. **Cumulative Non-Overlapping Receipt Allocation**:
   - `GoodsReceiptLine` tracks `ReceivedQuantity` and `AllocatedQuantity` (`AvailableQuantity = ReceivedQuantity - AllocatedQuantity`).
   - Deliveries from multiple goods receipts for the same PO are aggregated greedily by delivery order.
   - Invoiced quantities are allocated strictly up to available receipt quantities.
3. **Concurrency-Safe Atomic Allocation (Row-Level Conditional Update)**:
   - To prevent over-allocation when concurrent invoice reconciliations run simultaneously, receipt allocation is performed using an atomic conditional update at the database row level:
     ```sql
     UPDATE "GoodsReceiptLines"
     SET "AllocatedQuantity" = "AllocatedQuantity" + @qty
     WHERE "Id" = @id AND ("AllocatedQuantity" + @qty) <= "ReceivedQuantity"
     ```
   - If rows affected == 0, the engine detects that available stock was consumed by a concurrent transaction, rolls back the tentative allocation, records a `QuantityMismatch` discrepancy, and flags the invoice for review.
4. **Discrepancy Workflow & Human Approval**:
   - Mismatches (price, quantity, missing PO, missing GR, duplicate invoice) generate versioned `Discrepancy` records.
   - Human review requires authorized roles (`FinanceReviewer`, `Operator`, `Admin`). Approvals (`ApprovedWithTolerance`, `Adjusted`, `Rejected`) are recorded with user metadata and full immutable audit events.

## Alternatives Considered
1. **Header-Level Total Amount Matching**: Rejected because matching only total sums hides quantity/price fraud (e.g. charging double for half the delivered goods).
2. **Pessimistic Table Locking**: Rejected because locking the entire `GoodsReceipts` table serializes all warehouse intake and reconciliation across unrelated purchase orders.
3. **Floating-Point Financial Math**: Rejected. All quantities and amounts strictly use .NET `decimal` with `MidpointRounding.AwayFromZero` to eliminate IEEE-754 binary floating-point representation drift.

## Consequences & Trade-offs
- **Pros**:
  - Deterministic, auditable financial matching.
  - Provable protection against double-invoicing and concurrent over-allocation.
  - Transparent human-in-the-loop review for edge cases.
- **Trade-offs**:
  - Requires maintaining up-to-date SKU aliases and UOM conversion rules in the `MappingRules` table.
