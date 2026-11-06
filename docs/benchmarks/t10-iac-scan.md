# T10 — IaC Scan Report

## Scope
- Tool: Trivy v0.74.0 (`trivy config`)
- FlashSale SHA: `96dc3d9` (FlashSale side); AKS-SRE-Platform at its current state
- Commands:
  - `trivy config <AKS-SRE-Platform>/terraform/aks-foundation`
  - `trivy config <AKS-SRE-Platform>/kubernetes`
  - `trivy config <FlashSale>/src/payment-service-java/k8s`

## AKS terraform (terraform/aks-foundation/modules/aks_cluster/main.tf)

| Check | ID | Severity | Summary |
|-------|----|----------|---------|
| Cluster logging via OMS Agent | AZU-0040 | MEDIUM | AKS logging to Azure Monitoring not configured |
| API server IP access limits | AZU-0041 | CRITICAL | API server not limited to specific IP ranges |
| Private cluster | AZU-0065 | MEDIUM | Private cluster not enabled |
| Azure Policy add-on | AZU-0066 | LOW | Azure Policy Add-On not enabled |
| Disk encryption set ID | AZU-0067 | LOW | Disk encryption set ID not configured |

Totals: 5 failures (CRITICAL: 1, HIGH: 0, MEDIUM: 2, LOW: 2)

## Kubernetes manifests (AKS-SRE-Platform/kubernetes)

| File | Failures | HIGH | CRITICAL | Notable checks |
|------|----------|------|----------|----------------|
| apps/agent-hub/agent-hub.yaml | 7 | 1 | 0 | KSV-0014 HIGH readOnlyRootFilesystem; KSV-0001 MEDIUM allowPrivilegeEscalation; KSV-0030/KSV-0104 LOW seccomp |
| apps/langfuse/langfuse-deployment.yaml | 3 | 0 | 0 | KSV-0125 MEDIUM untrusted registry; KSV-0020/0021 LOW runAsUser/Group |
| order-worker.yaml | 13 | 3 | 0 | KSV-0014 HIGH readOnlyRootFilesystem; KSV-0118 HIGH default security context (root); KSV-0013 MEDIUM :latest tag |
| keda-autoscaler.yaml | 0 | 0 | 0 | — |
| keda-trigger-auth.yaml | 0 | 0 | 0 | — |
| namespace.yaml | 0 | 0 | 0 | — |
| pod-disruption-budget.yaml | 0 | 0 | 0 | — |

Totals: 23 failures (HIGH: 4, MEDIUM: ~7, LOW: ~12)

## FlashSale payment-service k8s (src/payment-service-java/k8s)

| Check | ID | Severity | Summary |
|-------|----|----------|---------|
| ConfigMap sensitive contents | KSV-01010 | MEDIUM | ConfigMap 'payment-service-config' stores APP_JWT_EXPIRATION_MS, SERVER_PORT |
| Image from untrusted registry | KSV-0125 | MEDIUM | payment-service image from untrusted registry |

Totals: 2 failures (MEDIUM: 2)

## Raw reports
- `/tmp/trivy-config-tf.txt` (AKS terraform)
- `/tmp/trivy-config-k8s.txt` (AKS kubernetes)
- `/tmp/trivy-config-flashsale-k8s.txt` (FlashSale k8s)

## Disposition
- AZU-0041 CRITICAL (API server IP limits): address before production; restrict API server authorized IP ranges.
- HIGH k8s findings (KSV-0014 readOnlyRootFilesystem, KSV-0118 default security context): add securityContext to order-worker and agent-hub deployments.
- MEDIUM/LOW: accepted risk / backlog; seccomp, capabilities.drop, runAsNonRoot, image tag pinning.
- Note: These scans target AKS platform manifests; the FlashSale payment-service k8s used in kind has only minor findings (no HIGH/CRITICAL).
