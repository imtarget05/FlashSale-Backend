#!/usr/bin/env bash
# E2E-FS-05: backup và restore phải khôi phục được invariant NGHIỆP VỤ.
#
# Vì sao script này tồn tại: "pg_dump succeeded" KHÔNG phải bằng chứng phục
# hồi. Một backup có thể chạy xanh, file có thể tồn tại, và dữ liệu vẫn có
# thể sai khi restore — nếu thứ bị hỏng là dữ liệu mà backup sinh ra, hoặc
# nếu quy trình khôi phục bỏ sót thứ gì đó. Cách duy nhất để biết là sửa dữ
# liệu cho sai, restore lại, rồi kiểm tra invariant nghiệp vụ quay về đúng.
#
# Toàn bộ chạy trên Postgres container thật, dùng schema do chính EF
# migrations của repo tạo. Không có schema viết tay, nên nếu domain model
# đổi thì script này bắt được ngay thay vì im lặng kiểm tra một bảng cũ.
set -euo pipefail

REPO="${REPO:-/Users/mainguyenbinhtan/Downloads/Projects/FlashSale-Backend}"
IMAGE="${IMAGE:-postgres:15-alpine}"
CONTAINER="fs-backup-e2e-$$"
DB="flashsale"
USER="postgres"
ARTIFACT_DIR="${ARTIFACT_DIR:-/tmp/fs-ev}"
DUMP="$ARTIFACT_DIR/backup.dump"

mkdir -p "$ARTIFACT_DIR"
log() { printf '  %s\n' "$*"; }

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

# --- Câu hỏi nghiệp vụ mà invariant trả lời ---------------------------------
# "Tồn kho còn bảo toàn không?" Tức là: stock cuối + đã bán == tồn kho đầu.
# Đây cũng chính là invariant mà E2E-FS-01 dùng, nên nó phải đứng vững cả
# sau khi dữ liệu đi qua backup và quay lại.
#
# Schema lấy từ EF migrations của chính repo, nên tên bảng/cột ở đây là
# "Products"/"AvailableStock" (PascalCase, không phải products/available_stock).
# Dùng đúng tên thật là điều kiện để câu SQL này chạy được; viết tay bảng
# song song sẽ khiến script kiểm tra một thứ khác với thứ production dùng.
inventory_invariant() {
  docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -c "
    SELECT
      CASE WHEN p.\"AvailableStock\" + COALESCE(SUM(o.\"Quantity\"),0) = 10
      THEN 'PASS' ELSE 'FAIL' END
    FROM \"Products\" p
    LEFT JOIN \"Orders\" o ON o.\"ProductId\" = p.\"Id\"
    WHERE p.\"Id\" = 1 GROUP BY p.\"Id\", p.\"AvailableStock\";"
}

echo "=== E2E-FS-05 backup -> mutate -> restore ==="

log "starting real postgres ($IMAGE)"
docker run -d --name "$CONTAINER" -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB="$DB" -P "$IMAGE" >/dev/null
for _ in $(seq 1 40); do
  docker exec "$CONTAINER" pg_isready -U "$USER" -d "$DB" >/dev/null 2>&1 && break
  sleep 1
done

PORT=$(docker port "$CONTAINER" 5432/tcp | head -1 | sed 's/.*://')
CONN="Host=localhost;Port=$PORT;Database=$DB;Username=$USER;Password=postgres"

# EF.Design chỉ nằm trên Order.Api, không có trên FlashSale.Infrastructure,
# nên startup project phải là Order.Api. Dùng sai sẽ báo "doesn't reference
# Microsoft.EntityFrameworkCore.Design" và script dừng im lặng.
log "applying the repository's own EF migrations"
(cd "$REPO" && dotnet ef database update \
  --project src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj \
  --startup-project src/Order.Api/Order.Api.csproj \
  --connection "$CONN" >/dev/null)

