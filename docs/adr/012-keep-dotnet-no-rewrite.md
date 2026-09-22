# ADR-012 — Keep .NET 10 / C#; map the interview spec onto ASP.NET Core

- Status: Accepted (Interview Release, Phase I)
- Context: the interview brief is written in NestJS/TypeScript vocabulary
  (guards, CASL abilities, Swagger, GraphQL, OpenAI/ElevenLabs providers). The
  existing P01 codebase is a **verified** .NET 10 solution whose value is not the
  language but the *evidence*: 32 unit tests, Testcontainers integration tests,
  an oversell harness, at-least-once queue redelivery with exactly-once business
  effects, and immutable-image release engineering (ADR-011).
- Problem: a rewrite to NestJS would discard that evidence and re-open every
  concurrency/idempotency question the project already answered with proof.

## Decision

**Keep .NET 10 / C#. Do not rewrite.** The interview requirements are satisfied
by mapping each NestJS concept onto its first-class ASP.NET Core equivalent —
the *capability* is what is being demonstrated, not the framework brand.

| Interview spec (NestJS/TS) | This codebase (.NET 10) | Where |
|---|---|---|
| NestJS module + controller | ASP.NET Core Minimal API endpoint group | `src/Order.Api/` |
| TypeScript DTO + `class-validator` | C# record + explicit validation in the handler | `FlashSale.Application` |
| `@nestjs/jwt` + `JwtAuthGuard` | `AddAuthentication().AddJwtBearer()` + `RequireAuthorization()` | `src/Order.Api/Auth/` |
| CASL ability factory | ASP.NET Core policy + `IAuthorizationHandler` | `src/Order.Api/Auth/` |
| `@nestjs/swagger` | `AddOpenApi()` / `MapOpenApi()` (built-in) | `src/Order.Api/Program.cs` |
| Swagger UI | `Swashbuckle.AspNetCore.SwaggerUi` (SHOULD, not blocking) | `src/Order.Api/Program.cs` |
| GraphQL (Apollo) | Hot Chocolate — **deferred, Phase V** | not in v1.0 |
| OpenAI SDK provider | `IAssistantProvider` port + fake/real/unavailable — **deferred, Phase VI** | not in v1.0 |
| ElevenLabs REST provider | `ITtsProvider` port + fake/real/unavailable — **deferred, Phase VII** | not in v1.0 |
| Prometheus `/metrics` | `System.Diagnostics.Metrics` + JSON snapshot — **Phase III** | `src/Order.Api/Metrics/` |

## Consequences

- **Preserved:** every existing regression gate stays meaningful and unmodified
  (32 unit tests, integration suite, oversell harness, Kustomize validation,
  mutation suite, immutable images). A rewrite would have invalidated all of it.
- **Preserved:** the Clean Architecture dependency rule is *executable*
  (`ArchitectureTests`), so new auth/product code cannot quietly couple layers.
- **Cost:** the deliverable is not a NestJS repository. This is stated openly in
  `docs/interview-demo/` rather than papered over — the mapping table above is
  the honest answer to "why isn't this NestJS?".
- **Cost:** GraphQL/OpenAI/ElevenLabs are not in v1.0. They are explicitly
  deferred (Phases V–VII) and **must not delay** the Interview Release.

## Alternatives rejected

- **Rewrite in NestJS.** Discards verified concurrency evidence, re-opens
  oversell/idempotency risk, and spends the interview budget on porting instead
  of on the auth/product/E2E surface the brief actually asks to see.
- **Hybrid (NestJS gateway in front of .NET).** Two runtimes, two CI pipelines,
  two dependency graphs, and a new network hop — for zero additional evidence.
