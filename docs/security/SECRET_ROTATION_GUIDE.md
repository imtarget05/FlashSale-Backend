# Multi-Service Zero-Downtime Secret Rotation Guide

> **Audited Scope**: OpsBridge, QuanLyKho, SmartDocumentChatbot, retail-finance-core-platform, AKS-SRE-Platform  
> **Security Baseline**: Zero secrets committed to git; dual-key validation during key transitions; zero user-facing downtime.

---

## 1. Core Principles of Production Secret Rotation

1. **Dual Verification Window (Staged Deployment)**:
   - Systems consuming signed tokens (JWTs, webhooks) must accept both the *current* signing key and the *previous* signing key during the rotation window.
   - New tokens are signed exclusively with the *new* key.
   - The *previous* key is only decommissioned after the maximum token TTL (e.g. 60 minutes) has fully lapsed.

2. **Decoupled Configuration & Runtime Delivery**:
   - Production secrets are injected via Azure Key Vault / Cloudflare Secrets / GitHub Secrets / Environment variables.
   - `.env` files are local developer templates only and are strictly excluded by `.gitignore` across all 5 repositories.

---

## 2. Service-Specific Rotation Runbooks

### 2.1 OpsBridge: `JWT_SIGNING_KEY`
- **Impact Surface**: Order API, OpsBridge Web Admin authentication, internal microservice tokens.
- **Current Token TTL**: 60 minutes.
- **Rotation Procedure**:
  1. **Generate New Key**:
     ```bash
     openssl rand -base64 48 | tr -d '\n'
     ```
  2. **Update Azure Key Vault / Secret Store**:
     - Set `JWT_SIGNING_KEY_NEW` with the newly generated key.
     - Keep `JWT_SIGNING_KEY_CURRENT` as fallback.
  3. **Deploy API with Dual-Key Validation**:
     - `JwtOptions.cs` and `AddJwtBearer` accept signatures from either key.
     - Outgoing JWTs are signed with `JWT_SIGNING_KEY_NEW`.
  4. **Retire Old Key**:
     - Wait 65 minutes (TTL + 5 min clock skew margin).
     - Promote `JWT_SIGNING_KEY_NEW` to `JWT_SIGNING_KEY` and delete the old key.

---

### 2.2 QuanLyKho: `CLOUDFLARE_API_TOKEN` & SMTP Passwords
- **Impact Surface**: Cloudflare Worker edge proxy deployment, automated low-stock and shift reconciliation email alerts.
- **Rotation Procedure**:
  1. **Cloudflare API Token**:
     - Log in to Cloudflare Dashboard -> **My Profile** -> **API Tokens**.
     - Create a new token using the **Edit Cloudflare Workers** template, scoped strictly to the target Account and Zone.
     - Update GitHub Secrets:
       ```bash
       gh secret set CLOUDFLARE_API_TOKEN --body "<NEW_TOKEN>" --repo imtarget05/QuanLyKho
       ```
     - Verify with a test deployment run via GitHub Actions.
     - Revoke the previous token in the Cloudflare Dashboard.
  2. **SMTP Password / Mailer API Key**:
     - Generate a new SMTP password / API key in your transactional email provider (Brevo / SendGrid / AWS SES).
     - Update `SMTP_PASSWORD` in the production environment / secret store.
     - Perform a rolling restart of the backend FastAPI workers (`app.worker.py`).
     - Trigger test email via `scripts/lowstock-alert-smoke.sh` and revoke the old SMTP credential.

---

### 2.3 SmartDocumentChatbot: `LANGFUSE_SECRET_KEY`
- **Impact Surface**: Real-time LLM telemetry, token usage tracking, CRAG retrieval latency tracing.
- **Rotation Procedure**:
  1. Log into Langfuse Cloud -> **Settings** -> **API Keys**.
  2. Create a new API Key pair (`LANGFUSE_PUBLIC_KEY`, `LANGFUSE_SECRET_KEY`).
  3. In Render / deployment dashboard:
     - Update `LANGFUSE_SECRET_KEY` and `LANGFUSE_PUBLIC_KEY` in environment variables.
     - Trigger a zero-downtime rolling deploy of the FastAPI backend service.
  4. Monitor `application.log` to confirm telemetry batches are delivered with HTTP 200.
  5. Delete the revoked key from the Langfuse project dashboard.

---

### 2.4 retail-finance-core-platform & AKS-SRE-Platform
- **Database & Ingress Credentials**:
  - MySQL 8 passwords and Azure Service Principal OIDC tokens are managed via Azure Managed Identity / OIDC federation where possible.
  - Terraform state contains zero raw passwords; all dynamic secrets flow through Azure Key Vault CSI Provider.

---

## 3. Emergency Leak Incident Response
If any credential is suspected to be compromised:
1. **Immediate Revocation**: Decommission the compromised key at the provider immediately.
2. **PostgreSQL / Redis Session Invalidation**: Invalidate active refresh tokens and clear Redis token cache.
3. **Audit Log Inspection**: Review access logs for anomalous IP addresses or unexpected batch operations during the exposure window.
4. **Postmortem Entry**: File an incident report under `docs/incidents/` documenting timeline, root cause, and remediation proof.
