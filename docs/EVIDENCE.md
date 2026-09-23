# Evidence Index — FlashSale-Backend (master)

Mục đích: mỗi claim trong CV/phỏng vấn trỏ tới đúng 1 file evidence có số liệu thật.
Quy ước: số liệu chỉ được claim nếu có raw output hoặc log lệnh đi kèm.

## 1. Concurrency & correctness (v1 story)

| Claim | Evidence | Số liệu |
|---|---|---|
| Overselling tái hiện được | `docs/benchmarks/phase2-oversell-experiment.md` + `phase2-oversell-raw-output.txt` | 50/50 accepted, stock 10→9 |
| Fix bằng atomic conditional UPDATE | `docs/benchmarks/phase3-atomic-update-experiment.md` + raw | 10 accepted / 40 rejected / stock 0 |
| p95 giảm sau Redis + async worker | `docs/benchmarks/phase4-highload-*`, `phase5-tiered-*` + raw | p95 574ms → 29ms |
| Idempotency 202/409-dup, async fulfillment | smoke `payment-automation-smoke.sh` | 21/21 (rerun 2026-09-23) |

## 2. Automation platform (§4–§9, §14, §17)

| Claim | Evidence | Số liệu |
|---|---|---|
| Payment timeout → remind → cancel → release | `scripts/payment-automation-smoke.sh` | 21/21 |
| Low-stock alert + dedup | `scripts/lowstock-alert-smoke.sh` | 11/11 |
| Daily report DB-only + upsert | `scripts/daily-report-smoke.sh` | 12/12 |
| Dashboard + E2E runbook | `docs/evidence/automation-01/phase6-automation-dashboard.md`, `docs/interview-demo/automation.md` | runsToday/success/failed live |

## 3. Backup & DR (Phase 3A/3B/3C)

| Claim | Evidence |
|---|---|
| Local restore drill | `docs/evidence/backup/restore-drill-001.md` (5,198B, restore ~1s) |
| Off-site Azure restore + hardening | `docs/evidence/backup/restore-drill-002-offsite.md` (~20s, firewall Deny + IP allowlist) |
| Integrated recovery runbook | `docs/evidence/backup/restore-drill-003-integrated.md`, `docs/runbooks/disaster-recovery.md` |

## 4. Platform (container / CI / K8s)

| Claim | Evidence |
|---|---|
| Container gate | `docs/evidence/container/phase4-container-gate.md` |
| DevSecOps pipeline | `docs/evidence/ci/phase5-devsecops-pipeline.md` |
| Release engineering (ACR SHA + GitOps pin) | `docs/evidence/release/phase6-release-engineering.md` |
| 7C readiness (offline validators) | `docs/evidence/kubernetes/phase7c-readiness.md` |

## 5. LOCAL v2 (microservices — đang làm, claim có điều kiện)

| Claim | Evidence | Trạng thái 2026-09-24 |
|---|---|---|
| Checkout saga 5 scenarios live | `docs/evidence/saga/saga-live-2026-09-24.log` | 21/21 qua Envoy `flashsale.local` |
| Kafka broker + `orders.events` | `kafka-topics.sh --list` on kind | broker chạy, app vẫn đi RabbitMQ (cutover chưa làm) |
| Outbox drain + replay dedup | `docs/evidence/v2/outbox-inbox-2026-09-24.md` | drain ✅, replay 1+4 ✅, broker-down lộ retry-gap (Cline fix) |
| Outbox/Inbox | migration `AddOutboxAndInbox` (code) | chưa deploy lên kind (kind DB mới tới `AddCheckoutSagas`) |
| Tempo/Loki/KEDA | — | chưa deploy |

## 6. OUT OF SCOPE (không claim)

- AI content generate (model đã xoá khỏi máy; smoke 13/24) — xem plan V2 notice.
- TTS ElevenLabs (cần paid key, chưa làm).
- Container Apps deploy (đã supersede bằng AKS/GitOps, ADR-011).

## Cách reproduce (local)

```bash
dotnet build AzureFlashSale.slnx
dotnet test AzureFlashSale.slnx          # 179 tests (139 unit + 40 integration)
bash scripts/payment-automation-smoke.sh # 21/21 — cần PG/Redis/RabbitMQ ở localhost
bash scripts/lowstock-alert-smoke.sh     # 11/11
bash scripts/daily-report-smoke.sh       # 12/12
# kind saga (cần port-forward gateway 8088):
bash scripts/saga-orchestration-smoke.sh # 21/21
```
