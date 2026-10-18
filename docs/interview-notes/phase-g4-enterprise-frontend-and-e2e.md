# Interview Notes: Enterprise Frontend, RBAC, and Full E2E Operations (Phase G4)

These notes summarize the architectural decisions, design patterns, and interview talking points for the enterprise React operations dashboard and end-to-end reconciliation flows in OpsBridge.

---

### 1. Full-Stack Enterprise Architecture & Zero-Mock Rule
**Question**: *"Many portfolio projects have impressive backend architectures but rudimentary or mock-driven frontends. How did you design OpsBridge.Web to represent an authentic enterprise operations tool?"*

**Answer**:
- In enterprise operations (ERP, AP automation, warehouse integration), operators and financial reviewers require an intuitive yet strictly controlled user interface to audit data, review variances, and resolve integration deadlocks without touching Postman or raw database tables.
- **Architectural Layout**:
  - Built with **React 19 + TypeScript + Vite + Tailwind CSS v4**.
  - Server state managed via **TanStack React Query v5** with automatic cache invalidation on mutations (e.g. creating invoices, approving variances, replaying DLQ events).
  - Navigation powered by **React Router v7** with protected route wrappers and client-side session management.
- **The Zero-Mock Rule**:
  - Every button, form, and table is wired to real ASP.NET Core endpoints in `OpsBridge.Api` backed by PostgreSQL and RabbitMQ.
  - Form validations, CSV uploads, tolerance configurations, and discrepancy review submissions produce real state changes in PostgreSQL that are immediately reflected across dashboards and audit logs.

---

### 2. Multi-Persona Authentication & Defense-in-Depth RBAC
**Question**: *"How did you handle user personas, authentication, and role-based access control (RBAC) across the frontend and backend?"*

**Answer**:
- **Authentication**:
  - OpsBridge uses industry-standard JWT Bearer tokens issued via `POST /api/v1/auth/login`.
  - For rapid interview demonstrations and role validation, a Persona Switcher is accessible directly in the navigation bar, allowing instant switching between:
    - **`FinanceReviewer`**: Can approve variances (`ApprovedWithTolerance`), adjust amounts, and reject invoices.
    - **`Operator`**: Monitors connectors, views receipts, and can manually replay Dead-Letter Queue (DLQ) messages.
    - **`Admin`**: Superuser with full operational and compliance authority.
    - **`Auditor`**: Strictly read-only visibility into purchase orders, receipts, matching batches, and audit trails.
- **Defense-in-Depth**:
  - *Frontend Guarding*: UI dynamically disables actions and displays explanatory notices (e.g. *"Read-Only: Switch to FinanceReviewer to approve or reject"*) if the user lacks permissions.
  - *Backend Enforcement*: Never trust the client. Every sensitive endpoint (`/reconciliation/discrepancies/{id}/review`, `/events/replay/{id}`) inspects JWT claims or security headers (`X-User-Role`) and throws HTTP 403 Forbidden if the caller is not in the required role.

---

### 3. Preserving Invariant Integrity across Discrepancy Workflows
**Question**: *"What happens to inventory allocations when an invoice has a discrepancy, is approved with variance, or is rejected?"*

**Answer**:
- **Problem**: In a flash sale or limited-inventory retail environment, holding inventory allocations indefinitely against an invoice that may ultimately be rejected starves other legitimate orders and distorts warehouse accounting.
- **OpsBridge Policy**:
  1. **Discrepancy State**: When an invoice encounters a variance (e.g. price mismatch or missing quantity), planned allocations are calculated in memory to visualize the match on the workspace, but are **not committed** to PostgreSQL stock yet.
  2. **Variance Approval (`ApprovedWithTolerance`)**: When a `FinanceReviewer` authorizes an acceptable variance, `ReviewDiscrepancyUseCase` commits the planned allocations to `GoodsReceiptLines` via the atomic conditional update `TryAllocateQuantityAsync` and transitions the invoice status to `ApprovedWithVariance`.
  3. **Rejection (`Rejected`)**: If the invoice is fraudulent or unacceptable, the reviewer rejects it. Any held allocations are atomically released via `ReleaseAllocationQuantityAsync`, keeping goods receipts available for subsequent supplier invoices.

---

### 4. Contract Interoperability & Postel's Law (Robustness Principle)
**Question**: *"In microservices or paired-agent systems, teams frequently disagree on field naming (e.g. `purchaseOrderNumber` vs `purchaseOrderNo`). How did you handle this without breaking integration contracts?"*

**Answer**:
- We applied **Postel's Law**: *"Be conservative in what you send, be liberal in what you accept."*
- In `docs/contracts/goods-receipt-confirmed-v1.schema.json`, we updated the JSON schema using `anyOf` to accept either `purchaseOrderNumber` or `purchaseOrderNo`.
- In the C# domain model `GoodsReceiptConfirmedV1.cs`, we implemented dual property deserialization:
  ```csharp
  [JsonPropertyName("purchaseOrderNumber")]
  public string? PurchaseOrderNumber { get; set; }

  [JsonPropertyName("purchaseOrderNo")]
  public string? PurchaseOrderNo { get; set; }

  public string EffectivePurchaseOrderNumber =>
      !string.IsNullOrWhiteSpace(PurchaseOrderNumber) ? PurchaseOrderNumber : (PurchaseOrderNo ?? "");
  ```
- This guarantees 100% interoperability with FactoryFlow (MiniERP) without forcing breaking changes on upstream publishers.

---

### 5. The Complete End-to-End Reference Verification Scenario
**Question**: *"Can you walk me through the end-to-end verification scenario for partial receipts and reconciliation reruns?"*

**Answer**:
- Verified in `ReconciliationIntegrationTests.cs` (`E2E_ThreeWayMatching_ReferenceScenario_Po1001_MatchesOnRerun`) running against real PostgreSQL and RabbitMQ Testcontainers:
  1. **Commercial Contract**: Ingested `PO-1001` for 100 PCS of titanium alloy rods @ 20,000 VND. Total: 2,000,000 VND.
  2. **Partial Receipts**: Warehouse receives `GR-1` (60 PCS) followed by `GR-2` (30 PCS), totaling 90 PCS cumulative intake.
  3. **Vendor Invoicing**: Vendor issues `INV-9001` for the full 100 PCS @ 20,000 VND.
  4. **Initial Matching Run**:
     - Status: `Discrepancy`.
     - Detected: `QuantityMismatch` for 10 PCS (100 PCS invoiced vs 90 PCS received).
     - Result: Invoice status is set to `Discrepancy`; zero receipt stock is locked in PostgreSQL.
  5. **Supplemental Delivery**: Warehouse confirms receipt of `GR-3` for the remaining 10 PCS.
  6. **Rerun Matching**:
     - Engine recalculates cumulative available receipts: $60 + 30 + 10 = 100 \text{ PCS}$.
     - Match completes cleanly: Status transitions to `Matched`, discrepancies are empty, and 3 atomic allocations are committed in PostgreSQL ($60 + 30 + 10$).
     - Invoice status transitions to `Matched`.
