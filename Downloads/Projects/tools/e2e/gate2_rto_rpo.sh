#!/usr/bin/env bash
# GATE 2 — RTO / RPO restore drill, có đo thời gian thật.
#
# Vì sao "backup exit 0" không đủ
# ------------------------------
# RTO và RPO là hai CON SỐ, không phải hai câu. "Backup succeeded" không nói
# bao lâu để phục hồi, và không nói mất bao nhiêu dữ liệu. Câu hỏi "mất DB
# lúc 10:00, bao lâu hệ thống chạy lại, mất bao nhiêu dữ liệu" chỉ trả lời
# được bằng cách làm đúng việc đó và đồng hồ.
#
# Cách đo
# -------
#   RPO = dữ liệu ghi SAU lần backup cuối. Đo bằng cách tạo một order ngay
#         TRƯỚC backup, rồi một order NGAY SAU backup. Order thứ hai không có
#         trong backup — đó chính là dữ liệu mất đi.
#   RTO = thời gian từ lúc phát hiện mất dữ liệu đến lúc dữ liệu dùng được
#         trở lại, đo bằng monotonic clock quanh chuỗi restore.
#
# Caveat in ra ở cuối: đây là restore của MỘT database trên máy dev, không
# phải failover của cụm production. Nó chặn được độ trễ của cơ chế restore,
# không thay cho kế hoạch khôi phục cả vùng.
set -uo pipefail

REPO="${REPO:-/Users/mainguyenbinhtan/Downloads/Projects/FlashSale-Backend}"
IMAGE="${IMAGE:-postgres:15-alpine}"
CONTAINER="rto-rpo-$$"
DB="flashsale"; USER="postgres"
ART="${ARTIFACT_DIR:-/tmp/fs-ev}"
DUMP="$ART/rto-rpo.dump"
mkdir -p "$ART"

log() { printf '  %s\n' "$*"; }
cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "=== GATE 2: RTO / RPO restore drill ==="
log "starting a real postgres ($IMAGE)"
docker run -d --name "$CONTAINER" -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB="$DB" -P "$IMAGE" >/dev/null
for _ in $(seq 1 40); do
  docker exec "$CONTAINER" pg_isready -U "$USER" -d "$DB" >/dev/null 2>&1 && break
  sleep 1
done
PORT=$(docker port "$CONTAINER" 5432/tcp | head -1 | sed 's/.*://')
CONN="Host=localhost;Port=$PORT;Database=$DB;Username=$USER;Password=postgres"

# EF.Design chỉ nằm trên Order.Api; dùng sai startup project sẽ dừng im lặng.
log "applying the repository's own EF migrations"
(cd "$REPO" && dotnet ef database update \
  --project src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj \
  --startup-project src/Order.Api/Order.Api.csproj \
  --connection "$CONN" >/dev/null) || { log "FAIL: migrations errored"; exit 1; }

# Cách chắc chắn để lấy id: INSERT không RETURNING, rồi SELECT lại theo SKU.
#
# `psql -t -A -c "INSERT ... RETURNING id"` in ra CẢ command tag ("INSERT 0 1")
# lẫn giá trị, và `-t -A` không loại bỏ tag đó — cắt "dòng cuối" vẫn dính
# ("INSERT01"). Dùng `tail -n 1` trông hợp lý nhưng hỏng. SELECT lại theo một
# khoá đọc được thì không còn gì để đoán.
docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "INSERT INTO \"Products\" (\"SKU\",\"Name\",\"Description\",\"Category\",
   \"OriginalPrice\",\"FlashSalePrice\",\"AvailableStock\",\"ReorderThreshold\")
   VALUES ('RTO-001','RTO Widget','restore drill','test',1200,1000,7,2)"

PID=$(docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -c \
  "SELECT \"Id\" FROM \"Products\" WHERE \"SKU\"='RTO-001'" | tr -d '\r\n ')
case "$PID" in
  ''|*[!0-9]*) log "FAIL: could not read a numeric product id (got '$PID')"; exit 1 ;;
