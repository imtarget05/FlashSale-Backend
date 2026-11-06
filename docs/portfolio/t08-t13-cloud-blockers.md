# T08–T13 — Cloud/staging readiness: chặn bởi Azure/không có mạng

## T08: Release pipeline staging

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| Image digest-pinned artifact | ❌ | Không có Docker registry được cấu hình, không có sha256 digest thực từ pipeline release |
| Promotion gate | ❌ | Không có staging environment để thực hiện promotion |
| Pre/post migration | ❌ | Không có staging database để chạy migration |
| Smoke + rollback target | ❌ | Không có live deployment để rollback |

**Kết luận**: `NOT_YET` — cần staging environment và Docker registry với digest thực.

## T09: SLO, incident và recovery

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| Error/latency/queue SLO | ❌ | Không có monitoring stack live để đo SLO |
| Alert delivery | ❌ | Không có Alertmanager/notification channel |
| Controlled outage drill | ❌ | Không có environment để deliberately kill service |
| Backup/restore drill | ❌ | Không có live database để backup/restore |
| RPO/RTO đo thật | ❌ | Không có dữ liệu thực từ drill |

**Kết luận**: `NOT_YET` — cần monitoring + live environment.

## T10: Performance/security release test

Xem chi tiết tại `docs/portfolio/t10-performance-security-release-test.md`.

| Yêu cầu | Trạng thái |
|---------|-----------|
| k6 load test | `NOT_YET` — script đã chuẩn bị, chưa chạy số liệu thực |
| Dependency scan (Trivy) | `DEMONSTRATED` — hosted CI đã chạy |
| Container scan (Trivy image) | `DEMONSTRATED` — hosted CI đã chạy |
| OWASP API review | `PARTIAL` — control đã có, chưa có scan report rõ ràng |
| IaC scan | `LAB_ONLY` — `terraform validate` local, không có `apply` |

## T11a: Azure compute/network lab

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| VM / App Service / AKS | ❌ | Cluster `aks-portfolio-dev` đã destroy, không tạo tài nguyên mới |
| VNet / VPN / LB / App Gateway | ❌ | Không có Terraform apply lên Azure |
| IaC validate/test | ✅ | Terraform modules có sẵn, validate local |

**Kết luận**: `LAB_ONLY` — code và IaC có sẵn, không có live Azure operation.

## T11b: Azure storage/security lab

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| Blob / File / Disk | ❌ | Chỉ có IaC template, không có live storage |
| RBAC / encryption / Key Vault | ❌ | Không có live resource |
| Backup/restore drill | ❌ | Không có live database |

**Kết luận**: `LAB_ONLY` — chỉ có IaC evidence.

## T11c: Azure delivery/scripting lab

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| GitHub Actions → Azure DevOps/GitLab/Jenkins | ❌ | Chỉ có GitHub Actions, không kết nối Azure DevOps |
| Python/PowerShell inventory script | ❌ | Chưa viết |

**Kết luận**: `PARTIAL` — GitHub Actions có sẵn, nhưng không có Azure DevOps/GitLab/Jenkins pipeline run.

## T12: Cost và migration rehearsal

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| Cost export/budget thật | ❌ | Không có quyền truy cập Cost Management |
| Backup PostgreSQL giả | ❌ | Không có live database |
| Cutover/rollback rehearsal | ❌ | Không có môi trường test |
| RPO/RTO đo | ❌ | Không có dữ liệu thực |

**Kết luận**: `NOT_YET` — không có cloud resource, không tạo chi phí mới.

## T13: AKS validation live

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| Deploy digest-pinned Java demo | ❌ | Không có AKS cluster |
| 2/2 Ready pod | ❌ | Không có cluster |
| Azure Monitor/Log Analytics alert | ❌ | Không có live telemetry |
| Rollback và teardown | ❌ | Không có workload live |

**Kết luận**: `BLOCKED` — người dùng xác nhận không tạo cluster/tài nguyên Azure có phí.

## T14: Production rehearsal và quyết định

| Yêu cầu | Trạng thái | Lý do |
|---------|-----------|-------|
| G01–G11 kiểm tra | ❌ | Nhiều gate còn OPEN |
| Rollout canary/rollback rehearsal | ❌ | Không có production/staging environment |
| Go/no-go quyết định | ❌ | Cần owner sign-off |

