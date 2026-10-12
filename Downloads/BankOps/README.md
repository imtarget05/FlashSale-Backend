# BankOps — Hệ thống Vận hành Kho & Quỹ Mô phỏng (End-to-End MVP)

**Trạng thái:** Ứng dụng Web/Cross-platform chạy xuyên suốt từ đầu đến cuối (Flutter Web + FastAPI + PostgreSQL + Cloudflare Worker).  
**Lưu ý an toàn:** Tất cả tiền, quỹ, giao dịch, mã tài sản và danh tính trong hệ thống là **giả lập** (`simulation: true`). Không kết nối core banking và không dùng dữ liệu ngân hàng thật.

---

## 1. Kiến trúc & Thành phần đã triển khai

| Thành phần | Đường dẫn | Mô tả |
|---|---|---|
| **Frontend (Flutter Web & Multi-platform)** | `frontend/` | Ứng dụng Flutter Material 3 kết nối trực tiếp tới backend thật qua `HttpBankOpsRepository` (`/api/v1`). Bao gồm đầy đủ các màn: Đăng nhập (Tài khoản/Mật khẩu demo + Google Sign-In), Tổng quan, Kho, Quỹ, Yêu cầu (Tạo & Gửi duyệt), Duyệt (Maker $\neq$ Checker), Tích hợp (Staging CSV/JSON/Google Sheets $\to$ Preview $\to$ Confirm), Hoạt động (SSE/Polling), Báo cáo (Biểu đồ ngày/tháng, Tồn đọng, Đối soát). |
| **Backend API (FastAPI + SQLAlchemy Async)** | `backend/` | Phục vụ cả đường dẫn gốc `/` và `/api/v1/*`. Thực thi đầy đủ RBAC theo capability, bất biến Maker $\neq$ Checker, khóa lạc quan (`version` / `If-Match`), `Idempotency-Key` bắt buộc cho thao tác ghi, sổ cái bút toán kép (`cash_journals` + `cash_entries`), và audit hash-chain. |
| **Cloudflare Worker Gateway (Hono + Neon)** | `infra/cloudflare-worker/` | Thực thi đầy đủ các nhóm API `/api/v1/health`, `/api/v1/auth`, `/api/v1/me`, `/api/v1/inventory/*`, `/api/v1/cash-sim/*`, `/api/v1/approvals/*`, `/api/v1/activity/*`, `/api/v1/reports/*`, `/api/v1/integrations/*`, `/api/v1/reconciliations`. |
| **Cơ sở dữ liệu & Seed mẫu** | `sample-data/` & `backend/app/db/seed.py` | Dữ liệu giả lập cho người dùng, kho, vật tư, tài sản có serial, quỹ mô phỏng, sổ quỹ và lịch sử giao dịch. |

---

## 2. Tài khoản Demo (Môi trường dữ liệu giả lập)

Mật khẩu mặc định cho tất cả tài khoản demo: **`DemoPass123!`**

| Username | Vai trò | Quyền hạn chính |
|---|---|---|
| `demo_maker` | Maker (Tạo phiếu) | Tạo phiếu nhập/xuất kho, tạo phiếu thu/chi quỹ mô phỏng, gửi duyệt (`submit`), tải file tích hợp staging. |
| `demo_checker` | Checker (Duyệt phiếu) | Xem hộp thư duyệt (`/api/v1/approvals/inbox`), duyệt (`approve`) hoặc từ chối (`reject`) phiếu do người khác tạo, chạy đối soát. |
| `demo_auditor` | Auditor (Kiểm toán) | Xem toàn bộ báo cáo, lịch sử hoạt động, kết quả đối soát và nhật ký kiểm toán. |

> Trên màn hình đăng nhập Web, bạn có thể bấm trực tiếp vào các chip **`demo_maker`**, **`demo_checker`**, hoặc **`demo_auditor`** để điền nhanh thông tin đăng nhập.

---

## 3. Chạy nhanh trên môi trường Local (Docker Compose)

```bash
docker compose up -d --build
```

