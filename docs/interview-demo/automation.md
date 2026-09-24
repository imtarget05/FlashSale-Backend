# Interview Demo Runbook — Business Automation (spec §17)

All scripts live in `scripts/` (reproducible, exit non-zero on failure).
Local prerequisites: Docker (postgres/redis/rabbit), built solution.

## A. Payment automation: timeout → reminder → cancellation → stock release → audit

```bash
bash scripts/payment-automation-smoke.sh
```
Flow: place order (PendingPayment) → set `PaymentDueAt` in the past → run
payment-timeout scan → reminder recorded → scan past grace → order Cancelled,
stock released (DB + Redis), `payment.expired`/`inventory.released` published,
`AutomationRuns` row `Success`.

## B. Low stock: reduce inventory → threshold detected → alert → notification

```bash
bash scripts/lowstock-alert-smoke.sh
```
Flow: drop `AvailableStock` below `ReorderThreshold` → low-stock scan →
deduplicated Open `StockAlerts` row + `inventory.low_stock` event + audit run.

## C. Daily report: trigger → metrics → summary → stored report

```bash
bash scripts/daily-report-smoke.sh
```
Flow: `POST /internal/automation/daily-report` → aggregates (orders, revenue,
failed payments, cancellations, top products, low stock, refunds, AOV) computed
**from PostgreSQL only** → one `DailyReports` row (rerun upserts, idempotent).
Dashboard numbers for the demo:
`GET /internal/automation/summary` → `{runsToday, successful, failed,
retrying, manualReview, averageDurationSeconds, topFailingWorkflow}` and
`GET /internal/automation/alerts` → open low-stock alerts.

## Test suites

```bash
dotnet build -clp:ErrorsOnly && dotnet test -v q   # 134 unit + 40 integration
```
