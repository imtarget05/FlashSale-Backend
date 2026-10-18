# Interview Notes: Enterprise Three-Way Reconciliation & Concurrency Control (Phase G3)

These notes summarize the business rationale, architectural decisions, and failure modes of OpsBridge's accounts-payable 3-way matching engine for senior engineering interviews.

---

### 1. The Core Business Problem: Accounts Payable Leakage & Three-Way Matching
**Question**: *"What is Three-Way Reconciliation, and why does an enterprise integration platform need it instead of relying on ERP totals?"*

**Answer**:
- In enterprise retail and manufacturing, Accounts Payable (AP) leakage is a multi-million-dollar risk caused by:
  1. Invoices for goods that never arrived at the warehouse.
  2. Vendors billing higher unit prices than contracted in the Purchase Order.
  3. Vendors billing multiple times for the same delivery.
  4. Packaging unit mismatches (e.g. supplier bills in `BOX` of 10, but warehouse receives in `PCS`).
- **Three-Way Matching** validates that:
  - **Purchase Order (PO)**: agreed commercial contract (Price, Quantity, Specifications).
  - **Goods Receipt (GR)**: proof of physical warehouse intake (Actual Received Qty, Lot, Date).
  - **Supplier Invoice**: vendor's claim for disbursement.
- An invoice is only approved for payment if:
  $$\text{Invoice Unit Price} \approx \text{PO Unit Price} \quad (\pm \text{Tolerance})$$
  $$\text{Invoiced Quantity} \le \text{Cumulative Available Received Quantity}$$
- By doing deterministic line-by-line reconciliation, we stop invoice fraud and overpayment before funds leave the company.

---

### 2. Preventing Double-Allocation / Over-Invoicing
**Question**: *"How do you prevent the same warehouse receipt from being matched and paid across multiple supplier invoices?"*

**Answer**:
- If a warehouse receives 100 PCS on a Goods Receipt (`GR-1`), and two invoices arrive (Invoice A for 60 PCS and Invoice B for 60 PCS):
- If the system only checks `60 <= 100`, both invoices would match, resulting in 120 PCS paid for a 100 PCS delivery!
- **Solution**: We track `AllocatedQuantity` directly on `GoodsReceiptLine`.
  - `AvailableQuantity = ReceivedQuantity - AllocatedQuantity`.
  - When Invoice A matches 60 PCS, `AllocatedQuantity` becomes 60, leaving 40 PCS available.
  - When Invoice B arrives requesting 60 PCS, the system detects that only 40 PCS are available, prevents over-allocation, and raises a `QuantityMismatch` discrepancy.

---

### 3. Concurrency Control Under Parallel Invoicing: Atomic Conditional Update
**Question**: *"What happens if two invoice reconciliation requests for 70 PCS each hit your API at the exact same millisecond against a single receipt of 100 PCS?"*

**Answer**:
- In-memory checks alone fail under concurrency because both requests read `AllocatedQuantity = 0` at the start of their transactions.
- Rather than locking the whole table (which destroys throughput), we use an **Atomic Conditional Update** at the database row level:
  ```sql
  UPDATE "GoodsReceiptLines"
  SET "AllocatedQuantity" = "AllocatedQuantity" + @qty
  WHERE "Id" = @id AND ("AllocatedQuantity" + @qty) <= "ReceivedQuantity"
  ```
- PostgreSQL serializes row updates via tuple locks:
  1. Transaction 1 executes the update: `AllocatedQuantity` becomes $0 + 70 = 70$. Rows affected = 1 $\rightarrow$ Transaction 1 succeeds and marks Invoice 1 as `Matched`.
  2. Transaction 2 attempts the update: `70 + 70 = 140 <= 100` is **false**. Rows affected = 0 $\rightarrow$ `TryAllocateQuantityAsync` returns `false`.
  3. The use case detects the race condition, converts Invoice 2's result to a `QuantityMismatch` discrepancy, and prevents over-allocation.
- We verified this with a 2-thread parallel stress test in `ReconciliationIntegrationTests.cs`, proving exactly 70 PCS allocated and zero over-allocation.

---

### 4. Decimal Arithmetic vs Binary Floating-Point
**Question**: *"Why did you avoid `float` or `double` in the reconciliation engine?"*

**Answer**:
- Binary floating-point numbers (`IEEE-754`) cannot accurately represent decimal fractions like `0.1` or `0.01` ($0.1 + 0.2 = 0.30000000000000004$).
- Over thousands of transactions, floating-point rounding errors compound into balance sheet discrepancies and false discrepancy alerts.
- We enforce .NET `decimal` (128-bit fixed-point) with explicit rounding (`MidpointRounding.AwayFromZero`), configured in EF Core as `decimal(18, 4)` for quantities and `decimal(18, 2)` for currency values.

---

### 5. Multi-Receipt Aggregation ($1 \text{ PO} \rightarrow N \text{ GRs}$)
**Question**: *"How does the reference scenario PO-1001 handle delayed or split shipments?"*

**Answer**:
- Large industrial orders rarely arrive in a single shipment. In our reference scenario:
  - PO-1001 orders 100 PCS.
  - GR-1 delivers 60 PCS; GR-2 delivers 30 PCS (Cumulative 90 PCS).
  - Invoice arrives for 100 PCS $\rightarrow$ The engine raises a 10 PCS `QuantityMismatch` discrepancy. The invoice is held in `Discrepancy` status.
  - Later, the final shipment GR-3 delivers 10 PCS.
  - An operator re-runs reconciliation: the engine evaluates the updated snapshot (Cumulative 100 PCS), matches all lines, commits the allocations across GR-1 (60), GR-2 (30), and GR-3 (10), and marks the invoice `Matched`, all while preserving the previous reconciliation audit history.
