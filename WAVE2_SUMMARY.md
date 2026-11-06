# Wave 2 Summary — FlashSale-Backend (Java payment service, Render preview/staging)

> **Trạng thái chung:** WAVE 2 KHÔNG hoàn thành đầy đủ trên cloud (BLOCKED / PARTIAL).
> Không có bằng chứng deploy/timestamp thực tế từ dịch vụ Render đang chạy. Dưới đây là ghi nhận trung thực
> về điều đã kiểm tra, điều đã sẵn sàng, và điều đang chặn cùng nguyên nhân. Không ghi tạo số liệu, credentials,
> hoặc timestamp bằng giả định.

## 1. Thời gian Wave 2
- Bắt đầu: 2026-11-06T10:32:02Z (thời gian nhiệm vụ giao)
- Kết thúc: 2026-11-06T10:40:00Z (ghi nhận tóm tắt này)

## 2. Commit SHA
- HEAD tại thời điểm bắt đầu Wave 2: `332859c5456bb53ef639d7bd6d02535f7e44933e`
  (thông điệp: `docs(evidence): add FlashSale F04 offline smoke evidence`)
- **SHA dùng để deploy:** KHÔNG có — chưa có deploy nào được thực hiện lên Render, do đó không có release SHA.
- Commit mới nhất trên `origin/main` đã được push (`git push origin main` → Everything up-to-date).

## 3. File đã chỉnh sửa (Wave 2)
| File | Thay đổi |
|------|----------|
| (không có) | Không file mới nào được tạo/sửa trong Wave 2. |

- `render.yaml` đã ở trạng thái đúng yêu cầu Wave 2 (đã được chuẩn bị ở Wave 1 và nằm trong HEAD hiện tại): 2 dịch vụ
  `flashsale-payment-api-preview` (free, branch `preview`) và `flashsale-payment-api-prod` (starter, branch `main`);
  tất cả secret envVars (`SPRING_DATASOURCE_URL/USERNAME/PASSWORD`, `SPRING_DATA_REDIS_HOST`,
  `SPRING_RABBITMQ_HOST/USERNAME/PASSWORD`, `JWT_SIGNING_KEY`, `OIDC_*`) đều có `sync: false`; không hardcode giá
  trị secret; `SPRING_PROFILES_ACTIVE=production`, `SERVER_PORT="8080"`.
- `.env` (root) và `.env` (src/payment-service-java) đều trong `.gitignore` — không commit credentials.

## 4. Kiểm tra workspace Render (CLI đã xác thực)
- `render whoami`: OK — người dùng `Tan Mai Nguyen Binh` (`usr-da292c3m8hqs73bq14g0`).
- Workspace Render **có** dịch vụ FlashSale (lỗi 404 deploy hook từ PROJECT_STATUS.md đã được khắc phục — dịch vụ tồn tại,
  không bị suspend):

| Dịch vụ | Loại | ID | Region | Plan | Nhánh kết nối | URL | Trạng thái |
|---------|------|----|--------|------|---------------|-----|------------|
| flashsale-payment-db-preview | Postgres | dpg-dbmpom6i0phs73chbndg-a | oregon | (Render managed) | — | — | active |
| flashsale-payment-redis-preview | Key Value (Redis) | red-dbmponajnfac73b5hg70 | oregon | (Render managed) | — | — | active |
| flashsale-payment-api-preview | Web Service (docker) | srv-dbmoohrm8hqs73b20jd0 | oregon | free | `main` | https://flashsale-payment-api-preview.onrender.com | not_suspended |
| flashsale-payment-rabbitmq-preview | **CHƯA CÓ** | — | — | — | — | — | **chưa cung cấp** |

- Lưu ý quan trọng: `render.yaml` khai báo `branch: preview` cho dịch vụ preview, nhưng dịch vụ Render thực tế đang kết
  nối nhánh `main`. Cần hành động trên Render dashboard/CLI để chuyển nhánh sang `preview` (hoặc sửa `render.yaml` cho
  khớp) trước khi deploy qua blueprint.
- **Chưa có dịch vụ RabbitMQ preview** — cần tạo qua Render dashboard/CLI trước khi có thể chạy luồng cần broker.