- **Web UI (Nginx phục vụ bản build Flutter Web):** `http://localhost:8080`
- **Backend FastAPI (`/api/v1` & `/docs`):** `http://localhost:8000/api/v1/health`
- **PostgreSQL 16:** `localhost:5432` (`bankops` / `bankops_demo_pass`)

---

## 4. Phát triển & Kiểm thử từng phần

### Backend (FastAPI + Pytest)
```bash
# Chạy toàn bộ bộ kiểm thử backend (99 passed)
pytest backend/tests -q
```

### Frontend (Flutter Web)
```bash
cd frontend

# Kiểm tra tĩnh & chạy widget/unit tests
flutter analyze
flutter test

# Chạy trực tiếp trên trình duyệt nối với backend thật
flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:8000/api/v1

# Build bản phân phối Web (output tại build/web và đồng bộ sang dist/)
flutter build web --dart-define=API_BASE_URL=http://localhost:8000/api/v1
cp -R build/web/* dist/
```

### Cloudflare Worker
```bash
cd infra/cloudflare-worker
npx tsc --noEmit
npx wrangler dev
```

---

## 5. Luồng nghiệm thu đầu-cuối trên trình duyệt (E2E Maker-Checker)

1. Mở trình duyệt tại `http://localhost:8080`, chọn chip **`demo_maker`** và bấm **Đăng nhập**.
2. Vào mục **Yêu cầu**:
   - Bấm **Tạo phiếu kho**, chọn kho/vật tư/số lượng $\to$ **Tạo phiếu nháp**, sau đó bấm **Gửi duyệt** trên phiếu vừa tạo.
   - Bấm **Tạo phiếu quỹ**, nhập số tiền thu/chi $\to$ **Tạo phiếu nháp**, sau đó bấm **Gửi duyệt**.
3. Mở menu người dùng ở góc phải trên $\to$ **Đăng xuất**, sau đó chọn chip **`demo_checker`** và bấm **Đăng nhập**.
4. Vào mục **Duyệt**: kiểm tra các phiếu đang chờ trong hộp thư duyệt và bấm **Duyệt**.
5. Vào mục **Kho**, **Quỹ**, và **Báo cáo**: xác nhận số dư tồn kho, số dư quỹ mô phỏng, sổ quỹ và biểu đồ ngày/tháng đã cập nhật tức thì.

---

## 6. Bản đồ tài liệu thiết kế

| Tệp | Nội dung |
|---|---|
| `docs/00-decisions.md` | Quyết định chung, thuật ngữ, ranh giới |
| `docs/01-prd.md` | Người dùng, phạm vi, yêu cầu |
| `docs/02-roadmap.md` | MVP và vertical slices |
| `docs/03-architecture.md` | Modular monolith và sơ đồ |
| `docs/04-database.md` | Schema, ledger, migrations |
| `docs/05-workflows.md` | Phê duyệt, đối soát, bất biến |
| `docs/06-api.md` | API contract và OpenAPI outline |
| `docs/07-flutter.md` | UI/UX, responsive, animation |
| `docs/08-security.md` | Auth, RBAC, audit, bảo vệ dữ liệu |
| `docs/09-integrations-jobs.md` | Integration hub, jobs, SSE |
| `docs/10-qa.md` | Test, QA, acceptance |
| `docs/11-devops.md` | Docker, cấu hình, deploy |
| `docs/12-glossary.md` | Từ điển dữ liệu |
| `docs/13-backlog.md` | Backlog ưu tiên |
| `sample-data/*.json` | Seed dữ liệu hoàn toàn giả |

## Quy tắc bắt buộc

- Không dùng dữ liệu khách hàng/ngân hàng thật; không nhập mã định danh, tài khoản, số dư hoặc chứng từ thật.
- Không kết nối core banking, không tự động hạch toán hoặc chuyển tiền thật.
- `inventory` và `cash_sim` là hai bounded context tách riêng; không chuyển giá trị giữa chúng.
- Mọi thay đổi số lượng/số dư chỉ qua nghiệp vụ đã duyệt, có idempotency, audit và đối soát.
