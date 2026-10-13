# Phase 2 Report — Live Evidence, Shared Workflow, SSO (2026-10-08)

## 2A. Live smoke (read-only GETs)
- CreditFlow ACA: /health/live 200 `{"status":"ok","service":"creditflow-api",
  "version":"0.2.0"}`; /health/ready `ready, model ok, checkpoint ok, config
  ok, logistic_regression_v001`; /model/info 200 (acc .837, roc_auc .867).
  => FIRST live-production evidence for the portfolio (lifts ADR-006 caveat
  for CreditFlow read path only; Terraform still capability, not proof).
- SupportDesk kh02: /api/health `{"status":"ok"}` 200 x3 (first try needed
  warm-up after sleep). Canonical confirmed live.
- SmartDoc backend 2hhz: TCP connects but 0 bytes in 30-90s (000 x4). Service
  asleep/suspended or deploy broken — recorded honestly; cd verify-readiness
  (Phase 1C/2B) will now catch this loudly on next push.

## 2B. Shared Render-readiness workflow — DONE
- Canonical `docs/render-readiness.yml` (workflow_call: health-path,
  attempts, interval; secrets render-api-key/service-id; fail-loud).
- Synced into SmartDoc + SupportDesk `.github/workflows/render-readiness.yml`.
- SmartDoc cd.yml verify-readiness refactored from 40-line inline script to
  12-line `uses: ./.github/workflows/render-readiness.yml`. YAML OK x3.

## 2C. SSO deferral — DONE
- `SmartDoc/docs/adr/0010-sso-deferred.md` (Accepted): Google OAuth canonical;
  Keycloak parked (live 404 on /realms/master); revisit trigger = public
  `.well-known/openid-configuration` 200. No code change (already OFF).

## Verify
- curl evidence above (CF 200s, SD 200 x3, SDC 000 x4 + verbose timeout).
- YAML parse OK x3. No test changes (workflows + docs only).
