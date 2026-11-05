# Payment Service Java — Cloud Production Deployment Architecture

**Tác giả:** Kilo (planner) · **Ngày:** 2026-11-05 · **Trạng thái:** Kiến trúc đề xuất, chưa thực thi  
**Mục tiêu:** Triển khai FlashSale Java Payment Service lên cloud production theo mô hình 3 lớp (Cloudflare / Render / Azure), tương tự CreditFlow & AKS-SRE-Platform, tuân thủ ràng buộc `no_azure_production_mutations` và `no_go_live_while_p0_gates_open`.

---

## 1. Mục tiêu deploy

| Mục tiêu | Chi tiết |
|----------|----------|
| Primary | Render (application runtime cho Java Spring Boot service) |
| Edge | Cloudflare Pages/Workers (TLS, WAF, caching, edge access control) |
| Fallback / Control plane | Azure App Service / Container Apps (chỉ khi có owner sign-off) |
| Không phải | Terraform apply AKS từ local (bị chặn bởi `no_azure_production_mutations`) |

---

## 2. Luồng traffic đề xuất

```
Internet
   │
   ▼
Cloudflare Pages / Workers (Edge)
   - TLS termination, WAF, caching, rate limiting
   - CNAME → Render service
   │
   ▼
Render (Java Spring Boot Payment Service)
   - Docker container, health probes, OTEL config
   - POST /api/payments, GET /api/payments/{id}, GET /api/payments
   - Environment variables từ Render dashboard (không hardcode)
   │
   ▼
PostgreSQL 15 (Render managed DB hoặc Azure Database for PostgreSQL)
   - Idempotency key PK, version column (optimistic locking)
   │
   ▼
Redis 7 (Render managed Redis hoặc external)
   - Cache Aside, TTL 300s, invalidation sau thanh toán
   │
   ▼
RabbitMQ 3 + Kafka 3 (Render managed hoặc external)
   - Order events, payment events, retry, DLQ
```

---

## 3. Cấu hình chi tiết từng target

### 3.1 Cloudflare Pages/Workers (Edge)

| Cấu hình | Giá trị |
|----------|---------|
| Target | `apps/sre-console` (nếu có frontend) hoặc Worker cho API edge |
| Config | `apps/sre-console/wrangler.toml` (nếu có) |
| Chức năng | TLS, WAF, caching, rate limiting (RATE_ORDER 100/10s, v.v.), edge access control |
| Trạng thái | Chưa deploy, cần `CLOUDFLARE_API_TOKEN` |

### 3.2 Render (Application Runtime)

| Cấu hình | Giá trị |
|----------|---------|
| Target | `app/sre-demo-api` (Java Spring Boot) |
| Config | `render.yaml` (tại root repo CreditFlow hoặc FlashSale) |
| Runtime | Dockerized, health probes `/health`, `/health/live`, `/healthz` |
| OTEL | Config theo AKS-SRE-Platform |
| Trạng thái | Chưa deploy, cần `RENDER_API_KEY` + connect GitHub repo |

**render.yaml mẫu cho Java Payment Service:**
```yaml
services:
  - type: web
    name: flashsale-payment-java
    env: docker
    repo: https://github.com/imtarget05/FlashSale-Backend.git
    rootDir: src/payment-service-java
    dockerContext: .
    dockerfile: Dockerfile
    region: oregon
    plan: starter
    envVars:
      - key: SPRING_DATASOURCE_URL
        fromDatabase:
          name: flashsale-postgres
          property: connectionString
      - key: JWT_SIGNING_KEY
        generateValue: true
      - key: SPRING_DATA_REDIS_HOST
        value: flashsale-redis
      - key: SPRING_DATA_REDIS_PORT
        value: "6379"
      - key: SPRING_RABBITMQ_HOST
        value: flashsale-rabbitmq
      - key: SPRING_KAFKA_BOOTSTRAP_SERVERS
        value: flashsale-kafka:9092
    healthCheckPath: /healthz
    autoDeploy: false
```

### 3.3 Azure (Fallback / Control Plane)

| Cấu hình | Giá trị |
|----------|---------|
| Target | Azure App Service / Container Apps (fallback) |
| Config | `azure.yaml` (tại root repo) |
| Chức năng | Managed control plane, identity, observability |
| Ràng buộc | **Không mutate Azure từ local** (`no_azure_production_mutations`) |
| Trạng thái | Chưa deploy, cần infra team mở quyền tenant/cluster/Private Link |

---

## 4. Điều kiện trước khi deploy production

