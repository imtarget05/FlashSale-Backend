# PROJECT SPEC — 01-FlashSale-Backend

## 1. Project Goal

Nâng cấp repo `01-FlashSale-Backend` thành backend production-style phù hợp phỏng vấn Backend Engineer, đặc biệt các yêu cầu:

- NestJS
- TypeScript
- REST
- GraphQL
- PostgreSQL
- Redis
- RabbitMQ
- JWT
- OAuth2
- CASL / authorization
- Swagger / OpenAPI
- async processing
- third-party integration
- OpenAI
- ElevenLabs
- reliability
- observability

Không rewrite toàn bộ nếu repo đã có các thành phần hoạt động.

---

# 2. Interview Story

Project phải chứng minh được:

> Một hệ thống Flash Sale xử lý concurrency cao, tránh overselling, xử lý bất đồng bộ bằng RabbitMQ, dùng Redis cho reservation/cache/rate limit, PostgreSQL cho dữ liệu bền vững, và có AI service tích hợp OpenAI + ElevenLabs theo kiến trúc async.

---

# 3. Core Architecture

```text
Client
  |
  v
API Gateway / Nginx
  |
  v
NestJS API
  |
  +---------------------+
  |                     |
  v                     v
Redis                PostgreSQL
  |
  v
RabbitMQ
  |
  +----------------------+--------------------+
  |                      |                    |
  v                      v                    v
Order Worker         AI Worker            TTS Worker
                         |                    |
                         v                    v
                      OpenAI             ElevenLabs
```

---

# 4. Functional Scope

## 4.1 Authentication

Must have:

- register;
- login;
- refresh token;
- logout;
- JWT access token;
- password hashing;
- role-based authorization.

Optional but recommended:

- Google OAuth2.

Roles:

- `CUSTOMER`
- `STAFF`
- `ADMIN`

---

## 4.2 Authorization

Dùng CASL hoặc policy layer tương đương.

Ví dụ:

- customer chỉ xem order của chính mình;
- staff xem order;
- admin quản lý product/inventory;
- AI endpoints yêu cầu user authenticated;
- admin-only endpoint xem AI usage.

---

## 4.3 Product

Features:

- create product;
- update product;
- get product;
- list/search/filter;
- stock information;
- flash-sale price;
- active/inactive status.

---

## 4.4 Flash Sale Order

Flow:

```text
User -> Create Order
     -> Validate sale
     -> Redis atomic stock reservation
     -> Publish order.created
     -> Return ACCEPTED
     -> Worker persists/processes order
     -> Success / Compensation
```

Phải xử lý:

- duplicate request;
- overselling;
- retry;
- reservation timeout;
- consumer failure;
- DB failure.

---

# 5. Redis Requirements

Redis phải có vai trò rõ ràng.

Implement tối thiểu:

1. Flash-sale stock reservation.
2. Product cache.
3. Rate limiting.
4. Idempotency key cache.
5. AI conversation/session cache nếu phù hợp.

Không dùng Redis chỉ để "có Redis".

---

# 6. RabbitMQ Requirements

Queues/exchanges đề xuất:

```text
order.created
order.retry
order.dlq

ai.requested
ai.failed

tts.requested
tts.completed
tts.dlq
```

Must support:

- durable queue;
- manual ack;
- retry;
- DLQ;
- idempotent consumer;
- structured event schema.

Ví dụ event:

```json
{
  "eventId": "uuid",
  "eventType": "order.created",
  "occurredAt": "ISO_DATE",
  "correlationId": "uuid",
  "payload": {}
}
```

---

# 7. PostgreSQL Requirements

Must have:

- migration;
- index;
- foreign key;
- transaction;
- unique constraint;
- audit timestamps.

Tables gợi ý:

```text
users
roles
products
inventory
flash_sales
orders
order_items
payment_attempts
processed_events
ai_requests
ai_usage
tts_jobs
```

---

# 8. REST API

Tối thiểu:

```text
POST   /auth/register
POST   /auth/login
POST   /auth/refresh
POST   /auth/logout

GET    /products
GET    /products/:id
POST   /products
PATCH  /products/:id

POST   /orders
GET    /orders/:id
GET    /orders/me

POST   /ai/chat
POST   /ai/content/summarize
POST   /ai/content/generate-quiz

POST   /ai/tts
GET    /ai/tts/:jobId

GET    /health
GET    /ready
```

Swagger phải hoạt động thật.

---

# 9. GraphQL

Không cần duplicate toàn bộ REST.

Implement tối thiểu:

```text
Query:
- product(id)
- products
- myOrders

Mutation:
- askProductAssistant
```

Mục tiêu là chứng minh hiểu GraphQL schema, resolver, auth context, error handling.

---

# 10. AI FEATURE A — Product Assistant

## Goal

AI chỉ được recommendation dựa trên product data thật.

Flow:

```text
User Question
   |
   v
Backend validates user
   |
   v
Retrieve product candidates
   |
   v
Build grounded context
   |
   v
OpenAI
   |
   v
Structured JSON response
```

Output example:

```json
{
  "answer": "string",
  "recommendedProducts": [
    {
      "productId": "uuid",
      "reason": "string"
    }
  ]
}
```

Rules:

- AI không được invent productId;
- validate output;
- log model, latency, token usage;
- timeout;
- retry có giới hạn;
- rate limit.

---

# 11. AI FEATURE B — Educational Content Service

Feature này dùng để sát domain doanh nghiệp xuất bản giáo dục.

Endpoints:

