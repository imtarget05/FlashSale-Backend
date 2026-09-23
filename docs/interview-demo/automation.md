# Interview Demo Runbook — Business Automation (spec §17)

All scripts live in `scripts/` (reproducible, exit non-zero on failure).
Local prerequisites: Docker (postgres/redis/rabbit), built solution, Ollama
+ `qwen3:4b` for scenarios touching AI (C-optional, D).

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

## D. AI product content: create product → AI draft → human approval → publish

```bash
bash scripts/content-support-smoke.sh   # also covers §9 support triage
```
Flow: `POST /api/products/{id}/content/generate` (STAFF only, real qwen3:4b,
bounded retry §12) → draft `ReviewRequired` in queue → `publish` before approve
= **409 (AI never auto-publishes §8)** → approve → publish →
`Products.Description` updated only after human approval (§13).
Same script proves §9: triage grounded in the real order row (facts echo the
order key/status from PostgreSQL) + REFUND category → `requiresHumanReview=true`.

## Test suites

```bash
dotnet build -clp:ErrorsOnly && dotnet test -v q   # 134 unit + 40 integration
```
