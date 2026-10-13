# Reusable: Render readiness poll (shared, Phase 2)

Use from any project cd/deploy workflow instead of copy-pasting the curl loop:

```yaml
jobs:
  verify:
    uses: ./.github/workflows/render-readiness.yml
    with:
      health-path: /api/health        # or /api/actuator/health
      attempts: '10'
      interval-seconds: '15'
    secrets:
      render-api-key: ${{ secrets.RENDER_API_KEY }}
      render-service-id: ${{ secrets.RENDER_SERVICE_ID }}
```

Behavior: resolves service URL via Render API, polls `{url}{health-path}`
until HTTP 200; fails loudly if secrets missing or URL unresolvable.
Inputs default to SupportDesk values (10 x 15s); SmartDoc callers pass
health-path `/api/actuator/health` and attempts `20` for Spring cold start.
