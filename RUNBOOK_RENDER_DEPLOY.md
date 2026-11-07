# Runbook: Deploy FlashSale Payment API lên Render Preview

## Tổng quan
Service: `flashsale-payment-api-preview` (ID: `srv-dbmoohrm8hqs73b20jd0`)
URL: https://flashsale-payment-api-preview.onrender.com
Region: oregon, Plan: free (starter)
Branch: preview

## Các tài nguyên Render đã cung cấp

| Tài nguyên | Tên | ID | Host |
|---|---|---|---|
| PostgreSQL | flashsale-payment-db-preview | dpg-dbmpom6i0phs73chbndg-a | dpg-dbmpom6i0phs73chbndg-a.oregon-postgres.render.com |
| Redis (Key Value) | flashsale-payment-redis-preview | red-dbmponajnfac73b5hg70 | red-dbmponajnfac73b5hg70.oregon-redis.render.com |
| Web Service | flashsale-payment-api-preview | srv-dbmoohrm8hqs73b20jd0 | https://flashsale-payment-api-preview.onrender.com |

**RabbitMQ**: Chưa có dịch vụ preview trên Render. Cần tạo qua Dashboard.

## Bước 1: Lấy PostgreSQL password từ Render Dashboard

Render không expose PostgreSQL password qua API (bảo mật). Bạn phải lấy từ Dashboard:

1. Truy cập: https://dashboard.render.com/d/dpg-dbmpom6i0phs73chbndg-a
2. Tab **Configuration** → Tìm `INTERNAL_DATABASE_URL` hoặc `DATABASE_URL`
3. Copy toàn bộ connection string (chứa password thực)
4. Hoặc tab **Connect** → Copy password từ connection string

Format connection string:
```
postgresql://flashsale_payment_db_preview_user:<PASSWORD>@dpg-dbmpom6i0phs73chbndg-a.oregon-postgres.render.com:5432/flashsale_payment_db_preview
```

## Bước 2: Cập nhật Environment Variables trên Render

Truy cập: https://dashboard.render.com/web/srv-dbmoohrm8hqs73b20jd0 → **Environment**

Thiết lập các biến sau (không hardcode, nhập bằng tay):

| Biến | Giá trị | Ghi chú |
|---|---|---|
| `SPRING_DATASOURCE_URL` | `jdbc:postgresql://dpg-dbmpom6i0phs73chbndg-a.oregon-postgres.render.com:5432/flashsale_payment_db_preview` | Sửa DB name từ `flashsale` → `flashsale_payment_db_preview` |
| `SPRING_DATASOURCE_USERNAME` | `flashsale_payment_db_preview_user` | Sửa từ `flashsale` |
| `SPRING_DATASOURCE_PASSWORD` | `<LẤY TỪ DASHBOARD>` | **Bắt buộc** - password từ Bước 1 |
| `SPRING_DATA_REDIS_HOST` | `red-dbmponajnfac73b5hg70.oregon-redis.render.com` | ✅ Đã set |
| `SPRING_DATA_REDIS_PORT` | `6379` | ✅ Đã set |
| `SPRING_RABBITMQ_HOST` | `<CẦN TẠO RABBITMQ TRÊN RENDER>` | Chưa có dịch vụ RabbitMQ preview |
| `SPRING_RABBITMQ_PORT` | `5672` | ✅ Đã set |
| `SPRING_RABBITMQ_USERNAME` | `guest` | ✅ Đã set |
| `SPRING_RABBITMQ_PASSWORD` | `<CẦN LẤY TỪ DASHBOARD>` | Cần tạo RabbitMQ trước |
| `JWT_SIGNING_KEY` | `JWT_KEY_PLACEHOLDER_CHANGE_ME` | ⚠️ Cần thay bằng giá trị thực (openssl rand -base64 48) |
| `OIDC_ISSUER_URI` | `https://flashsale-keycloak.onrender.com/realms/flashsale` | ✅ Đã set (placeholder) |
| `OIDC_AUDIENCE` | `payment-service` | ✅ Đã set |
| `OIDC_AUTHORITIES_CLAIM` | `roles` | ✅ Đã set |

## Bước 3: Tạo RabbitMQ trên Render (nếu cần test toàn bộ luồng)

1. Truy cập Render Dashboard → **Create** → **Redis** (hoặc tìm RabbitMQ add-on)
2. Nếu Render không có RabbitMQ managed, có thể:
   - Sử dụng RabbitMQ Cloud (cloudamqp.com) miễn phí tier
   - Hoặc bỏ qua RabbitMQ cho bước smoke test đầu tiên (service vẫn start được, chỉ không gửi outbox event)

## Bước 4: Trigger Deploy

Sau khi set xong env vars:

1. Trong Render Dashboard, vào service `flashsale-payment-api-preview`
2. Tab **Deploys** → Click **Manual Deploy** → **Deploy latest commit**
3. Hoặc gọi API:
   ```bash
   curl -s -X POST 'https://api.render.com/v1/services/srv-dbmoohrm8hqs73b20jd0/deploys' \
     -H "Authorization: Bearer $RENDER_API_KEY" \
     -H 'Content-Type: application/json' -d '{}'
   ```

## Bước 5: Xác minh Deploy

Chờ build xong (2-3 phút), sau đó kiểm tra:

```bash
# Health check
curl https://flashsale-payment-api-preview.onrender.com/health/live
# Mong đợi: {"status":"healthy"}

# Readiness check
curl https://flashsale-payment-api-preview.onrender.com/actuator/health/readiness
# Mong đợi: {"status":"UP"}
```

## Bước 6: Test luồng thanh toán (nếu có OIDC/JWT)

Nếu đã cấu hình OIDC và JWT:

```bash
# Login để lấy JWT token
curl -X POST https://flashsale-payment-api-preview.onrender.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"test","password":"<DEMO_AUTH_PASSWORD>"}'

# Xử lý thanh toán (cần JWT trong header)
curl -X POST https://flashsale-payment-api-preview.onrender.com/api/payments \
  -H 'Authorization: Bearer <JWT_TOKEN>' \
  -H 'Content-Type: application/json' \
  -d '{"idempotencyKey":"test-001","paymentId":"pay-001","orderId":"ord-001","amount":10.01,"currency":"USD"}'
```

## Bước 7: Cập nhật WAVE2_SUMMARY.md

Sau khi deploy thành công, ghi nhận:
- Release SHA từ Render deploy
- Thời gian deploy
- Kết quả health check (200/404/500)
- Kết quả test các bước idempotency (6 bước trong WAVE1_SUMMARY.md)
- Lỗi nếu có

## Lưu ý bảo mật

- **Không commit** giá trị thực của `SPRING_DATASOURCE_PASSWORD`, `JWT_SIGNING_KEY`, `SPRING_RABBITMQ_PASSWORD` vào repo
- Tất cả secret phải được nhập trực tiếp trên Render Dashboard
- `render.yaml` đã cấu hình `sync: false` cho tất cả secret env vars
- `.env` files nằm trong `.gitignore`

## Nếu deploy vẫn thất bại

Kiểm tra logs:
```bash
render logs --resources srv-dbmoohrm8hqs73b20jd0 --limit 50 --output text
```

Lỗi phổ biến:
1. `password authentication failed` → SPRING_DATASOURCE_PASSWORD sai
2. `UnknownHostException: postgres` → SPRING_DATASOURCE_URL sai host/port
3. `Connection refused` → Redis/RabbitMQ chưa sẵn sàng