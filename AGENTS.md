# Portfolio Engineering Conventions (AGENTS.md)

Guidelines for this monorepo. They apply to **every** project folder here, in every session.

## 1. Clean Architecture at the folder level (always)

Every codebase must be organized by layer, and the dependency direction must never be
reversed:

```text
Domain  ←  Application  ←  Infrastructure  ←  Presentation (composition roots)
```

**Project 01 (`01-FlashSale-Backend`, .NET):**

```
src/FlashSale.Domain/          entities, value objects, domain exceptions (NO package refs)
src/FlashSale.Application/     use cases + ports (interfaces). Depends on Domain only.
src/FlashSale.Infrastructure/  adapters implementing the ports (EF Core, Redis, Service Bus)
src/Order.Api/                 presentation + composition root (thin Program.cs)
src/Order.Worker/              composition root for the async worker
tests/UnitTests/               use-case tests with fakes + architecture dependency guards
```

**Project 02 (`02-Productionized-LegacyApp`, Node.js):**

```
src/server.js       entry point (listen + graceful shutdown)
src/app.js          composition root (wiring only)
src/routes/         HTTP layer (mapping only, no business logic)
src/services/       business logic
src/middleware/     cross-cutting concerns (logging, 404, errors)
src/test/           tests (import app.js, never bind the production port)
```

**Project 03 (`03-AKS-SRE-Platform`):** infrastructure only —
`terraform/` (resources), `kubernetes/` (runtime manifests), `gitops/` (ArgoCD),
`scripts/` (bootstrap automation).

### Rules
1. No business logic in composition roots (`Program.cs`, `app.js`).
2. Domain/Application layers must not reference EF Core, Redis, Service Bus,
   ASP.NET Core, or the Infrastructure project. This is enforced by
   `01-FlashSale-Backend/tests/UnitTests/ArchitectureTests.cs` — a violation fails the build.
3. New infrastructure capability = new **port** (Application) + **adapter** (Infrastructure).
4. Endpoints/handlers depend on ports, never on concrete adapters or `DbContext`.
5. When adding a project/folder, place it in its layer folder and update the solution file.

## 2. Evidence-first engineering
- Reproduce the problem before fixing it; record real numbers in `docs/benchmarks/`
  (never fabricate metrics).
- Every significant decision gets an ADR in `docs/adr/`; failures get a postmortem in
  `docs/incidents/`.
- Keep plans/tasks current (`plans/`, `tasks/`).

## 3. Delivery hygiene
- No secrets in the repo — environment variables / Key Vault / GitHub secrets.
- Containers: multi-stage, non-root, healthcheck, `.dockerignore`.
- IaC must pass `terraform validate`; manifests must render (`kubectl kustomize`).
- Tests must pass locally before claiming completion (`dotnet test`, `npm test`,
  concurrency harness, container smoke test).
