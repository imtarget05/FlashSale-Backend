# T10 — Performance & security release test — FlashSale payment-service

## 1. Mục tiêu

Chứng minh dịch vụ payment-service có thể chịu tảiFlash Sale, phát hiện lỗ hổng bảo mật, và có quy trình phát hành có thể lặp lại (reproducible release) trên cùng SHA/image digest.

## 2. Performance test — k6 load test

### 2.1 Mục tiêu tải

| Chỉ số | Giá trị mục tiêu | Ghi chú |
|--------|----------------|---------|
| Concurrent users | 100 VUs | Mô phỏng flash sale bursts |
| Duration | 5 minutes | Soak test ngắn |
| Target RPS | 200 req/s | POST /api/payments |
| Error rate | < 1% | Không double-charge, không 5xx do lỗi hệ thống |
| p95 latency | < 500ms | Cho POST /api/payments |

### 2.2 Cách chạy (k6)

```bash
# Cài k6: https://k6.io/docs/getting-started/install/
k6 run load-tests/flash-sale-payment.js
```

File k6 mẫu (`load-tests/flash-sale-payment.js`):

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 100,
  duration: '5m',
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const payload = JSON.stringify({
    paymentId: String(Date.now()),
    orderId: Math.floor(Math.random() * 10000),
    idempotencyKey: `idem-${__VU}-${__ITER}`,
    amount: '10.01',
    currency: 'VND',
  });
  const res = http.post('http://localhost:8080/api/payments', payload, {
    headers: { 'Content-Type': 'application/json' },
  });
  check(res, {
    'status is 200 or 201': (r) => r.status === 200 || r.status === 201,
    'body has transactionId': (r) => r.json('transactionId') !== '',
  });
  sleep(0.1);
}
```

### 2.3 Kết quả (local lab)

Chưa có số liệu thực tế từ k6 vì môi trường này không có Docker để chạy service đầy đủ. Số liệu phải thu từ chạy thực tế trên local với Docker Compose.

**Đánh giá**: `NOT_YET` — k6 script đã chuẩn bị, chưa chạy thu được số liệu thực.

## 3. Security test

### 3.1 Dependency scan (Trivy)

```bash
trivy fs --exit-code 1 --severity HIGH,CRITICAL .
trivy image --exit-code 1 --severity HIGH,CRITICAL flashsale/payment-service:local
```

**Đánh giá**: `DEMONSTRATED` — Trivy HIGH/CRITICAL gate đã chạy trên hosted CI (`dfaf73a`).

### 3.2 OWASP API review

| Control | Trạng thái | Ghi chú |
|---------|-----------|---------|
| Input validation | ✅ | `@Valid`, `@NotBlank`, `@Size`, `@DecimalMin` |
| Authentication | ✅ | JWT HS256, `JwtStartupValidator` fail closed |
| Authorization | ✅ | `@PreAuthorize`, role-based |
| Security headers | ✅ | `ProductionSecurityConfig` (profile production) |
| CORS | ✅ | Giới hạn origins, không wildcard |
| Rate limiting | ⚠️ | Cấu hình sẵn (`app.security.rate-limit-enabled`), chưa có limiter runtime |
| Secret management | ✅ | Env var, không hard-code |

**Đánh giá**: `PARTIAL` — phần lớn control đã có, nhưng rate limiting runtime và OWASP scan report chưa rõ ràng.

### 3.3 Negative auth tests

| Test | Kết quả |
|------|---------|
| Malformed JWT | 403 (forbidden) |
| Missing Authorization header | 403 (forbidden) |
| Anonymous access to /api/payments | 403 (forbidden) |

**Đánh giá**: `DEMONSTRATED` — unit tests đã sửa để khớp với 403 fail-closed behavior của Spring Security.

## 4. Container scan

```bash
docker build -t flashsale/payment-service:local .
trivy image --exit-code 1 --severity HIGH,CRITICAL flashsale/payment-service:local
```

**Đánh giá**: `DEMONSTRATED` — Docker build và Trivy scan đã chạy trên hosted CI.

## 5. IaC scan

Terraform modules trong `infrastructure/terraform/` được validate local (`terraform validate`, `terraform fmt -check`).

**Đánh giá**: `LAB_ONLY` — không có `terraform apply` lên Azure.

## 6. Release pipeline

| Bước | Trạng thái | Ghi chú |
|------|-----------|---------|
| Build (mvn package) | ✅ | Local build pass |
| Unit tests | ✅ | 39/39 pass |
| Docker build | ✅ | Multi-stage, temurin JRE |
| Trivy scan | ✅ | HIGH/CRITICAL gate |
| Deploy to Render | ❌ | Hook HTTP 404, cần cấu hình secret |
| Deploy to AKS | ❌ | Cluster destroyed, không tạo tài nguyên mới |

**Đánh giá**: `PARTIAL` — build/test/scan xanh, deploy bị chặn bởi môi trường.

## 7. Rollback drill

Rollback target: quay lại image SHA trước đó. Chưa chạy drill thực tế vì không có live deployment.

**Đánh giá**: `NOT_YET` — cần live deployment trước khi có thể drill rollback.
