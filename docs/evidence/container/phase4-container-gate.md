# Phase 4 — Container Gate (evidence)

Status: **PASS (with one real defect found and fixed)**.
Scope: formalize the container acceptance that Task 3 already implemented; no
Docker re-architecture. This gate must prove a **clean checkout** yields a
working system with *meaningful* probes — not that a Dockerfile exists.

```text
fresh clone  →  .env.example  →  docker compose up  →  Postgres + Redis + RabbitMQ
        →  API + Worker  →  POST Order (202)  →  status: completed  →  stock decremented
```

## 1) Fresh-clone run (the real test)

```bash
git clone <repo> /tmp/fresh-clone-check     # HEAD a86fb26, clean working tree
cd /tmp/fresh-clone-check && cp .env.example .env
API_PORT=5003 docker compose -p freshgate up -d --build
```

Compose reported every dependency healthy before starting the app containers,
which then reported healthy themselves:

```text
Container freshgate-rabbitmq-1 Healthy
Container freshgate-postgres-1 Healthy
Container freshgate-redis-1 Healthy
Container freshgate-order-api-1 Started / Up About a minute (healthy)
Container freshgate-order-worker-1 Started
```

Schema bootstrap happened automatically on first boot (ADR-006: EF
`MigrateAsync` + idempotent seed): `1|iPhone 15 Pro Max|100`.

Non-root runtime (ADR-007) verified from *inside* the containers:

```text
api_user=app      worker_user=app
docker inspect → Config.User = app for both
```

## 2) Business flow end-to-end (fresh clone)

Contract used: `Idempotency-Key` **HTTP header** + body
`{"productId":1,"quantity":1}`. (For the record: the body has no idempotency
field — `OrderRequest(int ProductId, int Quantity)` — and the API generates a
random GUID when the header is absent, so polling a client-invented body key
always returns `processing`. That is API contract behaviour, not a failure.)

```text
before stock = 100
POST /api/orders  → HTTP/1.1 202 Accepted
                    Location: /api/orders/freshgate-1789920752
                    {"message":"Order accepted","status":"processing"}
GET /api/orders/freshgate-1789920752 (t+4s)
   → {"status":"completed","orderId":1,"productId":1,"quantity":1}
after stock  = 99
Orders rows  = 1
```

Async path proof (not an in-process shortcut): the **worker** logged the
persistence, the API did not, and the queue registered two consumers:

```text
docker logs freshgate-order-worker-1 | grep -c 'Order persisted' = 1
docker logs freshgate-order-api-1    | grep -c 'Order persisted' = 0
GET /api/queues/%2F/orders → consumers: 2
```

## 3) Idempotency under duplicate delivery

Replaying the same `Idempotency-Key` against the fast-fail reservation tier:

```text
POST (same key) → HTTP 409 {"error":"Duplicate request","idempotencyKey":"p4gate-ok-…"}
stock unchanged = 48        # no double decrement
Orders rows     = 4         # no duplicate row
```

## 4) Probe semantics — defect found and fixed

**Finding:** the readiness endpoint called `await db.Database.CanConnectAsync()`
and **discarded the boolean**, catching only exceptions. With PostgreSQL
stopped it still answered `200 {"status":"ready"}`. In Kubernetes that is a
false-ready pod receiving traffic — a real orchestration defect.

**Fix** (`src/Order.Api/Program.cs`, minimal and commented):

```csharp
bool canConnect;
try { canConnect = await db.Database.CanConnectAsync(); }
catch (Exception ex) { readinessLogger.LogWarning(...); canConnect = false; }
return canConnect ? Results.Ok(new { status = "ready" })
                  : Results.Json(new { status = "not-ready" }, statusCode: 503);
```

**Re-verified after the fix** (image rebuilt):

```text
[1] PG up        → ready: 200 {"status":"ready"}
[2] PG down      → ready: 503 {"status":"not-ready"}     ← was 200 before the fix
[2b] PG down     → live : 200 {"status":"healthy"}       ← liveness independent of deps (correct)
[3] PG up again  → ready: 200 {"status":"ready"}         ← self-heals
```

The container-level healthcheck reacts too (interval 10s × retries 5):

```text
t+8s … t+40s api_healthcheck=healthy
t+48s        api_healthcheck=unhealthy
after PG returns → recovered=healthy
```

## 5) Persistence and dependency-loss behaviour