```text
POST /ai/content/summarize
POST /ai/content/generate-quiz
POST /ai/content/explain
```

Input:

```json
{
  "content": "...",
  "gradeLevel": "university",
  "language": "vi"
}
```

Quiz structured output:

```json
{
  "questions": [
    {
      "question": "...",
      "options": ["A", "B", "C", "D"],
      "answer": "A",
      "explanation": "..."
    }
  ]
}
```

---

# 12. AI FEATURE C — ElevenLabs TTS

Không block HTTP request chờ TTS dài.

Flow:

```text
POST /ai/tts
    |
    v
Create tts_job = PENDING
    |
    v
Publish tts.requested
    |
    v
Return 202 + jobId
    |
    v
TTS Worker
    |
    v
ElevenLabs
    |
    v
Store URL / file reference
    |
    v
status = DONE
```

API:

```text
POST /ai/tts
GET  /ai/tts/:jobId
```

Statuses:

- `PENDING`
- `PROCESSING`
- `DONE`
- `FAILED`

---

# 13. AI Safety / Reliability

Must implement:

- max input length;
- timeout;
- retries;
- model response validation;
- structured logs;
- token/cost tracking;
- no secrets in prompt;
- no sensitive data logged;
- fallback message when provider unavailable.

AI provider failure không được làm crash core order system.

---

# 14. Observability

Minimum:

- request log;
- correlation ID;
- worker event log;
- AI latency;
- RabbitMQ publish/consume error;
- Redis connection status;
- DB status.

Metrics recommended:

```text
http_request_duration
orders_created_total
stock_reservation_failed_total
rabbitmq_consume_failed_total
ai_requests_total
ai_request_duration
ai_tokens_total
tts_jobs_total
```

---

# 15. Testing

## Unit

- order validation;
- price calculation;
- authorization;
- AI response parser;
- stock reservation service.

## Integration

- PostgreSQL repository;
- Redis;
- RabbitMQ;
- auth flow.

## E2E

Scenario:

```text
Register
Login
Create/Get product
Create order
Verify order
Call AI assistant
Create TTS job
Poll TTS status
```

## Concurrency Test

Phải có test chứng minh:

```text
10 products
100+ concurrent requests
final successful order count <= 10
stock never negative
```

Không ghi benchmark giả.

---

# 16. Security

Must:

- DTO validation;
- secure password hashing;
- JWT expiration;
- refresh token handling;
- RBAC/ABAC;
- rate limiting;
- security headers;
- input sanitization where needed;
- no leaked stack trace in production;
- `.env.example`.

---

# 17. Folder Structure

Adapt theo repo hiện tại, không bắt buộc rewrite.

Target conceptual structure:

```text
src/
  auth/
  users/
  products/
  inventory/
  flash-sales/
  orders/
  messaging/
  redis/
  ai/
    openai/
    tts/
    dto/
    workers/
  graphql/
  common/
    auth/
    filters/
    guards/
    interceptors/
    logging/
    config/
  health/

test/
docs/
  architecture/
  decisions/
  interview-demo/
```

---

# 18. Implementation Phases

## Phase 0 — Audit

Cline phải output:

- current architecture;
- existing working features;
- missing requirements;
- technical debt;
- change plan.

Không code trước audit.

## Phase 1 — Core Hardening

- config;
- validation;
- global errors;
- logging;
- health;
- migrations.

## Phase 2 — Auth + Authorization

- JWT;
- refresh;
- roles;
- CASL/policies.

## Phase 3 — Flash Sale Correctness

- Redis reservation;
- idempotency;
- DB transactions;
- concurrency test.

## Phase 4 — RabbitMQ Reliability

- durable events;
- ack;
- retries;
- DLQ;
- worker idempotency.

## Phase 5 — REST + Swagger + GraphQL

- OpenAPI;
- GraphQL minimum scope.

## Phase 6 — OpenAI

- provider abstraction;
- product assistant;
- structured output;
- educational content endpoints.

## Phase 7 — ElevenLabs

- async TTS;
- queue;
- status API;
- failure handling.

## Phase 8 — Testing + Observability

- e2e;
- concurrency;
- metrics/logging;
- docs.

---

# 19. Demo Scenarios

## Demo 1 — Overselling Prevention

- set stock = 10;
- fire >=100 requests;
- show successful orders <=10;
- show stock >=0.

## Demo 2 — Duplicate Message

- deliver same event twice;
- prove worker handles idempotently.

## Demo 3 — RabbitMQ Failure

- simulate consumer failure;
- show retry;
- show DLQ after threshold.

## Demo 4 — AI Product Assistant

- ask a product question;
- show grounded product IDs;
- show structured output.

## Demo 5 — TTS

- create TTS job;
- show 202;
- worker processes;
- status transitions.

---

# 20. Out of Scope

Do not add unless core is complete:

- Kubernetes rewrite;
- service mesh;
- dozens of microservices;
- custom ML model training;
- vector DB purely for decoration;
- blockchain;
- event sourcing full rewrite.

---

# 21. Final Definition of Done

Project is interview-ready when:

- build succeeds;
- documented setup works;
- auth works;
- order flow works;
- Redis prevents overselling;
- RabbitMQ retry/DLQ works;
- REST + Swagger works;
- minimum GraphQL works;
- OpenAI integration runs with real provider when key supplied;
- ElevenLabs async integration runs when key supplied;
- provider unavailable path handled;
- tests exist and pass;
- no fake benchmark;
- README explains architecture and tradeoffs;
- `docs/interview-demo/` contains demo scripts.