log "seeding a known business state"
# Cột lấy từ FlashSale.Domain/Entities/Product.cs, không đoán. Product KHÔNG
# có CreatedAt/UpdatedAt — thêm hai cột đó khiến INSERT fail, và script im
# lặng dừng ở đó vì stderr bị nuốt.
docker exec -i "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "INSERT INTO \"Products\" (\"SKU\", \"Name\", \"Description\", \"Category\",
                            \"OriginalPrice\", \"FlashSalePrice\",
                            \"AvailableStock\", \"ReorderThreshold\")
   VALUES ('E2E-001', 'E2E Widget', 'backup/restore fixture', 'test',
           1200, 1000, 6, 2)"

PRODUCT_ID=$(docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -c \
  "SELECT \"Id\" FROM \"Products\" ORDER BY \"Id\" DESC LIMIT 1" | tr -d '\r\n ')
log "seeded product id=$PRODUCT_ID"
[ -n "$PRODUCT_ID" ] || { log "FAIL: could not read the seeded product id"; exit 1; }

docker exec -i "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "INSERT INTO \"Orders\" (\"ProductId\", \"Quantity\", \"IdempotencyKey\", \"CreatedAt\")
   VALUES ($PRODUCT_ID, 4, 'e2e-backup-key', now())"

BEFORE=$(inventory_invariant)
log "invariant before backup: $BEFORE (stock 6 + sold 4 == initial 10)"

log "taking the backup with pg_dump (custom format)"
# Định dạng custom (-Fc) thay vì SQL thuần, và restore bằng pg_restore.
#
# Đây không phải lựa chọn sở thích. pg_dump thuần | psql phát lại câu
# CREATE INDEX/CONSTRAINT lên database đã có, nên toàn bộ lệnh bị lỗi "already
# exists" và dữ liệu cuối cùng KHÔNG được phục hồi — đúng cái thất bại mà
# "pg_dump succeeded" che giấu. pg_restore --clean --if-exists mới là đường
# phục hồi dùng được.
docker exec "$CONTAINER" pg_dump -U "$USER" -Fc -d "$DB" > "$DUMP"
DUMP_BYTES=$(wc -c < "$DUMP" | tr -d ' ')
log "dump size: ${DUMP_BYTES} bytes"
[ "$DUMP_BYTES" -gt 1000 ] || { log "FAIL: dump suspiciously small"; exit 1; }

log "mutating the live database: delete the order, corrupt the stock"
docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "DELETE FROM \"Orders\" WHERE \"IdempotencyKey\" = 'e2e-backup-key';
   UPDATE \"Products\" SET \"AvailableStock\" = 999 WHERE \"Id\" = $PRODUCT_ID"
CORRUPTED=$(inventory_invariant)
log "invariant after mutation: $CORRUPTED (this MUST be FAIL)"
[ "$CORRUPTED" = "FAIL" ] || { log "FAIL: mutation did not corrupt the data"; exit 1; }

log "restoring with pg_restore --clean --if-exists"
docker exec -i "$CONTAINER" pg_restore --clean --if-exists --no-owner \
  -U "$USER" -d "$DB" < "$DUMP" >/dev/null 2>&1 \
  || { log "FAIL: pg_restore errored"; exit 1; }
AFTER=$(inventory_invariant)
log "invariant after restore: $AFTER"

RESULT="PASS"
[ "$BEFORE" = "PASS" ] || RESULT="FAIL(before)"
[ "$CORRUPTED" = "FAIL" ] || RESULT="FAIL(mutation did not corrupt)"
[ "$AFTER" = "PASS" ] || RESULT="FAIL(restore did not recover)"

{
  echo "scenario: E2E-FS-05"
  echo "database: $IMAGE (real container, repo EF migrations)"
  echo "invariant: available_stock + sold == initial_stock"
  echo "before_backup: $BEFORE"
  echo "after_mutation: $CORRUPTED"
  echo "after_restore: $AFTER"
  echo "dump_bytes: $DUMP_BYTES"
  echo "result: $RESULT"
} > "$ARTIFACT_DIR/fs05-backup-restore.txt"

log "result: $RESULT"
[ "$RESULT" = "PASS" ] || exit 1
