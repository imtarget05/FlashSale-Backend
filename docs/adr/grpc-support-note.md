# gRPC Support Note

## JD Requirement

JD mentions "RESTful API / gRPC" for serving mobile (iOS/Android) and web applications.

## Current Implementation

The Java payment-service exposes both REST and unary gRPC APIs backed by the
same `PaymentService` business logic. REST remains the default client-facing
API and is documented with OpenAPI/Swagger at `/v3/api-docs` and
`/swagger-ui.html`. The gRPC contract is `src/main/proto/payment.proto`.

`ProcessPayment` validates the request, delegates to the existing idempotent
payment service, and accepts only Staff/Admin JWT roles. The interceptor checks
the bearer token signature and expiry, reloads the principal and authorities,
and rejects disabled or expired accounts. The health RPC is public and returns
only service status.

The server is disabled by default (`GRPC_SERVER_ENABLED=false`). Enabling it
requires readable TLS certificate-chain and private-key files via
`GRPC_TLS_CERT_CHAIN` and `GRPC_TLS_PRIVATE_KEY`; it refuses to start without
both. The current AKS manifest keeps gRPC disabled and does not expose port
9090, because no production certificate provisioning or client connectivity
has been configured. This is a code-level demo, not evidence of a deployed
gRPC endpoint.

| Aspect | REST | gRPC |
|--------|------|------|
| Protocol | HTTP + JSON | HTTP/2 + Protocol Buffers |
| Operations | Process/get/list payments | Process payment; health check |
| Authentication | JWT bearer token | JWT bearer metadata |
| Authorization | Spring Security roles | Staff/Admin checked per RPC |
| Transport security | HTTPS at deployment edge | TLS required by the gRPC server |
| Default deployment | Configured by the service | Disabled until TLS and network policy are provisioned |

Only unary RPCs are implemented; streaming is not claimed. Neither path invokes
a real payment provider.
