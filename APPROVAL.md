# Production Go/No-Go Approval — FlashSale Backend

## Decision: GO for portfolio demo; HOLD for production

---

## 1. Release metadata

| Field | Value |
|-------|-------|
| Repository | FlashSale-Backend |
| Branch | `main` |
| Commit SHA | `a8e6f9d13fe92642b146b845731496956afc6348` (local portfolio reference; not the current remote `main`) |
| Preview URL | `https://flashsale-payment-api-preview.onrender.com` |
| Production URL | `https://flashsale-payment-api-prod.onrender.com` |
| Approver name | Mai Nguyễn Bình Tân |
| Approver email | tanmainguyenbinh@gmail.com |
| Role | Release Owner / Security Owner / System Owner |
| Date | 6/11/2026 |
| Signature / confirmation | Mai Nguyễn Bình Tân — GO for portfolio demo; production HOLD (confirmed 6/11/2026) |

---

## 2. Owner assignment (C2 — phân phối trách nhiệm)

| Vai trò | Tên | Email | Xác nhận |
|---------|-----|-------|----------|
| Release Owner | Mai Nguyễn Bình Tân | tanmainguyenbinh@gmail.com | [x] |
| Security Owner | Mai Nguyễn Bình Tân | tanmainguyenbinh@gmail.com | [x] |
| System Owner | Mai Nguyễn Bình Tân | tanmainguyenbinh@gmail.com | [x] |

---

## 3. Conditions met (trước khi ký G12)

- [ ] **C0 Release Gate** — demo payment và ba owner đã rõ; cost/state owner, cost export và release candidate cloud chưa khóa
- [ ] **Wave 2 staging** — FlashSale preview deploy failed; chưa có health và payment E2E trên cloud
- [ ] **Wave 3 engineering** — chưa có restore/rollback drill và monitoring/alerting được kiểm chứng trên candidate
- [ ] **Secrets configured** — tên GitHub Secrets đã có; chưa xác minh giá trị, scope và rotation sau khi lộ
- [x] **Rollback drill documented** — thủ tục rollback đã ghi ở `docs/deployment/release-approval-package.md` §4
- [ ] **UAT passed** — chưa có UAT thực tế trên candidate (G11)
- [ ] **Security review signed off** — người giữ vai trò đã được nêu; chưa có review evidence (G11)
- [ ] **Compliance sign-off** — người giữ vai trò đã được nêu; chưa có review evidence (G11)

---

## 4. Go/No-Go decision (G12)

### Quyết định: GO cho demo portfolio; HOLD cho production

**Lý do:** Mai Nguyễn Bình Tân chấp nhận phạm vi trình diễn portfolio. Ba vai trò Release Owner, Security Owner và System Owner đã được gán. SHA `a8e6f9d13fe92642b146b845731496956afc6348` là mốc local của tài liệu này; chưa phải production release candidate đã triển khai và kiểm chứng.

**Rủi ro chấp nhận được:** Các gate tại phần 3 còn mở chỉ được chấp nhận cho phạm vi demo portfolio, không được ghi là PASS hoặc dùng để quảng bá production readiness.

**Điều kiện phải hoàn tất trước khi production live:** Thu hồi và tạo lại các credential/deploy hook đã lộ; xác minh giá trị, scope và rotation của GitHub Secrets; cấu hình database, Redis, RabbitMQ và OIDC được phê duyệt trên Render; chạy health, payment/idempotency, UAT, security review, restore/rollback và monitoring/alert drill trên cùng release SHA; owner ký quyết định production riêng sau khi mọi gate bắt buộc PASS.

---

## 5. Owner acknowledgement — portfolio demo scope only

| Người ký | Vai trò | Xác nhận phạm vi demo / Email | Ngày |
|----------|---------|------------------------|------|
| Mai Nguyễn Bình Tân | Release Owner | tanmainguyenbinh@gmail.com | 6/11/2026 |
| Mai Nguyễn Bình Tân | Security Owner | tanmainguyenbinh@gmail.com | 6/11/2026 |
| Mai Nguyễn Bình Tân | System Owner | tanmainguyenbinh@gmail.com | 6/11/2026 |

---

## 6. Ghi chú cho các giai đoạn tiếp theo (khi có credentials)

1. Chỉ thêm Azure credentials nếu workflow FlashSale thực sự dùng Azure; không thêm quyền ngoài nhu cầu triển khai.
2. Thu hồi và tạo lại các credential/deploy hook đã lộ; cập nhật GitHub/Render/Cloudflare trực tiếp trong dashboard. Token Cloudflare đã bị lộ phải hủy bỏ và tạo lại trước khi dùng.
3. Xác minh các biến môi trường Render cho database, Redis, RabbitMQ và OIDC bằng giá trị riêng của preview/production.
4. Deploy Render preview theo `render.yaml`; kiểm chứng health, payment/idempotency và dependencies trên cùng release SHA.
5. Cấu hình custom domain + TLS qua Cloudflare sau khi token hợp lệ được tạo lại.
6. Chạy backup/restore và rollback drill thực tế ở staging, ghi RTO/RPO cùng log.
7. Named owners ký G11/G12 cho đúng release SHA và phạm vi demo payment.
8. Deploy production có kiểm soát; chạy post-deploy smoke và lưu timestamped logs, SHA, digest.
9. Chỉ công bố production live khi mọi gate bắt buộc PASS và owner xác nhận lại.

---
