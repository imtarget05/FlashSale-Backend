# Authoritative Edge & Host Rate Limiting Architecture Defense

## 1. Problem Statement & Architecture Decision

In a flash-sale distributed commerce system, traffic spikes can saturate both edge reverse proxies and core application services. 

A common naive approach assumes that edge rate limiting (e.g. Cloudflare WAF / Worker rate limits) is sufficient to protect the backend. However:
1. Edge rate limiting operates across geographically distributed PoPs without global atomic counters; counters are approximate per-datacenter.
2. Direct-to-origin bypasses (if origin IP is leaked) can completely overwhelm the database.
3. Edge rate limiting cannot inspect deep application identity (e.g., JWT user claims vs. anonymous client IP) without SSL termination and expensive edge secrets.

## 2. Solution: Defense-in-Depth Layering

| Layer | Responsibility | Mechanism | Failure Mode |
|---|---|---|---|
| **Edge (Cloudflare Worker)** | Coarse volumetric filtering & geo-distribution | Fixed rate limits (e.g., 100 req/min/IP), origin cloaking, TLS termination | Pass-through of burst or approximate overages |
| **Origin Ingress (Envoy Gateway on AKS)** | Gateway-level origin enforcement & mTLS | Gateway API HTTPRoute, Cloudflare Authenticated Origin Pulls (Client CA validation) | Rejection with TLS Handshake Error if caller is not Cloudflare edge |
| **Host Application (.NET 10 Web API)** | Authoritative partition rate limiting | `Microsoft.AspNetCore.RateLimiting` (`PartitionedRateLimiter`) with RFC 7807 Problem Details and `Retry-After` header | Returns HTTP 429 `application/problem+json` with exact backoff seconds |

## 3. Distributed Messaging & Idempotency Invariants

When order workers fail or queue broker nodes failover:
- **At-least-once delivery:** RabbitMQ publisher confirms (`publisherConfirmationsEnabled: true`) ensure messages reach disk before publishing threads proceed.
- **Outbox Pattern:** Relational DB transactions wrap business state changes and outbox records atomically.
- **Consumer Idempotency:** The worker records processed message IDs and idempotency keys; duplicate deliveries result in `already_applied` responses without duplicate financial or inventory state changes.

## 4. Evidence & Verification

- Unit Tests: `tests/UnitTests/RateLimitingTests.cs` (197 passed in `FlashSale.UnitTests`, 47 passed in `OpsBridge.UnitTests`).
- Integration Tests: `tests/OpsBridge.IntegrationTests/RateLimitingIntegrationTests.cs` verifies HTTP 429, headers, and RFC 7807 compliance against live Testcontainers.
- AKS Fault Drill: `scripts/test-aks-fault-drill.sh` verifies worker pod SIGKILL auto-healing and publisher confirmation enforcement on Kubernetes.