| Điều kiện | Trạng thái |
|-----------|-----------|
| Environment-specific secrets (không hardcode trong repo) | ⏳ Chờ cấu hình GitHub Secrets/Render env vars |
| Image digest thực tế (không placeholder `@sha256:0000...00000`) | ⏳ Chờ build pipeline |
| SBOM + Trivy scan + ký + publish trước khi triển khai prod | ⏳ Chờ CI pipeline |
| Owner sign-off cho từng workload | ⏳ Chờ infra/business owner |
| CI/Sonar/security gate xanh hoàn toàn | ⏳ Chờ T3 (CI/Sonar verify) |
| Azure AKS live verify (nếu dùng Azure) | ❌ Bị chặn (`no_azure_production_mutations`) |

---

## 5. Ràng buộc cốt lõi (không negotiate)

| Ràng buộc | Nội dung | Ảnh hưởng |
|-----------|----------|-----------|
| `no_azure_production_mutations` | Không chạy `terraform apply` / mutate cloud từ local | Azure AKS để lại open item, cần infra team |
| `no_go_live_while_p0_gates_open` | 5 P0 gates mở → không claim production readiness | Không đánh dấu "production-ready" trong docs |
| Không nhận token qua chat | AI không lưu/sử dụng Render/Sonar/Cloudflare token | Chủ tài khoản tự thêm vào GitHub Secrets |

---

## 6. Lệnh deploy (chỉ mô tả, không thực thi)

### 6.1 Cloudflare Pages (chạy trên máy local sau khi có token)

```bash
cd /Users/mainguyenbinhtan/Downloads/FlashSale-Backend/src/payment-service-java
export CLOUDFLARE_API_TOKEN="<token của bạn>"
wrangler pages deploy dist/ --project-name=flashsale-payment-java
# hoặc nếu không có frontend, dùng Worker:
wrangler deploy --project-name=flashsale-payment-java
```

### 6.2 Render (qua Dashboard)

1. Vào Render Dashboard → Connect GitHub repo `imtarget05/FlashSale-Backend`
2. Chọn root directory `src/payment-service-java`
3. Render đọc `render.yaml` và deploy `app/sre-demo-api`
4. Thiết lập env var `JWT_SIGNING_KEY`, `SPRING_DATASOURCE_URL`, v.v.
5. Verify: `curl https://flashsale-payment-java.onrender.com/healthz` → HTTP 200

### 6.3 Azure (không thực thi từ local)

- Cần infra team mở quyền tenant/cluster/Private Link/VNet
- Chạy `terraform apply` từ môi trường trong VNet/bastion
- Verify ArgoCD sync từ bên trong cluster

---

## 7. Open items (chưa làm)

| # | Công việc | Mức độ | Trạng thái | Owner đề xuất | Ghi chú |
|---|-----------|--------|------------|---------------|---------|
| C1 | Deploy Cloudflare Pages (flashsale-payment-java) | P1 | Chưa làm | Chủ tài khoản | Cần `CLOUDFLARE_API_TOKEN` |
| C2 | Deploy Render (flashsale-payment-java) | P1 | Chưa làm | Chủ tài khoản | Cần `RENDER_API_KEY` + connect GitHub |
| C3 | Azure AKS live verify | P0 | Chặn | Infra team | Bị chặn bởi `no_azure_production_mutations` |
| C4 | Production go-live claim | P0 | Chặn | Business owner | 5 P0 gates mở |
| C5 | Image digest thực tế (không placeholder) | P1 | Chưa làm | CI pipeline | Cần SBOM + Trivy + ký + publish |

---

## 8. Validation plan (sau khi deploy)

1. `kubectl get nodes` thành công từ bên trong VNet (bastion/VM) trước khi verify ArgoCD sync (Azure fallback, chặn bởi `no_azure_production_mutations`)
2. `curl https://flashsale-payment-java.onrender.com/healthz` → HTTP 200
3. `curl https://flashsale-payment-java.onrender.com/health/live` → HTTP 200
4. `curl https://flashsale-payment-java.onrender.com/api/payments/{idempotencyKey}` → 401 (chưa auth) hoặc 200 (nếu có token)
5. Render Dashboard hiển thị service trạng thái `live`
6. Cloudflare Pages deployment URL truy cập được

---

## 9. Kết luận

FlashSale Java Payment Service có kiến trúc deploy cloud sẵn sàng (Dockerfile multi-stage, docker-compose, render.yaml, azure.yaml, cloudflare config). Tuy nhiên:
- **Chưa deploy production** (chỉ có config, chưa thực thi)
- **Không claim production go-live** (5 P0 gates mở, `no_azure_production_mutations`)
- **Chỉ có thể deploy lên Cloudflare + Render** sau khi có token/credential của chủ tài khoản
- **Azure AKS** để lại open item, cần infra team mở quyền tenant/Private Link/VNet

Lưu tại: `FlashSale-Backend/docs/deployment/payment-service-cloud-deploy.md`