## 5. Môi trường infra trên Render (thông tin công khai, không credentials)
- PostgreSQL: `flashsale-payment-db-preview`, region oregon, Render managed Postgres. Host/mật khẩu chỉ set qua Render
  dashboard (sync: false) — không ghi vào file/tin nhắn.
- Redis: `flashsale-payment-redis-preview`, region oregon, Render Key Value. Host chỉ qua Render dashboard.
- RabbitMQ: **chưa có dịch vụ preview** — cần tạo (Render managed RabbitMQ) trước khi deploy chạy luồng payment.
- Secret còn thiếu trên Render dashboard: `SPRING_DATASOURCE_URL/USERNAME/PASSWORD`, `SPRING_DATA_REDIS_HOST`,
  `SPRING_RABBITMQ_HOST/USERNAME/PASSWORD`, `JWT_SIGNING_KEY` — phải nhập thủ công trên Render dashboard (không hardcode,
  không có giá trị thực trong môi trường này nên không thể nhập thay).

## 6. Xác thực healthcheck trên dịch vụ preview đang chạy
Thực hiện curl trực tiếp đến `https://flashsale-payment-api-preview.onrender.com` (--max-time 30s):

| Endpoint | HTTP status | Kết quả | Ghi chú |
|----------|-------------|---------|---------|
| GET /health/live | 000 (connection timeout) | **Không đạt** | Dịch vụ không phản hồi — chưa có bản build/container đang chạy reachable, hoặc env vars chưa đủ để container khởi động. |
| GET /actuator/health/readiness | 000 (connection timeout) | **Không đạt** | Cùng nguyên nhân ở trên. |

- `render.yaml` khai báo `healthCheckPath: /health/live` ✓ (đúng với endpoint kiểm tra).
- Do không có giá trị secret thực để cấu hình Render env vars và chưa có dịch vụ RabbitMQ, container dịch vụ không thể
  khởi động đủ điều kiện → cả hai probe đều timeout (HTTP 000). **Không có bản deploy thành công, không có SHA release.**

## 7. Luồng payment / idempotency trên cloud preview
**Không thực hiện được** (BLOCKED). Lý do:

1. **POST /api/auth/login** → bị chặn: với `SPRING_PROFILES_ACTIVE=production`, `SecurityConfig.java:52` thiết lập
   `auth.requestMatchers("/api/auth/**").denyAll()`, và `DemoUserDetailsConfig.java:37-41` trả về
   `"No identity provider is configured"` (chỉ có demo user trong profile `demo & !production & !prod`). Do đó không thể
   nhận JWT trên preview chạy profile `production` — luồng login→payment không thể chạy trên cloud preview theo cấu hình
   hiện tại. Đây là thiết kế fail-closed của production profile, không phải lỗi cấu hình Render.
2. **POST /api/payments** → yêu cầu vai trò STAFF/ADMIN và JWT hợp lệ, phụ thuộc bước 1.
3. Cần DB + Redis + RabbitMQ đều kết nối tốt mới pass readiness; hiện thiếu RabbitMQ service và secret DB/Rabbit.

Kết quả: tất cả 6 bước (login, process payment 200, same-key replay 200, conflicting replay 409, decline 402/400,
timeout) **chưa có timestamp/request/response thực tế** trên cloud. Lưu ý: luồng này đã được xác thực thành công cục bộ ở
Wave 1 qua `scripts/smoke-local.sh` (PASS, 6/6 bước) với profile `demo` — nhưng đó là local, không phải bằng chứng cloud.