esac
log "seeded product id=$PID"

# Chụp lại số liệu tại THỜI ĐIỂM BACKUP, không phải tại thời điểm hỏng.
#
# Đây là điều lần chạy đầu dạy: tôi so restore với trạng thái trước khi hỏng và
# thấy sold 5 -> 2 nên báo MISMATCH. Nhưng 5 gồm cả order ghi SAU backup, mà
# đơn nghĩa của backup là đưa hệ thống về ĐÚNG THỜI ĐIỂM BACKUP — thứ bị mất
# chính là RPO. Restore đã đúng; phép so sánh của tôi sai.
#
# Nên: chụp state lúc backup, rồi assert restore khớp state đó, và báo riêng
# bao nhiêu đơn vị dữ liệu bị mất do nằm ngoài backup.
docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "INSERT INTO \"Orders\" (\"ProductId\",\"Quantity\",\"IdempotencyKey\",\"CreatedAt\")
   VALUES ($PID,2,'before-backup',now())"
BEFORE_EPOCH=$(date +%s)

read -r AT_BACKUP_SOLD AT_BACKUP_STOCK <<<"$(docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -F' ' -c \
  "SELECT COALESCE(SUM(o.\"Quantity\"),0), p.\"AvailableStock\"
   FROM \"Products\" p LEFT JOIN \"Orders\" o ON o.\"ProductId\"=p.\"Id\"
   WHERE p.\"Id\"=$PID GROUP BY p.\"Id\", p.\"AvailableStock\"")"
log "state AT BACKUP TIME: sold=$AT_BACKUP_SOLD stock=$AT_BACKUP_STOCK"

log "taking the backup (timed)"
T0=$(python3 -c 'import time;print(time.monotonic())')
docker exec "$CONTAINER" sh -c "pg_dump -U $USER -Fc -d $DB > /tmp/backup.dump"
docker cp "$CONTAINER:/tmp/backup.dump" "$DUMP" >/dev/null
T1=$(python3 -c 'import time;print(time.monotonic())')
BACKUP_S=$(python3 -c "print(round($T1-$T0,2))")
DUMP_BYTES=$(wc -c < "$DUMP" | tr -d ' ')
log "backup took ${BACKUP_S}s, ${DUMP_BYTES} bytes"

# --- Dữ liệu SAU backup = dữ liệu sẽ mất -----------------------------------
docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "INSERT INTO \"Orders\" (\"ProductId\",\"Quantity\",\"IdempotencyKey\",\"CreatedAt\")
   VALUES ($PID,3,'after-backup',now())"
AFTER_EPOCH=$(date +%s)
RPO_S=$((AFTER_EPOCH-BEFORE_EPOCH))
log "an order was written AFTER the backup; that is the data at risk"
log "measured RPO window: ${RPO_S}s"

# Chụp lại số liệu ĐÚNG trước khi phá hỏng. Invariant của restore là "đưa hệ
# thống về đúng trạng thái trước khi hỏng", không phải "đưa về một hằng số tôi
# tự đặt ra".
#
# Lần chạy đầu tôi so với hằng số 10 và nó BROKEN, dù restore hoàn toàn đúng —
# vì tôi insert order bằng SQL thô nên kho không bị trừ, và 10 vốn chưa bao
# giờ là trạng thái nhất quán. Đây là dạng lỗi nguy hiểm nhất của một bài test
# khôi phục: báo hỏng khi hệ thống đúng, và dễ dẫn tới "sửa" dữ liệu để làm
# test xanh.
read -r PRE_SOLD PRE_STOCK <<<"$(docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -F' ' -c \
  "SELECT COALESCE(SUM(o.\"Quantity\"),0), p.\"AvailableStock\"
   FROM \"Products\" p LEFT JOIN \"Orders\" o ON o.\"ProductId\"=p.\"Id\"
   WHERE p.\"Id\"=$PID GROUP BY p.\"Id\", p.\"AvailableStock\"")"
log "pre-damage state: sold=$PRE_SOLD stock=$PRE_STOCK"
DATA_LOSS=$((PRE_SOLD - AT_BACKUP_SOLD))
log "data written after the backup (the RPO exposure): ${DATA_LOSS} unit(s)"

# --- Phá hỏng, đo RTO --------------------------------------------------------
log "destroying the data, as if the database had been lost"
docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -q -c \
  "DELETE FROM \"Orders\" WHERE \"ProductId\"=$PID;
   UPDATE \"Products\" SET \"AvailableStock\"=0 WHERE \"Id\"=$PID"
# Đếm TRƯỚC khi xoá. Đếm sau khi DELETE thì luôn ra 0, tức là con số luôn
# đúng dù phục hồi có hỏng hay không — một chỉ số không bao giờ báo động.
log "destroyed $PRE_SOLD sold unit(s) and zeroed the stock"

R0=$(python3 -c 'import time;print(time.monotonic())')
log "restoring (timed)"
# -Fc + --clean --if-exists: pg_dump thuần | psql KHÔNG phục hồi được, vì mọi
# CREATE INDEX đều đụng "already exists" và dữ liệu cuối cùng không quay lại.
docker exec -i "$CONTAINER" pg_restore --clean --if-exists --no-owner \
  -U "$USER" -d "$DB" < "$DUMP" >/dev/null 2>&1
RC=$?
R1=$(python3 -c 'import time;print(time.monotonic())')
RTO_S=$(python3 -c "print(round($R1-$R0,2))")
log "restore took ${RTO_S}s (pg_restore rc=$RC)"

# --- Xác minh invariant, không chỉ "restore chạy xong" ----------------------
read -r SOLD STOCK <<<"$(docker exec "$CONTAINER" psql -U "$USER" -d "$DB" -t -A -F' ' -c \
  "SELECT COALESCE(SUM(o.\"Quantity\"),0), p.\"AvailableStock\"
   FROM \"Products\" p LEFT JOIN \"Orders\" o ON o.\"ProductId\"=p.\"Id\"
   WHERE p.\"Id\"=$PID GROUP BY p.\"Id\", p.\"AvailableStock\"")"
if [ "$SOLD" = "$AT_BACKUP_SOLD" ] && [ "$STOCK" = "$AT_BACKUP_STOCK" ]; then
  INV="RESTORED_TO_BACKUP_POINT_IN_TIME"
else
  INV="MISMATCH_EXPECTED_${AT_BACKUP_SOLD}_${AT_BACKUP_STOCK}"
fi
log "after restore: sold=$SOLD stock=$STOCK (expected sold=$AT_BACKUP_SOLD stock=$AT_BACKUP_STOCK) -> $INV"

cat > "$ART/gate2-rto-rpo.json" <<JSON
{
  "scenario": "GATE2-RTO-RPO",
  "database": "$IMAGE real container, repository EF migrations",
  "backup_seconds": $BACKUP_S,
  "restore_seconds": $RTO_S,
  "rpo_window_seconds": $RPO_S,
  "rpo_explanation": "orders written after the last successful backup; this is the data a restore would lose",
  "destroyed_units": $PRE_SOLD,
  "sold_after_restore": $SOLD,
  "stock_after_restore": $STOCK,
  "data_loss_units": $DATA_LOSS,
  "state_at_backup_sold": $AT_BACKUP_SOLD,
  "state_at_backup_stock": $AT_BACKUP_STOCK,
  "pre_damage_sold": $PRE_SOLD,
  "pre_damage_stock": $PRE_STOCK,
  "invariant_after_restore": "$INV",
  "dump_bytes": $DUMP_BYTES,
  "caveat": "Single local postgres restore, not a production cluster failover. It bounds the restore mechanism, not regional recovery."
}
JSON

log "written $ART/gate2-rto-rpo.json"
case "$INV" in RESTORED_TO_BACKUP_POINT_IN_TIME) exit 0 ;; *) exit 1 ;; esac
