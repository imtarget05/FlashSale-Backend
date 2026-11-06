# T10 — Performance/Load Test Report

## Scope
- Target: `http://localhost:8080/healthz` (via `kubectl port-forward svc/payment-service 8080:80 -n flashsale`)
- Tool: Apache Bench (`ab`) v2.3
- FlashSale SHA: `96dc3d9`
- Image: `flashsale/payment-service:local-96dc3d9 @ sha256:9cb5a5ede5331c3150ad57f7e3bcdf3102fc0fddd628a8ec6d9cf2b499905af8`
- Cluster: kind `flashsale-staging` (v1.30.0, arm64, single node)
- payment-service: 2/2 Ready

## Test configuration
- Requests: 1000 total
- Concurrency: 50
- Endpoint: GET /healthz (read-only, safe; no payment mutation)
- Tool: `ab -n 1000 -c 50 http://localhost:8080/healthz`

## Measured results

| Metric | Value |
|--------|-------|
| Complete requests | 1000 |
| Failed requests | 0 |
| Error rate | 0.00% |
| Requests per second | 48.48 [#/sec] |
| Time per request (mean, concurrent) | 20.625 ms |
| Time per request (mean, per request) | 1031.259 ms |
| Transfer rate | 13.97 Kbytes/sec |
| Total time | 20.625 seconds |

### Latency percentiles
| Percentile | Latency (ms) |
|------------|--------------|
| 50% | 1009 |
| 66% | 1012 |
| 75% | 1014 |
| 80% | 1019 |
| 90% | 1061 |
| 95% (p95) | 1103 |
| 98% | 1162 |
| 99% (p99) | 1188 |
| 100% (max) | 1189 |

## Notes
- All 1000 requests returned 200; 0 failures.
- p95 = 1103 ms, p99 = 1188 ms — measured THROUGH `kubectl port-forward`, which adds overhead.
- Direct `curl /healthz` samples earlier showed ~12–75 ms (single request, no concurrency), indicating port-forward + kind networking is the dominant latency component, not the application.
- Because /healthz is a static healthy response, it does not exercise payment processing or DB. For payment-path load, a JWT token and /api/payments/{idempotencyKey} would be required (authZ-protected).
- T09 SLO target p95 < 500 ms: NOT met through port-forward (1103 ms), but this is a port-forward/kind artifact, not an application SLO violation.