**Kết luận**: `NO_GO` — có P0 gate mở (G05, G06, G07, G08, G09, G10, G11, G12).

## Tổng kết T08–T13

| Nhóm | Số mục | DEMONSTRATED | PARTIAL | LAB_ONLY | NOT_YET | BLOCKED |
|------|--------|--------------|---------|----------|---------|---------|
| T08–T10 | 3 | 2 | 1 | 0 | 1 | 0 |
| T11a–T11c | 3 | 0 | 1 | 2 | 0 | 0 |
| T12–T14 | 3 | 0 | 0 | 0 | 2 | 1 |
| **Tổng** | **9** | **2** | **2** | **2** | **3** | **1** |

**Lời khuyên**: Các mục T08–T14 không thể hoàn thành được mà không có (1) mạng để tải Docker images/protoc, (2) quyền truy cập Azure subscription, (3) ngân sách cho cloud resource. Tất cả claim về production readiness phải được đánh giá là `NOT_YET` hoặc `LAB_ONLY` cho đến khi có môi trường live.

## Cập nhật trạng thái — 2026-11-06

Các bảng phía trên là snapshot lịch sử. Sau đó đã có quyền đọc Azure subscription và Cost Management:

- Cost Management trả về **58.352554 USD month-to-date** tại `2026-11-06T09:23:58Z`; phần lớn là VM/storage của AKS cũ trong resource group managed đã teardown. Chi phí có thể trễ và chưa phải hóa đơn cuối.
- Chưa có subscription budget hay scheduled Cost Management export. Chỉ có một storage account dùng cho Terraform state; không tái sử dụng nó làm nơi chứa billing data. Xem [`cost query evidence`](../../../AKS-SRE-Platform/docs/evidence/2026-11-06-cost-management-query.md).
- Migration rehearsal 033 → 034, backup/restore rollback, forward recovery và full backend suite đã chạy trên QuanLyKho SHA `e3420a5` (980 passed, 8 skipped, 1 xfailed). Đây là local evidence, không phải Azure DB/cutover acceptance. Xem [`T12 migration evidence`](../../../QuanLyKho/docs/evidence/t12-migration-rehearsal-2026-11-06.md).
- AKS đã được tạo tạm ở `eastasia` để lấy infrastructure evidence, sau đó teardown hoàn tất. Chưa deploy Java payment service lên cluster và không còn AKS live. Xem [`temporary AKS evidence`](../../../AKS-SRE-Platform/docs/evidence/2026-11-06-temporary-aks-validation.md).

**Trạng thái hiện tại:** T12 `PARTIAL` (local migration rehearsal + live cost query; budget/recurring export, cloud DB cutover và RPO/RTO còn OPEN); T13 `NOT_YET` (cần budget/alerts, Java image gắn digest với candidate SHA, dependencies/secret và deploy live); T14 `NO_GO` cho production, cần đóng gate còn lại và owner sign-off.

## Cập nhật T09 / G07–G08 — 2026-11-06

- **G07:** Đã chứng minh local end-to-end: Blackbox probe readiness → Prometheus alert → Alertmanager POST → webhook receiver nhận firing và resolved. Integration test mới xác nhận outage Redis trả HTTP 503 trong giới hạn 5 giây. Đây chưa phải delivery tới destination do owner quản lý; G07 ở mức project gate vẫn `PARTIAL`.
- **G08:** Đã chạy rollback drill trên kind. Candidate không khởi động vì database hiện tại thiếu cột `payment_records.customer_email`; `kubectl rollout undo` đưa deployment về revision trước, 2/2 pods Ready và readiness HTTP 200. Local rollback procedure được chứng minh, nhưng staging/Azure gate vẫn `PARTIAL`.
- **Chưa làm được:** retry rollout trên schema đã migrate; staging/Azure drill (AKS đã destroy, không có credentials/budget/authorization); production alert receiver có owner; owner ký go/no-go.
- **Production decision:** tiếp tục **NO-GO**. Bằng chứng chi tiết nằm trong [`T09 SLO and drill evidence`](../benchmarks/t09-slo.md#addendum--local-g07g08-follow-up-2026-11-06).
