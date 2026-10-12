# 🚀 BankOps — Hướng dẫn Deploy Cloudflare (Zero Cold Start)

> **Mục tiêu**: Nhân viên thu chi mở app lúc **bất kỳ giờ nào** đều dùng được ngay, không chờ server khởi động.

## Tổng quan kiến trúc

```
[Nhân viên] → [Cloudflare Pages - Flutter Web] → [Cloudflare Workers API] → [Neon PostgreSQL]
                 CDN toàn cầu, instant             V8 isolate, < 5ms           Serverless pool
                 (không cold start)                (không cold start)           (HTTP transport)
```

## Yêu cầu

- [ ] Tài khoản [Cloudflare](https://dash.cloudflare.com) (free)
- [ ] Tài khoản [Neon](https://console.neon.tech) (free)
- [ ] Tài khoản [GitHub](https://github.com) (để CI/CD)
- [ ] Node.js 20+, Flutter 3.24+, Python 3.11+, Poetry

---

## BƯỚC 1 — Tạo Neon PostgreSQL (3 phút)

1. Vào https://console.neon.tech → **New Project** → đặt tên `bankops`
2. Chọn region: **Asia Pacific (Singapore)** hoặc gần nhất
3. Sau khi tạo, copy **Connection string (Pooled)**:
   ```
   postgresql://user:password@ep-xxx-xxx.ap-southeast-1.aws.neon.tech/bankops?sslmode=require
   ```
   > ⚠️ Dùng **pooled** endpoint (có `-pooler` trong URL), không phải direct endpoint

---

## BƯỚC 2 — Chạy Migrations & Seed

```bash
cd backend

# Cài dependencies
poetry install

# Đặt DATABASE_URL
export DATABASE_URL="postgresql+asyncpg://user:password@ep-xxx.ap-southeast-1.aws.neon.tech/bankops?sslmode=require"

# Chạy migrations
poetry run alembic upgrade head

# Seed demo data
poetry run python -c "from app.seed import seed_demo; import asyncio; asyncio.run(seed_demo())"
```

---

## BƯỚC 3 — Setup Cloudflare Workers API (10 phút)

### 3a. Đăng nhập Cloudflare

```bash
cd infra/cloudflare-worker
npm install  # cài dependencies
npx wrangler login  # mở browser đăng nhập
```

### 3b. Tạo KV namespace

```bash
npx wrangler kv namespace create BANKOPS_KV
# Copy ID được in ra, ví dụ: abc123...
```

Cập nhật `infra/cloudflare-worker/wrangler.toml`:
```toml
[[kv_namespaces]]
binding = "BANKOPS_KV"
id = "abc123..."  # thay PLACEHOLDER_KV_ID bằng ID thực
```

### 3c. Đặt secrets

```bash
# DATABASE_URL từ Neon (dùng pooled endpoint, không cần đổi sang asyncpg)
echo "postgresql://user:password@ep-xxx.ap-southeast-1.aws.neon.tech/bankops?sslmode=require" \
  | npx wrangler secret put DATABASE_URL

# JWT secret (tạo ngẫu nhiên)
openssl rand -hex 32 | npx wrangler secret put JWT_SECRET

# Session secret
openssl rand -hex 32 | npx wrangler secret put SESSION_SECRET
```

### 3d. Deploy Worker

```bash
npx wrangler deploy
# Output: https://bankops-api.<account-name>.workers.dev
```

### 3e. Test

```bash
curl https://bankops-api.<account-name>.workers.dev/api/v1/health/live
# {"status":"alive","runtime":"cloudflare-workers",...}

curl -X POST https://bankops-api.<account-name>.workers.dev/api/v1/auth/login \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "username=admin&password=demo123"
# {"access_token":"...","user":{...}}
```

---

## BƯỚC 4 — Build & Deploy Flutter Web (15 phút)

### 4a. Build

```bash
cd frontend
flutter pub get

# Thay <WORKER_URL> bằng URL Worker ở bước 3d
flutter build web \
  --release \
  --web-renderer canvaskit \
  --dart-define=API_BASE_URL=https://bankops-api.<account-name>.workers.dev

# Output: frontend/build/web/
```

### 4b. Deploy lên Cloudflare Pages

```bash
cd infra/cloudflare-worker  # wrangler đang ở đây
npx wrangler pages project create bankops-web  # lần đầu

npx wrangler pages deploy ../frontend/build/web \
  --project-name=bankops-web
# URL: https://bankops-web.pages.dev
```

### 4c. Copy _headers và _redirects vào build

```bash
cp infra/cloudflare-pages/_headers frontend/build/web/
cp infra/cloudflare-pages/_redirects frontend/build/web/
# Redeploy nếu cần
```

---

## BƯỚC 5 — Cấu hình CI/CD GitHub Actions

Vào **GitHub → Settings → Secrets and variables → Actions**, thêm:

| Secret | Giá trị |
|--------|---------|
| `CLOUDFLARE_API_TOKEN` | Tạo tại https://dash.cloudflare.com/profile/api-tokens (template: "Edit Cloudflare Workers") |
| `CLOUDFLARE_ACCOUNT_ID` | Xem ở sidebar Cloudflare dashboard |
| `API_BASE_URL` | `https://bankops-api.<account>.workers.dev` |

Từ nay, mỗi khi push lên `main`:
- `backend/**` thay đổi → không deploy (Worker không cần Python)
- `infra/cloudflare-worker/**` thay đổi → tự deploy Worker
- `frontend/**` thay đổi → tự build Flutter + deploy Pages

---

## BƯỚC 6 — Custom Domain (tùy chọn)

**Worker:**
```bash
# Thêm vào wrangler.toml:
routes = [{ pattern = "api.bankops.example.com/*", zone_name = "example.com" }]
```

**Pages:**
Vào Cloudflare Dashboard → Pages → bankops-web → Custom domains → Add domain

---

## Kiểm tra Zero Cold Start

```bash
# Test nhiều lần liên tiếp - thời gian phải đồng đều
for i in 1 2 3 4 5; do
  time curl -s https://bankops-api.<account>.workers.dev/api/v1/health/live -o /dev/null
done

# Kết quả mong đợi: ~50-200ms mỗi request, không có spike lớn
```

---

## Theo dõi & Monitoring

- **Cloudflare Analytics**: https://dash.cloudflare.com → Workers → bankops-api → Analytics
- **Logs thời gian thực**: `npx wrangler tail` (từ terminal local)
- **Neon Monitoring**: https://console.neon.tech → Monitoring

---

## Troubleshooting

| Vấn đề | Giải pháp |
|--------|-----------|
| `DATABASE_URL not set` | Chạy lại `wrangler secret put DATABASE_URL` |
| `KV namespace not found` | Cập nhật ID trong `wrangler.toml` |
| Flutter app không gọi được API | Kiểm tra `API_BASE_URL` trong build args |
| CORS error | CORS đã cấu hình `*` cho demo; nếu production thêm domain vào `cors()` trong `index.ts` |
| `bcrypt verify failed` | Password hash từ FastAPI cũ vẫn dùng được vì `bcryptjs` tương thích |

---

## Chi phí (Free tier)

| Service | Free limit | BankOps dùng |
|---------|-----------|--------------|
| CF Workers | 100,000 req/ngày | ~1,000 req/ngày (team nhỏ) ✅ |
| CF Pages | Unlimited bandwidth | ✅ |
| CF KV | 100,000 reads/ngày | < 1,000 ✅ |
| Neon PostgreSQL | 0.5 GB, 10 branches | ~50 MB ✅ |

**Tổng chi phí: \$0/tháng** 🎉
