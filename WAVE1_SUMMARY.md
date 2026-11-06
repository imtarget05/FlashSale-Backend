# Wave 1 Summary — FlashSale-Backend (Java payment service, local only)

## Thoi gian
- Bat dau: 2026-11-06T09:34:19Z
- Ket thuc: 2026-11-06T09:50:39Z

## Commit SHA
- Hien tai: 0eb8f518a531ccdc86294e1a41236c54c531c325 (branch main, up-to-date with origin/main)

## File da chinh sua

| File | Thay doi |
|------|----------|
| .env (root) | Ghi de tu .env.example; bo sung JWT_SIGNING_KEY va DEMO_AUTH_PASSWORD da sinh ngau nhien (base64). File da co trong .gitignore, khong commit. |
| render.yaml | Them JWT_SIGNING_KEY voi sync: false vao ca 2 service block (flashsale-payment-api-preview va flashsale-payment-api-prod). Cac secret khac (SPRING_DATASOURCE_*, SPRING_DATA_REDIS_HOST, SPRING_RABBITMQ_*, OIDC_*) da co sync: false. Khong hardcode gia tri secret. |

## Ket qua kiem tra

### 2. Khoi dong stack local (docker compose)
- docker compose up --build -d: thanh cong. Tat ca container healthy: postgres, redis, rabbitmq, mongo, clickhouse, payment-service-java.
- Loi gap va xu ly:
  1. JWT_SIGNING_KEY bi thieu khi interpolate compose (.env tu .env.example de trong gia tri nay). Xu ly: sinh gia tri ngau nhien va ghi vao .env (gitignored).
  2. Payment service crash: Schema validation: missing table [payment_outbox]. Nguyen nhan: volume postgres-data cu da ton tai truoc khi schema payment_outbox duoc them vao init-schema.sql, nen entrypoint khong chay lai. Xu ly: docker compose down -v de xoa volume, roi khoi dong lai.
- curl http://localhost:8081/healthz -> HTTP 200 ({"status":"healthy"})
- curl http://localhost:8081/actuator/health/readiness -> HTTP 200 ({"status":"UP"})

### 3. Chay tests (Maven)
- Unit tests (mvn test -Dtest='!*Integration*'): 106 passed, 0 failed, 0 errors, 0 skipped — BUILD SUCCESS.
- Integration tests (mvn test, Testcontainers: Postgres/Redis/RabbitMQ/Mongo): 135 passed, 0 failed, 0 errors, 0 skipped — BUILD SUCCESS.
- Tong: 241 passed, 0 failed.

### 4. Smoke script local (scripts/smoke-local.sh)
- Ket qua: PASS — login; success; same-key replay (200, cung transactionId); conflicting replay (409); decline (200, Declined); timeout (504, TimedOut); confirmed-event Mongo audit projection (Succeeded/Declined/TimedOut).
- In loi: khong.

### 5. Build Docker image + smoke test container
- docker build -t flashsale-payment-api:local-test src/payment-service-java: thanh cong (multi-stage temurin JRE 17).
- Chay container doc lap tren portfolio-net, map 8082:8080: GET http://localhost:8082/healthz -> HTTP 200 ({"status":"healthy"}).
- Container test da don dep (docker rm -f).

### 6. render.yaml cho Render
- 2 dich vu: flashsale-payment-api-preview (free, branch preview) va flashsale-payment-api-prod (starter, branch main).
- SPRING_DATASOURCE_URL/USERNAME/PASSWORD -> sync: false OK
- SPRING_DATA_REDIS_HOST -> sync: false OK (port 6379 la gia tri mac dinh, khong secret)
- SPRING_RABBITMQ_HOST/USERNAME/PASSWORD -> sync: false OK (port 5672 la gia tri mac dinh, khong secret)
- JWT_SIGNING_KEY -> sync: false OK (da bo sung)
- Khong co gia tri secret hardcode trong file; tat ca cung qua Render dashboard.
- Chi chuan bi local, chua thuc su deploy cloud (Wave 1).

### 7. Kiem tra bao mat secret
- .env (root) da co trong .gitignore OK
- .env (src/payment-service-java) da co trong .gitignore cua module OK
- Khong co gia tri SPRING_DATASOURCE_PASSWORD, SPRING_RABBITMQ_PASSWORD, JWT_SIGNING_KEY, DEMO_AUTH_PASSWORD nao duoc commit vao tracked files (git grep chi tim thay ten bien tham chieu, khong gia tri thuc) OK

## Loi gap va cach xu ly

| Loi | Cach xu ly |
|-----|------------|
| JWT_SIGNING_KEY bi thieu khi docker compose interpolate .env | Sinh gia tri base64 ngau nhien, ghi vao .env (gitignored) |
| Schema validation: missing table [payment_outbox] khi payment service khoi dong | docker compose down -v xoa volume postgres cu, khoi dong lai de init-schema.sql tao day du schema |
| sed that bo doi ky tu dac biet trong base64 | Dung Python de cap nhat .env va render.yaml |

## Tong ket
- Stack local chay on dinh: co (yes)
- Tests: 241 passed, 0 failed
- Smoke-local.sh: PASS (tat ca 6 buoc)
- /healthz: HTTP 200
- Docker image: build thanh cong, container /healthz = 200
- render.yaml: da chuan bi cho Render, chua deploy cloud
- Bao mat: khong secret rot rui vao repo, .env gitignored

> Luu ý: Repo KHONG production-ready (xem RECRUITER-EVIDENCE.md). Chua co real PSP, chua co production database shape, khong claim production readiness.
