# GitFlow Branching Model for payment-service-java

## Branch Strategy

| Branch | Purpose | Protection |
|--------|---------|------------|
| `main` | Production-ready code. Only updated via release/hotfix merge. | Protected, requires PR + CI pass |
| `develop` | Integration branch for features. Default development branch. | Protected, requires PR + CI pass |
| `feature/*` | New features (e.g. `feature/kafka-integration`). Created from `develop`. | Not protected |
| `release/*` | Release preparation (e.g. `release/v1.0.0`). Created from `develop`, merged to `main` and `develop`. | Protected |
| `hotfix/*` | Production bug fixes. Created from `main`, merged to `main` and `develop`. | Protected |

## Workflow

1. **Start feature:** `git checkout develop && git checkout -b feature/<name>`
2. **Finish feature:** `git checkout develop && git merge --no-ff feature/<name>`
3. **Release:** `git checkout -b release/v1.0.0 develop` → bump version → `git checkout main && git merge --no-ff release/v1.0.0` → tag → `git checkout develop && git merge --no-ff release/v1.0.0`
4. **Hotfix:** `git checkout -b hotfix/v1.0.1 main` → fix → merge to `main` and `develop`

## Commit Convention

`feat(scope): <summary>` — follows the repo's existing convention (see `.kilo/command/*.md` and `project.md`).

Example:
```
feat(payment): add Kafka payment events publisher
```

## Tags

Semantic versioning tags on `main`:
- `v1.0.0` — first production release of payment-service-java

## CI Integration

GitHub Actions workflow (`.github/workflows/payment-service-java-ci.yml`) runs on push to `main` and PRs. Branch protection rules require CI green before merge.
