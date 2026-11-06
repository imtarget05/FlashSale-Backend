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

## Cập nhật live Azure — T13 (2026-11-06)

- **T13: `DEMONSTRATED` — live AKS workload and operational checks passed.** Đã deploy FlashSale Java source SHA `0eb8f518a531ccdc86294e1a41236c54c531c325` bằng image digest `sha256:da0223892f59394f2c512bdae5c3f520514950625b22c3d393f2081960d3f1d3` lên `aks-validation-t13` ở East Asia; hoàn tất health/readiness, login, payment/idempotency, decline/timeout, outbox/Mongo audit, Azure Monitor metric alert Fired/Resolved, rollback về digest hiện hành, teardown và post-teardown cost query. Query MTD ghi nhận `$58.471605 USD` toàn subscription nhưng chưa có row cho resource group T13; attribution vẫn chờ billing delay.
- Tạo ACR Basic `acrflashsalevalidation.azurecr.io` với admin user disabled; giữ lại sau teardown. AKS và managed node resource group đã vắng khỏi inventory cuối; không sửa `aks-portfolio-dev`. Budget subscription là `$100/month` với cảnh báo 80%/100% actual và 100% forecast, không phải giới hạn chi tiêu.
- **Giới hạn:** alert không gắn action group; không kiểm chứng email/on-call, Log Analytics/KQL, tải, DR hoặc production security. Chưa có cost amount gắn riêng với run này; Cost Management có thể cập nhật trễ. Xem [`T13 live AKS evidence`](../../../AKS-SRE-Platform/docs/evidence/2026-11-06-temporary-aks-validation.md) và [`post-T13 cost query`](../../../AKS-SRE-Platform/docs/evidence/2026-11-06-cost-management-query.md).
- **T12 vẫn `PARTIAL`:** có budget/cost visibility, nhưng chưa có export định kỳ, cloud DB cutover, RPO/RTO hoặc migration rehearsal trên Azure.
- **T14 vẫn `NO_GO`:** các gate production còn mở và cần owner sign-off; demo T13 không chứng minh production readiness.
