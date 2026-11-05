# gRPC implementation TDD evidence

## User journey

As an authenticated Staff or Admin client, I can submit a valid payment request
over gRPC and receive the same idempotent payment result as the REST API.

## RED / GREEN

| Stage | Command | Result |
|---|---|---|
| RED | `mvn -B -Dtest=PaymentGrpcServiceImplTest test` | Failed at test compilation because the gRPC service and auth interceptor were not implemented; generated protobuf classes compiled. |
| GREEN | `mvn -B clean verify` | PASS: 75 tests, 0 failures, 0 errors, 0 skipped. |
| Container | `docker build -t flashsale-payment-api:verify .` | PASS: local image built; not pushed or deployed. |
| Auth edge-case RED | `mvn -B -Dtest=PaymentGrpcAuthInterceptorTest#rejectsTokenWithoutSubjectBeforeLoadingUser test` | Failed because the interceptor called `UserDetailsService` with a null username. |
| Auth edge-case GREEN | `mvn -B -Dproject.build.directory=/tmp/flashsale-grpc-target-4adefcf -Dtest=PaymentGrpcAuthInterceptorTest test` | PASS: 6 tests, 0 failures, 0 errors, 0 skipped. |

## Guarantees

| Behavior | Test |
|---|---|
| Staff/Admin authorization and payment result mapping | `PaymentGrpcServiceImplTest` |
| Invalid UUID, amount, order, currency, and idempotency key are rejected before business logic | `PaymentGrpcServiceImplTest` |
| Customer and unauthenticated callers cannot process payments | `PaymentGrpcServiceImplTest` |
| Transient failures preserve the recorded payment result; conflicts and unexpected exceptions use non-sensitive gRPC statuses | `PaymentGrpcServiceImplTest` |
| JWT metadata is required, verified, tied to a current enabled principal, and propagated into the RPC context | `PaymentGrpcAuthInterceptorTest` |
| Health RPC bypasses authentication; gRPC startup refuses missing TLS material | `PaymentGrpcAuthInterceptorTest`, `PaymentGrpcServerConfigTest` |

For the three gRPC production classes, JaCoCo reports 93.4% instruction
coverage, 93.9% line coverage, and 81.4% branch coverage from the full
verification run. The server startup configuration has lower individual
coverage because a valid TLS certificate/key pair is not committed as a test
fixture; the missing-TLS guard and readable-but-invalid material path are
covered. No claim is made that an endpoint was deployed or tested against a
production identity provider.

## Platform validation boundaries

- `kubectl kustomize k8s`: PASS (local render).
- Ruby YAML parsing for all Kubernetes manifests: PASS.
- `terraform fmt -check -recursive` and `terraform validate`: PASS after
  `terraform init -backend=false -input=false`.
- No Kubernetes apply, Terraform plan/apply, Azure provisioning, image push, or
  production deployment was run.
