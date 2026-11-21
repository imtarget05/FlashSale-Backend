# Local payment-path k6 benchmark — 2026-11-21

Source baseline: `0f0ba729227e8a3198005bb4158dc7aa5333da8a` plus the benchmark and Compose changes in this branch. The tested endpoint was `POST /api/payments`, with a unique idempotency key and fixed successful `10.01 USD` payload per request. This is a simulated payment provider, not a bank or Stripe transaction.

| VUs | Scheduled duration | Completed requests | Throughput | p95 | p99 | Non-200 responses |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 30 s | 11,660 | 388.65 req/s | 2.99 ms | 39.10 ms | 0 (0%) |
| 5 | 30 s; 56.1 s including drain | 14,319 | 255.44 req/s | 74.53 ms | 81.31 ms | 2 (0.014%) |
| 10 | 30 s | 13,261 | 441.99 req/s | 86.71 ms | 92.72 ms | 0 (0%) |

Raw [1 VU](final-vus-1.json), [5 VU](final-vus-5.json), and [10 VU](final-vus-10.json) k6 1.5.0 summaries contain request counts, latency distributions, thresholds, and error rates. All runs passed the script's p95 < 500 ms and error rate < 1% thresholds. The 5 VU run had two non-200 responses (no HTTP 429); their status codes were not captured, and its extra drain time makes the throughput comparison less clean. A prior [default-limiter trial](default-limiter-vus-1.json) returned 98.32% non-200 responses under unpaced load; it is excluded from the payment-path table.

Environment: Apple M1 Pro, 16 GiB RAM, Docker Desktop 29.6.1. The payment container was capped at 0.5 CPU and 512 MiB RAM; PostgreSQL 15, RabbitMQ 3, and Redis 7 ran as local Compose dependencies. k6 ran in a Docker container on the same machine and reached the API over `host.docker.internal:18081`. A fresh PostgreSQL database was initialized by Flyway. For the table, the demo rate limiter was explicitly set to capacity 100,000 and refill 100,000/minute so HTTP 429 would not dominate the measurement. The raw output reports zero rate-limited responses.

Reproduce with `src/payment-service-java/docker-compose.payment-java.yml`, disposable credentials set through environment variables, `PAYMENT_PORT=18081`, and the same rate-limit overrides. Obtain a demo JWT from `/api/auth/login`, keep it in a temporary private env file as `TOKEN`, and run:

```sh
docker run --rm --env-file /path/to/private-benchmark.env \
  -e VUS=5 -e DURATION=30s \
  -v "$PWD/load-tests/k6:/scripts:ro" -v "$PWD/results:/results" \
  grafana/k6:1.5.0 run --summary-export=/results/summary.json \
  /scripts/payment-path-benchmark.js
```

This is a local capacity observation with shared host resources and a simulated provider. It does not establish production throughput or a cloud SLO.
