# gRPC Support Note

## JD Requirement

JD mentions "RESTful API / gRPC" for serving mobile (iOS/Android) and web applications.

## Current Implementation

The Java payment-service exposes **RESTful API** via Spring MVC (`@RestController`), documented with OpenAPI/Swagger at `/v3/api-docs` and `/swagger-ui.html`.

## gRPC — Planned, Not Yet Implemented

gRPC (Protocol Buffers, unary/streaming) is **not yet implemented** in this service. It is documented here as a known gap with rationale:

| Aspect | REST (current) | gRPC (planned) | Decision |
|--------|---------------|----------------|----------|
| Protocol | HTTP/1.1 + JSON | HTTP/2 + Protobuf | REST chosen for simplicity and broad client support |
| Client types | Mobile (iOS/Android) via JSON | Mobile via generated stubs | REST sufficient for current scope |
| Code generation | OpenAPI → client SDKs | protoc → language stubs | REST tooling more mature for team |
| Performance | Text-based JSON | Binary Protobuf | gRPC better for high-frequency internal calls |

## When to add gRPC

Add gRPC when:
- Internal service-to-service communication needs lower latency than HTTP/JSON
- Mobile team requests strongly-typed client stubs
- Streaming use cases appear (e.g. real-time payment status stream)

Implementation would add `spring-boot-starter-grpc`, a `.proto` service definition, and a `GrpcService` alongside the REST controller — both exposing the same business logic in `PaymentService`.