| Bước | Input | Output (mong đợi) | Status | Timestamp | Ghi chú |
|------|-------|-------------------|--------|-----------|---------|
| 1. Login | POST /api/auth/login | JWT | Chưa chạy (blocked) | — | /api/auth/** denyAll ở profile production |
| 2. Process payment | POST /api/payments + idempotency key | HTTP 200 | Chưa chạy | — | Cần JWT + DB |
| 3. Same-key replay | POST /api/payments cùng key | HTTP 200 (idempotent) | Chưa chạy | — | |
| 4. Conflicting replay | POST /api/payments cùng key, payload khác | HTTP 409 | Chưa chạy | — | IdempotencyConflictException |
| 5. Decline | amount .02 | HTTP 402/400 | Chưa chạy | — | |
| 6. Timeout | amount .03 | HTTP 504 | Chưa chạy | — | PaymentTransientException |

## 8. Kiểm tra bảo mật
- Không credentials hardcode trong `render.yaml` hay bất kỳ file tracked nào (`sync: false` cho tất cả secret).
- `.env` root và module nằm trong `.gitignore`.
- Không có giá trị `SPRING_DATASOURCE_PASSWORD`, `SPRING_RABBITMQ_PASSWORD`, `JWT_SIGNING_KEY`, `DEMO_AUTH_PASSWORD`
  nào được commit (git grep chỉ tìm thấy tên biến tham chiếu, không giá trị thực).
- Không paste credentials vào file hoặc tin nhắn nào trong Verlauf này.

## 9. Lỗi gặp phải và cách xử lý
| Lỗi | Nguyên nhân | Cách xử lý / khuyến nghị |
|-----|-------------|---------------------------|
| HTTP 000 (timeout) cả /health/live và /actuator/health/readiness trên `https://flashsale-payment-api-preview.onrender.com` | Chưa có bản deploy active; container không khởi động do thiếu secret env vars (DB, Redis, RabbitMQ, JWT) và chưa có dịch vụ RabbitMQ | Vào Render dashboard, cung cấp giá trị secret (sync: false), tạo dịch vụ RabbitMQ preview, chuyển dịch vụ web sang nhánh `preview`, rồi trigger deploy/redeploy. |
| Dịch vụ Render web kết nối nhánh `main`, không phải `preview` | `render.yaml` khai báo `branch: preview` nhưng dịch vụ thực tế trên `main` | Đồng bộ nhánh trên Render dashboard/CLI với `render.yaml` (hoặc ngược lại) trước khi deploy qua blueprint. |
| Chưa có dịch vụ RabbitMQ preview | Chưa cung cấp trên Render | Tạo Render RabbitMQ (staging/disposable), lấy host/user/password, set env var `sync: false`. |
| Login + luồng payment không chạy trên profile `production` | `/api/auth/**` denyAll + không có identity provider ở profile production (fail-closed, by design) | Đối với preview/staging, xem xét chạy profile `demo` để có thể smoke-test luồng login→payment; giữ `production` cho prod. Đây là sựchênh lệch giữa yêu cầu task (`SPRING_PROFILES_ACTIVE=production`) và khả năng chạy luồng nghiệp vụ — cần quyết định với product/reviewer. Không được làm-giả bằng chứng. |
| Không có giá trị secret thực trong môi trường này | Credentials do người dùng/Radar dashboard quản lý | Không được bịa/mã hóa-hardcode. Người dùng tự nhập trên Render dashboard. |

## 10. Kết luận / Bước tiếp theo
- Wave 2 **chưa hoàn thành** trên cloud: không có deploy thành công, không có healthcheck 200, không có readiness 200,
  không có bằng chứng timestamped luồng payment/idempotency trên Render.
- Điều đã sẵn sàng: `render.yaml` đúngspec (tất cả secret `sync: false`, production profile, cổng 8080); dịch vụ Postgres +
  Redis preview đã tồn tại trên workspace Render; code Java build thành công (`mvn -DskipTests compile` → EXIT 0);
  git remote `origin/main` up-to-date.
- Điều cần làm để hoàn thành Wave 2:
  1. Trên Render dashboard: tạo RabbitMQ preview; nhập giá trị secret (DB/Redis/Rabbit/JWT_SIGNING_KEY) với `sync: false`.
  2. Đồng bộ nhánh dịch vụ web với `preview` (theo `render.yaml`).
  3. Trigger deploy, chờ bản build active, lấy SHA release thực tế.
  4. Xác thực `GET /health/live` → 200 và `GET /actuator/health/readiness` → 200.
  5. Nếu muốn chạy luồng payment/idempotency trên preview: bật profile `demo` (hoặc cấu hình OIDC issuer/audience hợp lệ)
     vì `production` chặn `/api/auth/login`; ghi timestamp, request/response, status cho 6 bước.
  6. Ghi nhận lại vào WAVE2_SUMMARY.md (SHA release, thời gian, bảng kết quả, lỗi nếu có).

> **Quan trọng:** Repo KHÔNG production-ready (xem RECRUITER-EVIDENCE.md). Wave 2 là cloud preview/staging. Không claim
> production readiness. Không có real PSP, không có production database shape.
