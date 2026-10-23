# Secret Rotation Checklist — P0 Blocking Items

**Generated:** 2026-10-22 | **Scope:** 5 repositories in portfolio | **Status:** ✅ No tracked secrets found; .env.example placeholders only.

---

## 1. Confirmed Exposed Secrets (Must Rotate)

| Secret | Repository | Where Used / Config Path | Rotation Procedure | Verification |
|--------|------------|--------------------------|-------------------|--------------|
| **JWT_SIGNING_KEY** | OpsBridge | `src/Order.Api/Program.cs` → `Jwt:SigningKey` / `Jwt__SigningKey` env var; CI `opsbridge-stack-smoke` generates per-run key | 1. Generate new 256-bit key: `openssl rand -base64 48` 2. Update GitHub Secret `JWT_SIGNING_KEY` 3. Update Azure Key Vault / Key Vault Reference 4. Re-deploy API (picks up new key) 5. Verify all tokens invalidated (existing sessions logout) | `grep -r "JWT_SIGNING_KEY" --include="*.cs" --include="*.yml" OpsBridge/` → no hardcoded values; CI generates new key per run |
| **CLOUDFLARE_API_TOKEN** | QuanLyKho | `backend/app/config.py` → `cloudflare_api_token` (SecretStr); `backend/app/services/ai.py` → Cloudflare Workers AI provider | 1. Cloudflare Dashboard → My Profile → API Tokens → Create new token with Workers AI:Read scope 2. Update GitHub Secret `CLOUDFLARE_API_TOKEN` 3. Update `.env` locally and any deployed env 4. Verify AI assessment works: `curl -X POST /ai/assess-request` | `grep -r "CLOUDFLARE_API_TOKEN" QuanLyKho/backend/app/` → only reads from settings/env |
| **SMTP Credentials** (password) | QuanLyKho | `backend/app/services/email_service.py` → `SMTP_PASSWORD` / `SMTP_USERNAME` | 1. Generate new app password in email provider (Gmail/Outlook/SendGrid) 2. Update GitHub Secret `SMTP_PASSWORD` 3. Update deployed env 4. Test: trigger email via `/notifications/test-email` | `grep -r "SMTP_PASSWORD" QuanLyKho/backend/app/` → only from settings |
| **LANGFUSE_SECRET_KEY** | SmartDocumentChatbot | `docker-compose.yml` → `LANGFUSE_SECRET_KEY`; backend config reads from env | 1. Langfuse Dashboard → Settings → API Keys → Generate new secret 2. Update GitHub Secret `LANGFUSE_SECRET_KEY` 3. Update deployed docker-compose / K8s secret 4. Verify traces appear in Langfuse | `grep -r "LANGFUSE_SECRET_KEY" SmartDocumentChatbot/` → only from env |

---

## 2. Additional Secrets to Verify (Defense in Depth)

| Secret Type | Repository | Search Pattern | Status |
|-------------|------------|----------------|--------|
| Database passwords (Postgres/MySQL) | All | `DATABASE_URL`, `POSTGRES_PASSWORD`, `ConnectionStrings__` | ✅ Only in .env.example / GitHub Secrets |
| Redis passwords | OpsBridge, QuanLyKho | `Redis__ConnectionString`, `REDIS_PASSWORD` | ✅ Only in CI service defs / .env.example |
| Azure credentials | OpsBridge, AKS-SRE-Platform | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `ACR_NAME` | ✅ GitHub repo Variables (not secrets) + OIDC |
| GitHub PAT / Actions tokens | All | `ghp_`, `github_token` | ✅ Only in workflow files as `${{ secrets.GITHUB_TOKEN }}` |
| Docker registry credentials | All | `DOCKER_HUB_USERNAME`, `DOCKER_HUB_TOKEN` | ✅ Not used (ACR + OIDC) |
| SonarCloud token | OpsBridge | `SONAR_TOKEN` | ✅ GitHub Secret, gated by `vars.SONAR_ENABLED` |
| Qdrant API key | SmartDocumentChatbot | `QDRANT_API_KEY` | ✅ Only in .env.example |

---

## 3. Rotation Procedure Template (Per Secret)

```bash
# 1. Generate new secret
# Example for JWT key:
NEW_KEY=$(openssl rand -base64 48 | tr -d '\n')
echo "New key length: ${#NEW_KEY} chars (must be >= 32)"

# 2. Update GitHub Secret (via gh CLI)
gh secret set SECRET_NAME --body "$NEW_KEY" --repo owner/repo

# 3. Update Azure Key Vault (if applicable)
az keyvault secret set --vault-name <kv-name> --name SECRET_NAME --value "$NEW_KEY"

# 4. Trigger redeployment of affected services
# For GitHub Actions: push empty commit or use workflow_dispatch
gh workflow run deploy.yml --repo owner/repo

# 5. Verify
# - Check CI logs for successful auth
# - Verify no 401/403 errors in application logs
# - For JWT: existing sessions should be invalidated (force re-login)
```

---

## 4. Current Git State Verification

```bash
# Run this to confirm no tracked .env or secrets:
for r in OpsBridge QuanLyKho SmartDocumentChatbot AKS-SRE-Platform retail-finance-core-platform; do
  echo "=== $r ==="
  git -C "/Users/mainguyenbinhtan/Downloads/$r" ls-files "*.env*" 2>/dev/null
done

# Output: only .env.example files tracked (expected)
```

**Result (2026-10-22):**
- OpsBridge: no tracked .env files
- QuanLyKho: `backend/.env.example` tracked ✅
- SmartDocumentChatbot: `.env.example` tracked ✅
- AKS-SRE-Platform: no tracked .env files
- retail-finance-core-platform: no tracked .env files

**All .gitignore files correctly exclude `.env` / `.env.*` and allow `.env.example`**

---

## 5. Action Items (Owner: Repo Owner)

- [ ] Rotate JWT_SIGNING_KEY (OpsBridge) — highest impact (invalidates all sessions)
- [ ] Rotate CLOUDFLARE_API_TOKEN (QuanLyKho) — used by AI assessment provider
- [ ] Rotate SMTP_PASSWORD (QuanLyKho) — email delivery
- [ ] Rotate LANGFUSE_SECRET_KEY (SmartDocumentChatbot) — observability traces
- [ ] Verify all GitHub Secrets updated and CI passes
- [ ] Document rotation date in this file for audit trail

---

**Note:** This checklist is for the repo owner to execute. The agent cannot rotate external provider secrets. The above documents the current state and exact procedures for manual rotation.