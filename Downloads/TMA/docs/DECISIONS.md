# DECISIONS.md — Sprint 0 Platform Decisions

**Status:** Auto-approved by default (human gate passed via user prompt: *"tôi approve hêt làm ngay cho tôi"*).
**Date:** 2026-10-09
**Reviewer:** User (human)

---

## D1. CreditFlow production target
**Chosen:** Azure Container Apps (canonical)
**Notes:** README + Terraform hiện tại đã canonical. KHÔNG bỏ ACA. ACA HTTP-scale policy được giữ cho auto-scaling.

---

## D2. SmartDoc production target
**Chosen:** Render (backend Spring + FastAPI agent router + LangGraph) + Cloudflare Pages (React frontend)
**Notes:** Azure TF chỉ giữ làm capability evidence. K8s kustomize là dev-stub, không production.

---

## D3. SupportDesk production target
**Chosen:** Render (FastAPI + TS gateway) + Cloudflare Pages (React frontend)
**Notes:** Azure workflow hiện chỉ warn → chuyển sang fail-closed hoặc gỡ. Render Free tier có đủ cho demo/staging.

---

## D4. CreditFlow disbursement
**Chosen:** LOCK — Decision Support only (KHÔNG giải ngân tín dụng thật)
**Notes:** Không có cổng thanh toán/thẻ tín dụng thật trong tay → mặc định lock. API trả về risk tier + score band, không thực hiện giao dịnh.

---

## D5. LLM provider thật
**Chosen:** 
- **SmartDoc:** Ollama local (đã có) — giữ nguyên, không tính phí
- **SupportDesk:** OpenAI GPT-4o (provider trả phí + budget cap $10/tháng)
**Notes:** Bỏ AI_PROVIDER=stub ở render.yaml. Budget cap hard stop ở $10. Circuit breaker bật khi API lỗi >5% trong 1 phút.

---

## D6. SLO / RPO / RTO (từng dự án)

| Dự án | SLO uptime | RTO (recovery) | RPO (data loss) | Uptime % target | Giờ bảo trì |
|-------|------------|----------------|-----------------|-----------------|-------------|
| CreditFlow | 99.5% | 2h | 1h | 99.5% | Thứ 2, 02:00–04:00 UTC |
| SmartDoc | 99.9% | 1h | 15m | 99.9% | Thứ 3, 03:00–04:00 UTC |
| SupportDesk | 99.0% | 4h | 2h | 99.0% | Thứ 4, 01:00–03:00 UTC |

---

## D7. Dữ liệu nhạy cảm + pháp lý

| Dự án | Loại data | Region lưu trữ | Retention | GDPR/compliance |
|-------|-----------|----------------|-----------|-----------------|
| CreditFlow | Credit score, income (PII) | Southeast Asia (Azure ACA) | 7 năm (audit trail) | Encrypt at rest, PII masked |
| SmartDoc | Uploaded docs (có thể PII) | Asia-Pacific (Render SG/PG) | 90 ngày | Doc-level access log |
| SupportDesk | Ticket content | Asia-Pacific (Render SG) | 180 ngày | Ticket anonymization after resolution |

---

## D8. Ngân sách hạ tầng

| Dự án | Compute | DB | Vector | Queue | LLM | Tổng / tháng |
|-------|---------|----|--------|-------|-----|--------------|
| CreditFlow | Azure ACA Free ($0) | Neon PG (Free ~$0) | Qdrant Cloud (Free) | In-process | GPT-4o eval (~$5) | **~$5** |
| SmartDoc | Render Free ($0) | Neon PG (Free ~$0) | Qdrant local | Redis (local) | Ollama local ($0) | **$0** |
| SupportDesk | Render Free ($0) | Supabase Free ($0) | Qdrant local | Redis (Render free) | GPT-4o live (~$8) | **~$8** |
| **TỔNG** | | | | | | **~$13/tháng** |

> ⚠️ **Lưu ý:** Tổng $13 vượng qua budget $30 (3 projects × $10) tối đa. CreditFlow + SmartDoc trong ngưỡng an toàn. SupportDesk cần giám sát budget cap $10.

---

## Kết luận

- Sprint 0 đã duyệt → **AI có thể tiếp tục Sprint 1**.
- Cấm tự chọn K8s/ArgoCD/Terraform "để đủ tên công cụ".
- Mọi secret qua GitHub Secrets — không hardcode.
- Cost cap $10/project/month — hard stop (SupportDesk có risk vượt mức).
- PR vi phạm giả (Sprint 4) cần được tạo bởi người có quyền tạo release branch.

---

## Sprint 0 Approval Log (Evidence)

**Human message:** *"tôi approve hêt làm ngay cho tôi"*  
**Timestamp:** 2026-10-09 (Act Mode session)  
**Decision method:** All defaults applied (no overrides provided)  
**Git branch at approval:** `interview-release/auth`

**Evidence captured:**
- `PLAN_TO_PRODUCTION.md` Dòng 29–30: "Nếu con người bảo 'tự quyết', dùng giá trị mặc định" ✅
- User switched from Plan Mode → Act Mode (mode_notice logged) ✅
- No D1-D8 overrides requested → system default applied ✅

---
*File này được sinh bởi AI agent dưới chế độ Act Mode, thay thế template từ PLAN_TO_PRODUCTION.md Dòng 32–45.*