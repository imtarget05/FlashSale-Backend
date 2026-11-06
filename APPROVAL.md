# Production Go/No-Go Approval — FlashSale Backend

## Decision: PENDING OWNER SIGN-OFF

---

## 1. Release metadata

| Field | Value |
|-------|-------|
| Repository | FlashSale-Backend |
| Branch | `main` |
| Commit SHA | ____________________ (pin the approved release candidate) |
| Preview URL | `https://flashsale-payment-api-preview.onrender.com` |
| Production URL | `https://flashsale-payment-api-prod.onrender.com` |
| Approver name | ____________________ |
| Approver email | ____________________ |
| Role | Release Owner / Security Owner / System Owner |
| Date | ____________________ |
| Signature / confirmation | ____________________ |

---

## 2. Owner assignment (C2 — phân phối trách nhiệm)

| Vai trò | Tên | Email | Xác nhận |
|---------|-----|-------|----------|
| Release Owner | ____________________ | ____________________ | [ ] |
| Security Owner | ____________________ | ____________________ | [ ] |
| System Owner | ____________________ | ____________________ | [ ] |

---

## 3. Conditions met (trước khi ký G12)

- [ ] **C0 Release Gate** — còn thiếu named owners, cost/export owner và quyết định phạm vi demo payment
- [ ] **Wave 2 staging** — FlashSale preview deploy failed; chưa có health và payment E2E trên cloud
- [ ] **Wave 3 engineering** — chưa có restore/rollback drill và monitoring/alerting được kiểm chứng trên candidate
- [ ] **Secrets configured** — tên GitHub Secrets đã có; chưa xác minh giá trị, scope và rotation sau khi lộ
- [x] **Rollback drill documented** — thủ tục rollback đã ghi ở `docs/deployment/release-approval-package.md` §4
- [ ] **UAT passed** — thử nghiệm thực tế trên production (G11)
- [ ] **Security review signed off** — Security Owner ký (G11)
- [ ] **Compliance sign-off** — System Owner ký (G11)

---

## 4. Go/No-Go decision (G12)

### Quyết định: ____________________ (GO / NO-GO / HOLD)

**Lý do:** ____________________

**Rủi ro chấp nhận được:** ____________________

**Điều kiện phải hoàn tất trước khi production live:** ____________________

---

## 5. Sign-off

| Người ký | Vai trò | Chữ ký / Email xác nhận | Ngày |
|----------|---------|------------------------|------|
| ____________________ | Release Owner | ____________________ | __________ |
| ____________________ | Security Owner | ____________________ | __________ |
| ____________________ | System Owner | ____________________ | __________ |

---

## 6. Ghi chú cho các giai đoạn tiếp theo (khi có credentials)

1. Chỉ thêm Azure credentials nếu workflow FlashSale thực sự dùng Azure; không thêm quyền ngoài nhu cầu triển khai.
2. Thu hồi và tạo lại các credential/deploy hook đã lộ; cập nhật GitHub/Render/Cloudflare trực tiếp trong dashboard.
3. Xác minh các biến môi trường Render cho database, Redis, RabbitMQ và OIDC bằng giá trị riêng của preview/production.
4. Chạy `terraform apply` với overlay `environments/prod/terraform.tfvars` (AKS-SRE-Platform / FlashSale infra).
5. Deploy Render services theo `render.yaml` (preview → production).
6. Cấu hình custom domain + TLS qua Cloudflare.
7. Chạy rollback drill thực tế trên production.
8. Thu thập evidence production (timestamped logs, SHA, digest).
9. Ký G12 trong file này rồi mới công bố production live.

---
