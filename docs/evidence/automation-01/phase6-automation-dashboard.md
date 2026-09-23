# Phase 6 — Automation Dashboard (§14) — Evidence

Date: 2026-09-23 · Endpoint: `GET /internal/automation/summary` (+ `/alerts`)

## Summary fields (§14) ↔ live response

```json
{
  "runsToday": 76,
  "successful": 71,
  "failed": 4,
  "retrying": 0,
  "manualReview": 0,
  "averageDurationSeconds": 49.24,
  "topFailingWorkflow": "AiContentGeneration"
}
```

| §14 field | key | live value | note |
|---|---|---|---|
| Runs Today | `runsToday` | 76 | UTC day boundary (fixed: `DateTimeOffset.UtcNow.Date` had `Kind=Unspecified` → local offset → Npgsql `42P10`; now pinned to `new DateTimeOffset(UtcNow.Date, TimeSpan.Zero)`) |
| Success | `successful` | 71 | `Status = Success` |
| Failed | `failed` | 4 | honest — the 4 are the earlier AI-timeout runs before the HttpClient fix |
| Retrying | `retrying` | 0 | `Status = Retrying` |
| Manual Review | `manualReview` | 0 | `Status = ManualReview` |
| Average Duration | `averageDurationSeconds` | 49.24 | over finished runs today |
| Top Failing Workflow | `topFailingWorkflow` | `AiContentGeneration` | aggregate over history |

## Open alerts (spec §6 feed) — `GET /internal/automation/alerts`

```json
[{ "id": 2, "workflowName": "InventoryAutomation", "severity": "LowStock",
   "productId": 1, "message": "Available stock (5) <= reorder threshold (5)",
   "status": "Open", "createdAt": "2026-09-23T13:17:31Z" }]
```

HTTP **200** for both endpoints on the live local build.

## Tests

- Unit **134** (incl. `AutomationDashboardTests`: empty-day zero summary,
  top-failing computation, avg-duration NaN→0, counts per status).
- Integration **40** (incl. `DashboardTests`-style seeding through
  `FlashSaleFixture`).
- Build clean; full suite green.

## Honest limitation

Dashboard is an **internal JSON endpoint**, not a UI — matches spec §14
("Show ...") via API surface; Grafana wiring is planned in v1.1 roadmap
(AKS Observability Phase 8), not claimed here.