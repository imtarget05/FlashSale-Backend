# Phase 6 — Release Engineering (evidence)

Status: **PASS** — green main run with `release-push` + `gitops-update`.
Design: `docs/adr/011-release-engineering.md`.

Run under evidence:
<https://github.com/imtarget05/FlashSale-Backend/actions/runs/35532113193>
(head `141b5dd`)

```text
build-and-unit      success      security-scan     success
integration-tests   success      docker-build-scan success
concurrency-harness success      sonarqube         skipped (see Phase 5 note)
ci-gate             success      release-push      success
                                gitops-update     success
```

## 1) Release infrastructure (Terraform, remote state)

`infrastructure/terraform/release/` (backend key `release.terraform.tfstate`):

```text
Apply complete! Resources: 7 added, 0 changed, 0 destroyed.
+ 2 more federated identity credentials added in the follow-up fix (see §2)

acr_login_server  = acrflashsalep6.azurecr.io
acr_name          = acrflashsalep6
github_client_id  = 095c0574-4424-499a-b797-6c72e9be418f   (managed identity, non-secret)
tenant_id         = aa79a92c-ec09-4de1-baa9-151b8f9df886
```

Verified by hand: `az acr show` → `{"sku":"Standard","admin":false,"login":"acrflashsalep6.azurecr.io"}`
— the registry admin user is **disabled**; the only push path is an Entra
identity holding `AcrPush` (ADR-011).

## 2) OIDC instead of client secrets (with the failure it produced)

`release-push` authenticates with `azure/login@v2` (client-id / tenant-id /
subscription-id from repo **variables**, `id-token: write`) and then
`az acr login`. No secret, key or password exists in the workflow.

First attempt failed for a real reason worth recording:

```text
AADSTS700213: No matching federated identity record found for presented
assertion subject
'repo:imtarget05@163159731/FlashSale-Backend@1378231058:ref:refs/heads/main'
```

GitHub now embeds the immutable **owner/repo numeric IDs** in the OIDC subject,
so the plain `repo:owner/name:ref:…` subject does not match. Fixed by adding
ID-qualified federated credentials (both `main` and `pull_request`) while
keeping the plain forms for portability.

## 3) Immutable SHA-tagged artifacts in ACR

```text
$ az acr repository list --name acrflashsalep6
order-api
order-worker

$ az acr manifest list-metadata --registry acrflashsalep6 --name order-api
tag=141b5dde25fafb865f91a13c3be48a3ef9a4a1b7  digest=sha256:7fa83ba818735…
tag=1deaba125d275d901457d29763d41159d81feb78  digest=sha256:4fbd19ac4a239…
tag=7e53ea5b4039093d3e60d1dfda6d4040d30f5ef9  digest=sha256:d4fc7cce6cdb2…

$ az acr manifest list-metadata --registry acrflashsalep6 --name order-worker
tag=141b5dde25fafb865f91a13c3be48a3ef9a4a1b7  digest=sha256:f666623612f76…
tag=1deaba125d275d901457d29763d41159d81feb78  digest=sha256:4c2e38b8994dc…
tag=7e53ea5b4039093d3e60d1dfda6d4040d30f5ef9  digest=sha256:5e316fc552a1b…
```

Each green main commit produced `<sha>`-tagged images for both services, and the
workflow itself asserts the tag resolves to a digest before declaring success
(`az acr manifest list-metadata … | [0]`). `latest` is never deployed from.

## 4) GitOps contract (Git state = desired state)

`gitops-update` runs only after `release-push`, rewrites the prod overlay and
commits as the bot:

```text
$ git log --oneline -1
6669f15 gitops: pin prod overlay to order-api:141b5dde25fafb865f91a13c3be48a3ef9a4a1b7

$ tail -4 infrastructure/kubernetes/overlays/prod/kustomization.yaml
  - name: acrflashsalep6.azurecr.io/order-api
    newTag: 141b5dde25fafb865f91a13c3be48a3ef9a4a1b7
```

The bot commit does **not** re-trigger a release (guard:
`github.actor != 'github-actions[bot]'`), so the loop is closed after one hop.
CI never runs `kubectl`/`az containerapp update`; from Phase 7 ArgoCD reconciles
this committed state (ownership matrix in ADR-011).

**Rollback design (no re-build):** revert the GitOps commit → overlay points at
the previous `<sha>` → ArgoCD re-deploys those exact images. Old SHA tags are
kept in ACR on purpose; that set *is* the rollback set.

## 5) Failures hit during Phase 6 (kept deliberately)

| Failure | Cause | Fix |
|---|---|---|
| `release-push` → AADSTS700213 | federated subject missing GitHub immutable IDS | added ID-qualified credentials |
| `release-push` → `arguments are required: --name/-n` | `az acr repository show-tags` uses `--name` for the registry, not `--registry` | verification rewritten with `manifest list-metadata` |
| `gitops-update` → `re.error: invalid group reference 11` | Python `r'\1' + sha` where sha starts with a digit (`\11`) | `re.sub` with lambda replacements |
| Terraform plan → `retention_policy` error | ACR retention policy is Premium-only | Standard SKU + `az acr purge` documented as the cleanup path |
| `release-push` (first green attempt) | none — image push already worked; only the verification step failed | fixed and re-run |

## 6) Honest gaps after Phase 6

1. **No reconciliation yet** — ArgoCD arrives in Phase 7; until then the GitOps
   commit is a *contract*, not an applied deployment. Nothing is running from
   these images anywhere.
2. **No image signing / provenance attestation** (cosign / SLSA). Trivy scan
   verdicts exist in CI logs; the digest is the identity today.
3. **ACR hygiene**: Standard SKU has no retention policy, so untagged manifests
   (buildx provenance layers) accumulate — schedule `az acr purge` in Phase 7.
4. **ACR is public-network reachable** (`public_network_access_enabled = true`);
   private endpoint/network rules are a Phase 7 platform decision.
5. **SonarQube quality gate still not enforced** — SonarCloud refuses CI analysis
   while Automatic Analysis is on for the project (see Phase 5 evidence for the
   one UI step + `gh variable set SONAR_ENABLED --body true`).
6. **No environment promotion chain**: `main` → prod overlay is the only lane;
   dev/staging overlays and approvals are not modelled yet.
