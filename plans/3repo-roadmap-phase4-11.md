# Plan 3-Repo Roadmap — Phase 4–11

Nguồn đã đọc: `tasks/current.md` (chỉ tick 3A), `plans/roadmap.md` (Phase 1–12),
evidence `docs/evidence/backup/restore-drill-001.md` (3A),
`restore-drill-002-offsite.md` (3B), `restore-drill-003-integrated.md` (3C).

## 0. Định vị 3 repo (flagship đã duyệt)

| ID | Repo | Flagship | Vai trò trong roadmap |
|----|------|----------|----------------------|
| P01 | `FlashSale-Backend` | **Software flagship** | Domain logic, atomic UPDATE, Redis reservation, async worker/idempotency, Clean Architecture, backup/DR scripts + evidence |
| P02 | `Productionized-LegacyApp` | **DevOps Productionization flagship** | Biến legacy thành production-grade: Dockerfile, compose, CI audit gate, DevSecOps scan, ACR artifact |
| P03 | `AKS-SRE-Platform` | **Cloud/SRE flagship** | AKS host cả 2 workload (namespaces `flashsale` + `legacyapp`), Terraform, GitOps, observability 3 tầng, scaling, reliability, DR |

## 1. Phase 3 — trạng thái thực tế (đã DONE, tracking đang lệch)

### P01 — 3A / 3B / 3C = DONE (có evidence)

- **3A local restore — DONE.** `pg_dump -Fc` (5,198 B, sha256 OK) → restore vào DB mới
  `FlashSaleRestoreDrill`, verify data/index/migrations, app smoke 202→completed,
  stock 98→97, DB gốc untouched, restore ~1 s. Evidence: `docs/evidence/backup/restore-drill-001.md`.
- **3B off-site — DONE.** Upload Entra-only lên `stflashsalebackup/postgres-backups`,
  quarantine local (coi như mất), download từ Azure + checksum pass, restore vào
  `FlashSaleRestore3BFinal`, smoke stock 99→98, download 10 s / restore 5 s / tổng ~20 s.
  Hardening: firewall Deny + IP allowlist, lifecycle 30d/90d, Content-MD5 + round-trip SHA-256,
  versioning/soft-delete/WORM-unlocked-1d đã test. Scheduler launchd daily 02:30.
  Evidence: `docs/evidence/backup/restore-drill-002-offsite.md`. ADR-008/ADR-009.
- **3C integrated — DONE.** Runbook `docs/runbooks/disaster-recovery.md` chạy end-to-end:
  RabbitMQ definitions backup (789 B, queue `orders` durable) → wipe volume → rebuild + order thật;
  Redis FLUSHDB → resync từ PostgreSQL truth (`/internal/resync-stock/1`);
  Terraform state migrate lên `stflashs3ctfbk01/tfstate` + locking proof (lease error fail-fast);
  `run-full-recovery.sh` Azure-only (DOWNLOAD 6 s / RESTORE 0 s / VERIFY 1 s).
  Evidence: `docs/evidence/backup/restore-drill-003-integrated.md`.

### P02 — stateless, không ép backup

- LegacyApp là workload **stateless** (không có DB/message truth riêng cần pg_dump).
- Không yêu cầu drill 3A/3B/3C kiểu P01. Productionization tập trung vào
  Dockerfile/compose/CI thay vì backup/restore dữ liệu.

### P03 — ghi nhận Terraform state risk

- Risk đã biết từ 3C: state backend (`stflashs3ctfbk01/tfstate`) dùng Shared Key cho
  azurerm-backend path trong khi backup account disable Shared Key (bất đối xứng, ghi trong ADR-009).
- State của data-protection root đã migrate remote + lock; các root Terraform còn lại
  trong P03 cần kiểm tra backend remote trước Phase 4.

### ⚠️ Tracking gap (cần sửa ngay)

- `tasks/current.md` **chỉ tick `[x] Phase 3A`**, chưa tick 3B/3C dù evidence 002/003 đã tồn tại.
- Hành động: thêm 2 dòng `[x] Phase 3B` (off-site, evidence 002) và
  `[x] Phase 3C` (integrated, evidence 003) vào `tasks/current.md`,
  hoặc gộp thành một dòng Phase 3 DONE với 3 link evidence. Không để tracker lệch với thực tế.

## 2. Phase 4–11 (đã duyệt) — phân công P01 / P02 / P03

### Phase 4 — P02 audit gate

