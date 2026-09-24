# Phase 7B Azure Quota Gate

> **MANUAL VERIFICATION REQUIRED**
> This gate requires Azure portal action by the platform owner (P03).
> The application repo cannot verify Azure quotas without `az` CLI access
> and subscription permissions.

## Quota Check: 16/16 Live

- [ ] Azure subscription quota verified for AKS, ACR, PostgreSQL, Redis, etc.
- [ ] All required resource providers registered
- [ ] No quota blocks preventing Terraform apply

## Evidence

- Azure portal screenshot or `az quota show` output
- Resource provider registration status
- Date/time of verification

**Status:** PENDING_MANUAL_VERIFICATION
