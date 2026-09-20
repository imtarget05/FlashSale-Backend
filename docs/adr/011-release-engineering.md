# ADR-011 — Release Engineering (immutable artifacts, ACR via OIDC, GitOps contract)

- Status: Accepted (Phase 6)
- Context: CI (Phase 5) proves build + tests + security on every PR, but nothing
  yet turns a green `main` commit into a **deployable, traceable artifact**.
  Local builds (`docker build` on a laptop) and mutable tags (`latest`) are
  exactly the gap between "CI passes" and "we know what is running".

## Decision

1. **Immutable, content-addressed images.** Every green push to `main` builds
   both images and pushes them to ACR tagged with the commit SHA:

   ```text
   <acr>.azurecr.io/order-api:<git-sha>
   <acr>.azurecr.io/order-worker:<git-sha>
   ```

   Rules: an image tag is written **once** and never re-built under the same
   name (the digest is the identity); `latest` may exist as a *pointer* for
   humans but is never the deploy source; rollback = deploy the previous SHA.
2. **Push via OIDC, no client secrets.** GitHub Actions authenticates to Azure
   with a federated credential (Workload identity federation): the workflow
   exchanges its OIDC token for an Entra token against a user-assigned managed
   identity that holds `AcrPush` on the registry. No `ACR_PASSWORD` in GitHub
   Secrets; a leaked runner cannot push after the trust is removed.
3. **GitOps contract.** CI owns *building and verifying*; the **Git state owns
   the desired runtime state**. After images are pushed, CI updates the
   kustomize overlay tag (`infrastructure/kubernetes/overlays/prod/…`) in a
   single "gitops:" commit. From Phase 7, ArgoCD syncs from that commit —
   no human `kubectl apply`, no `az containerapp update`.
4. **Ownership matrix (one owner per thing):**

   | Thing | Owner | Never touched by |
   |---|---|---|
   | Azure infrastructure (RGs, ACR, AKS, Postgres, Redis…) | Terraform | ArgoCD, manual portal changes |
   | Platform components in-cluster (ingress, cert-manager, monitoring) | Helm | Terraform |
   | Application workloads (Deployments/images/config) | ArgoCD via Git | Terraform, kubectl |
   | Image contents | CI (build + scan) | manual builds |

5. **Promotion & rollback design.** `main` is the promotion lane: PR (Phase 5
   quality bar) → merge → build+scan → push `sha` images → GitOps commit.
   Rollback is a revert of the GitOps commit (previous SHA re-deployed by
   ArgoCD). No re-build from old branches, no manual image surgery.
6. **Registry hygiene.** Registry is `Standard` SKU, admin user **disabled**,
   retention of untagged manifests via a lifecycle policy, and images keep their
   Trivy scan verdicts in the CI logs (Phase 7 may add ACR-native scanning).

## Trade-offs

- **No continuous deployment to prod yet.** Phase 6 stops at "artifact pushed +
  desired state committed"; the actual reconcile step (ArgoCD) is Phase 7. This
  keeps the blast radius of Phase 6 to the registry and git.
- **OIDC trust is repo-scoped** (`repo:imtarget05/FlashSale-Backend`) with
  subjects for `ref:refs/heads/main` and `pull_request` where needed — broad
  enough to run, narrow enough to matter.
- **SHA tags make the registry grow**; a lifecycle policy trims untagged
  manifests, and old tagged SHAs are the rollback set (deliberately kept).