- P02 làm **audit gate**: rà soát Dockerfile, compose, CI hiện có trước khi đụng vào P01/P03.
- Output: checklist gate (build xanh, image chạy, compose up, secrets không hardcode).
- P01/P03 đứng ngoài, chỉ nhận kết quả gate.

### Phase 5 — P01 + P02 DevSecOps

- Áp DevSecOps đồng thời 2 repo code:
  - P01: secret scan, dependency scan (.NET), image scan (API + Worker), non-root đã có (uid 1654) verify lại.
  - P02: tương tự trên Dockerfile legacy + CI của nó.
- P03 chưa tham gia (chỉ nhận policy/mẫu scan nếu tái dùng được).

### Phase 6 — Artifact ACR

- Build + push image lên **ACR**: `flashsale-api`, `flashsale-worker` (P01) và `legacyapp` (P02).
- Tagging + provenance rõ ràng (sha / semver). P03 là consumer (pull từ ACR về AKS).

### Phase 7 — P03 AKS host cả 2 workload

- P03 triển khai AKS với **2 namespaces**: `flashsale` (P01) và `legacyapp` (P02).
- Manifests/Kustomize + GitOps (ArgoCD đã có nền trong `03-AKS-SRE-Platform`).
- P01/P02 chỉ cung cấp image + config mẫu, không tự deploy cụm.

### Phase 8 — Observability 3 tầng

- P03 làm trung tâm: **metrics (Prometheus/Grafana) + logs (Log Analytics/Container Insights) + traces (OpenTelemetry → App Insights)**.
- P01 wiring OTel SDK còn dở (Phase 10 roadmap ghi "Log Analytics đã provision; wiring OTel SDK còn lại") — hoàn tất trong phase này.
- P02 chỉ cần log chuẩn + /health để P03 scrape.

### Phase 9 — Reliability: stateful vs stateless rollback

- **Stateful (P01/flashsale):** rollback phải giữ DB truth — migrate forward-only, Redis rebuild từ PG, broker rebuild từ definitions, không rollback data.
- **Stateless (P02/legacyapp):** rollback đơn giản bằng image tag / rollout undo.
- Output: 2 runbook rollback riêng biệt, test trên AKS (P03).

### Phase 10 — DR

- Kế thừa 3A/3B/3C của P01, nâng lên cấp cụm: backup AKS etcd/manifests (GitOps repo là truth),
  backup-storage đã có, diễn tập restore `flashsale` namespace từ ACR + PG backup.
- P02 DR = redeploy image + config (stateless). RPO/RTO reference ≤24h/≤2h giữ nguyên ngữ nghĩa portfolio.

### Phase 11 — Scaling: KEDA vs HPA

- **P01 worker → KEDA (event-driven):** ScaledObject theo queue depth (ServiceBus/RabbitMQ `orders` queue length, ví dụ `queueLength: 10`, `minReplicaCount 1 maxReplicaCount 10`, polling 15s). Gate: `kubectl get scaledobject -n flashsale`, k6 burst → worker scale-out theo backlog, drain về 1 khi queue rỗng.
- **P01 API + P02 legacyapp → HPA (request/resource-driven):** `cpu 70%` (+ custom `http_requests_per_second` nếu có Prometheus Adapter), `min 2 max 6`. Gate: `kubectl get hpa -n flashsale/legacyapp`, k6 RPS ramp → replicas tăng, RPS giảm → scale-in.
- **Bảng phân biệt PV:** KEDA = scale-to-zero/event backlog (P01 worker); HPA = CPU/RPS, không scale-to-zero (P01 API, P02 stateless). P02 **HPA-only, out-of-scope KEDA** (không queue).
- Output: 2 manifests + 2 evidence scale events (k6 graph + `kubectl describe hpa/scaledobject`).

## 3. Việc tiếp theo (thứ tự)

1. Sửa `tasks/current.md`: tick 3B/3C (xóa tracking gap).
2. Mở Phase 4: chạy P02 audit gate.
3. Sau gate: Phase 5 DevSecOps (P01+P02) → Phase 6 ACR → Phase 7 AKS (P03).
---
## Approved
- Status: APPROVED by user 2026-09-21
- Scope locked: P01 BUILD NEW, P02 MODERNIZE OLD, P03 OPERATE BOTH. Phase 4 P02 gate → Phase 5 DevSecOps → Phase 11 KEDA vs HPA.
- Owner: build orchestrator. Cline agents: read-only trừ khi được giao task, không sửa 2 file này.