| Drill | Action | Result |
|---|---|---|
| PostgreSQL persistence | restart `postgres` | orders=1, stock=99 survived; `ready` restored; no re-seed |
| RabbitMQ persistence | restart `rabbitmq` (+ postgres) | durable `orders` queue retained; consumers reconnected |
| Redis not required | stop `redis`, then POST order | `200 {"message":"Order placed successfully"}` (synchronous Postgres fallback), order persisted (`orderId=5`), stock 10→9 |
| Redis reconstructable | start `redis`, `POST /internal/resync-stock/3` | `{"id":3,"resyncedTo":9}`, product read consistent |
| Stock exhaustion | POST on a product with stock 0 | `409 {"error":"Out of stock"}` (fast-fail, no oversell) |

## 6) Gate checklist

| Requirement | Status | Evidence |
|---|---|---|
| fresh clone → `compose up` works | **PASS** | section 1 (clone at `a86fb26`) |
| API non-root | **PASS** | `whoami=app`, `Config.User=app` |
| Worker non-root | **PASS** | `whoami=app`, `Config.User=app` |
| Readiness/Liveness probes | **PASS after fix** | section 4 (defect shipped in `a86fb26`) |
| PostgreSQL persistent | **PASS** | restart keeps orders/stock |
| RabbitMQ persistent | **PASS** | durable queue `orders` |
| Redis reconstructable | **PASS** | stop/FLUSH → resync from Postgres truth |
| Kafka optional | **PASS (by design)** | compose profile `experimental`, unused by the app (`KafkaActivityTracker` dead code, ADR-007); deliberately not started to avoid pulling unused images |
| Clean-environment run | **PASS** | whole gate ran on a fresh clone; the drill stack was then destroyed (`down -v`) and the developer stack restored |

## 7) Findings and follow-ups (honest list)

1. **Readiness false-positive — fixed, needs a commit.** The fix lives in the
   working tree; `a86fb26` (and therefore the fresh clone in section 1) still
   contains the buggy probe. Phase 4 cannot be called "closed in git" until this
   is committed.
2. **API requires PostgreSQL at boot.** `DatabaseInitializer` migrates at
   startup, so with Postgres down the container crash-loops rather than staying
   up and reporting not-ready. Acceptable with `depends_on`/init ordering, but
   worth knowing: probe semantics apply *after* a successful start.
3. **`Cannot load library libgssapi_krb5.so.2` log noise** on `aspnet:10.0`
   (Ubuntu 24.04) from the native Kerberos shim. Non-fatal here (no Kerberos
   auth; Postgres uses password auth). Cosmetic; installing
   `libgssapi-krb5-2` would silence it if log scanning ever matters.
4. **Two consumers on one queue** (API in-process consumer + worker, ADR-005)
   means an order can be processed by the API rather than the worker. Intentional
   for the dev queue; recorded so the AKS phase can decide deliberately.
5. **Port 5000 is occupied by macOS Control Center** on this host, so the gate
   used `API_PORT=5001`/`5003`. Recorded so the demo script stays reproducible.
6. **`warning CS8618` in dead `KafkaActivityTracker`** still compiles; Phase 5
   (Sonar/CI) is where it should be caught.

## 8) Reproduce the gate

```bash
# 1. clean checkout
git clone <repo> /tmp/fresh-clone-check && cd /tmp/fresh-clone-check
cp .env.example .env

# 2. bring it up on a free port
API_PORT=5003 docker compose -p freshgate up -d --build

# 3. prove the business flow (header contract!)
curl -i -X POST localhost:5003/api/orders -H 'Content-Type: application/json' \
     -H "Idempotency-Key: gate-$(date +%s)" -d '{"productId":1,"quantity":1}'
curl -s localhost:5003/api/orders/<that-key>       # → status: completed
curl -s localhost:5003/api/products/1              # → stock decremented

# 4. prove the probes are real
docker stop freshgate-postgres-1
curl -si localhost:5003/health/ready | head -1     # → 503 not-ready
curl -s  localhost:5003/health/live                # → healthy (liveness unaffected)
docker start freshgate-postgres-1

# 5. destroy the drill environment
docker compose -p freshgate down -v
```

## Conclusion

**PHASE 4 — CONTAINER GATE: PASS**, conditional on committing the readiness fix
(finding 1). The gate did its job: instead of rubber-stamping "Dockerfile
exists", it exposed a probe that would have mis-reported health in production.
