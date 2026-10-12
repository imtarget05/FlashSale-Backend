#!/usr/bin/env bash
# =============================================================================
# BankOps — Script setup Neon PostgreSQL cho Cloudflare deployment
# =============================================================================
# Chạy: bash infra/scripts/setup-neon.sh
# Yêu cầu: psql, wrangler đã cài và đăng nhập Cloudflare
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_DIR="$(cd "$SCRIPT_DIR/../../backend" && pwd)"

# ─── Màu sắc output ──────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

info()    { echo -e "${BLUE}[INFO]${NC}  $*"; }
success() { echo -e "${GREEN}[OK]${NC}    $*"; }
warn()    { echo -e "${YELLOW}[WARN]${NC}  $*"; }
error()   { echo -e "${RED}[ERROR]${NC} $*"; exit 1; }

# ─── Banner ──────────────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║       BankOps — Cloudflare + Neon Setup Script           ║"
echo "║  Zero cold start cho nhân viên thu chi 24/7              ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ─── Kiểm tra tools ──────────────────────────────────────────────────────────
info "Kiểm tra dependencies..."
command -v psql     >/dev/null 2>&1 || error "psql chưa cài. brew install postgresql"
command -v wrangler >/dev/null 2>&1 || error "wrangler chưa cài. npm install -g wrangler"
command -v npm      >/dev/null 2>&1 || error "npm chưa cài."
success "Tất cả tools OK"

# ─── Nhập thông tin ──────────────────────────────────────────────────────────
echo ""
info "=== BƯỚC 1: Neon Database ==="
echo ""
echo "1. Vào https://console.neon.tech và tạo project 'bankops'"
echo "2. Lấy connection string dạng:"
echo "   postgresql://user:password@ep-xxx.ap-southeast-1.aws.neon.tech/bankops?sslmode=require"
echo ""
read -rp "Dán Neon connection string (pooled endpoint): " NEON_URL
[[ -z "$NEON_URL" ]] && error "Connection string không được trống"

# Validate format
if [[ ! "$NEON_URL" =~ ^postgresql:// ]]; then
  error "Connection string phải bắt đầu bằng postgresql://"
fi

# Chuyển sang asyncpg format cho Python (Alembic)
NEON_URL_ASYNC="${NEON_URL/postgresql:\/\//postgresql+asyncpg://}"

# ─── Test kết nối ─────────────────────────────────────────────────────────────
info "Test kết nối Neon..."
psql "$NEON_URL" -c "SELECT version();" >/dev/null 2>&1 || error "Không kết nối được Neon. Kiểm tra lại connection string."
success "Kết nối Neon OK"

# ─── Run migrations ──────────────────────────────────────────────────────────
echo ""
info "=== BƯỚC 2: Chạy Alembic migrations ==="
cd "$BACKEND_DIR"

# Kiểm tra poetry
if command -v poetry >/dev/null 2>&1; then
  PYTHON_RUN="poetry run"
else
  PYTHON_RUN=""
  warn "Poetry không có. Dùng python trực tiếp (cần venv đã setup)."
fi

DATABASE_URL="$NEON_URL_ASYNC" $PYTHON_RUN alembic upgrade head
success "Migrations hoàn tất"

# ─── Seed demo data ──────────────────────────────────────────────────────────
echo ""
info "=== BƯỚC 3: Seed demo data ==="
DATABASE_URL="$NEON_URL_ASYNC" $PYTHON_RUN python -c \
  "from app.seed import seed_demo; import asyncio; asyncio.run(seed_demo())"
success "Seed data hoàn tất"

# ─── Tạo JWT secret ──────────────────────────────────────────────────────────
echo ""
info "=== BƯỚC 4: Tạo JWT secret ==="
JWT_SECRET=$(openssl rand -hex 32)
SESSION_SECRET=$(openssl rand -hex 32)
success "JWT secret generated (32 bytes hex)"

# ─── Cài npm Worker dependencies ─────────────────────────────────────────────
echo ""
info "=== BƯỚC 5: Install Worker dependencies ==="
WORKER_DIR="$(cd "$SCRIPT_DIR/../cloudflare-worker" && pwd)"
cd "$WORKER_DIR"
npm install
success "npm install OK"

# ─── Tạo KV namespace ────────────────────────────────────────────────────────
echo ""
info "=== BƯỚC 6: Tạo Cloudflare KV namespace ==="
KV_OUTPUT=$(wrangler kv namespace create BANKOPS_KV 2>&1)
KV_ID=$(echo "$KV_OUTPUT" | grep -oP 'id = "\K[^"]+' || echo "")
if [[ -z "$KV_ID" ]]; then
  warn "Không tự động lấy KV ID. Chạy thủ công: wrangler kv namespace create BANKOPS_KV"
  warn "Rồi cập nhật KV id trong wrangler.toml"
else
  # Cập nhật wrangler.toml với KV ID thực
  sed -i.bak "s/PLACEHOLDER_KV_ID/$KV_ID/" "$WORKER_DIR/wrangler.toml"
  success "KV namespace tạo xong, id = $KV_ID"
fi

# ─── Đặt Cloudflare secrets ──────────────────────────────────────────────────
echo ""
info "=== BƯỚC 7: Đặt secrets lên Cloudflare Workers ==="
cd "$WORKER_DIR"

echo "$NEON_URL"     | wrangler secret put DATABASE_URL   && success "DATABASE_URL set"
echo "$JWT_SECRET"   | wrangler secret put JWT_SECRET     && success "JWT_SECRET set"
echo "$SESSION_SECRET" | wrangler secret put SESSION_SECRET && success "SESSION_SECRET set"

# Google Client ID (public, không cần secret nhưng để Worker đọc được)
read -rp "Google Client ID (hoặc Enter để skip): " GOOGLE_CLIENT_ID
if [[ -n "$GOOGLE_CLIENT_ID" ]]; then
  echo "$GOOGLE_CLIENT_ID" | wrangler secret put GOOGLE_CLIENT_ID
  success "GOOGLE_CLIENT_ID set"
fi

# ─── Deploy Worker ────────────────────────────────────────────────────────────
echo ""
info "=== BƯỚC 8: Deploy Cloudflare Worker ==="
npm run deploy
success "Worker deployed!"

# ─── Tóm tắt ─────────────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║                    ✅ SETUP HOÀN TẤT                     ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "Worker URL: https://bankops-api.<account>.workers.dev"
echo ""
echo "Bước tiếp theo:"
echo "  1. Build Flutter web:"
echo "     cd frontend"
echo "     flutter build web --release \\"
echo "       --dart-define=API_BASE_URL=https://bankops-api.<account>.workers.dev"
echo ""
echo "  2. Deploy Pages:"
echo "     cd infra/cloudflare-worker"
echo "     wrangler pages deploy ../frontend/build/web --project-name=bankops-web"
echo ""
echo "  3. Thêm secrets GitHub Actions:"
echo "     CLOUDFLARE_API_TOKEN  = $(wrangler whoami 2>/dev/null | grep -oP 'token.*' || echo '<lấy từ dash.cloudflare.com>')"
echo "     CLOUDFLARE_ACCOUNT_ID = $(wrangler whoami 2>/dev/null | grep -oP 'account.*' || echo '<lấy từ dash.cloudflare.com>')"
echo "     API_BASE_URL          = https://bankops-api.<account>.workers.dev"
echo ""
