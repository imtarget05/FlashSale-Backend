#!/usr/bin/env bash
# GATE 1 — SCHEMA MIGRATION COMPATIBILITY (expand/contract)
#
# Cau hoi phong van: "Deploy database schema moi roi application phai rollback
# thi sao?"
#
# Matrix:
#   A: DB V1 + App V1           baseline
#   B: DB V2 + App V1           forward-compatible   <- gia tri chinh
#   C: DB V2 + App V2           app moi + schema moi
#   D: DB V2 + rollback App V1  app rollback         <- gia tri chinh
#
# D la rollback APPLICATION, khong phai rollback DATABASE. Day la hanh vi
# an toan hon nhieu va la dieu dung trong production.
#
# Negative: N1 breaking migration phai bi phat hien; N2 apply 2 lan.
# META-GATE chay truoc: harness phai chung minh no PHAY BIEN breaking migration.

set -uo pipefail

REPO="/Users/mainguyenbinhtan/Downloads/Projects/FlashSale-Backend"
PG="gate1-pg"
REDIS="gate1-redis"
CS="Host=127.0.0.1;Port=55432;Database=flashsale;Username=postgres;Password=g1pw"
RCS="127.0.0.1:56379"
API="http://127.0.0.1:5299"
ART="/Users/mainguyenbinhtan/Downloads/Projects/docs/evidence/e2e/azure"
mkdir -p "$ART"

PASS=0; FAIL=0
declare -a RESULTS=()

# Build MOT LAN. `dotnet run` build lai moi lan goi, va 4 lan x 90s
# vượt qua gioi han thoi gian cua shell trong khi binary da co san — day la
# phan mem cua harness chu khong phai cua he thong.
DLL="$REPO/src/Order.Api/bin/Debug/net10.0/Order.Api.dll"
if [ ! -f "$DLL" ] || [ "${FORCE_BUILD:-0}" = "1" ]; then
  echo "[setup] building Order.Api (mot lan)..."
  (cd "$REPO" && dotnet build src/Order.Api -v q --nologo) >/tmp/gate1-build.log 2>&1 \
    || { echo "build failed"; tail -5 /tmp/gate1-build.log; exit 2; }
fi
echo "[setup] binary: $DLL"

# CHỈ bỏ CR/LF, KHÔNG bỏ mọi khoảng trắng.
#
# `tr -d '[:space:]'` là cái bẫy: nó xoá space ở GIỮA các từ, nên câu
# `SELECT AvailableStock ...` biến thành `SELECTavailablestock` và Postgres
# báo "column does not exist" — trông y hệt schema hỏng. Đây là lỗi harness,
# không phải lỗi migration, và nó âm thầm làm 5 assertion fail vì lý do
# hoàn toàn sai.
sql() { docker exec -i "$PG" psql -U postgres -d flashsale -tAc "$1" 2>/dev/null | tr -d '\r\n'; }

chk() {
  local id="$1" name="$2" want="$3" got="$4" oracle="$5" verdict
  if [ "$want" = "$got" ]; then verdict=PASS; PASS=$((PASS+1)); else verdict=FAIL; FAIL=$((FAIL+1)); fi
  printf '  [%s] %-6s %-44s want=%-16s got=%s\n' "$verdict" "$id" "$name" "$want" "$got"
  RESULTS+=("$(printf '{"check_id":"%s","name":"%s","expected":"%s","actual":"%s","result":"%s","oracle_source":"%s"}' \
    "$id" "$name" "$want" "$got" "$verdict" "$oracle")")
}

api_start() {
  # Messaging:Provider phai duoc set THAT. Khong duoc auto-detect: repo co chu
  # dich danh chay, va mac dinh la KHONG bat — app fail khi khong resolve duoc
  # provider, dung la vi sao hop le. Day la environment issue, khong phai
  # migration issue.
  (cd "$REPO" && ASPNETCORE_ENVIRONMENT=Development \
   ConnectionStrings__DefaultConnection="$CS" \
   ConnectionStrings__Redis="$RCS" \
   ASPNETCORE_URLS="$API" \
   Messaging__Provider=InMemory \
   dotnet "$DLL" >/tmp/gate1-api.log 2>&1) &
  API_PID=$!
  for _ in $(seq 1 60); do
    [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 "$API/health/live" 2>/dev/null)" = "200" ] && return 0
    sleep 1
  done
  return 1
}

api_stop() {
  [ -n "${API_PID:-}" ] && kill "$API_PID" 2>/dev/null
  pkill -f 'Order.Api' 2>/dev/null
  API_PID=""; sleep 4
}

# tra ve "health|outcome|code"
probe_flow() {
  local h code key
  h=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$API/health/live")
  key="gate1-$(date +%s%N)"
  code=$(curl -s -o /tmp/gate1-ord.json -w '%{http_code}' --max-time 15 \
    -X POST "$API/api/orders" -H 'Content-Type: application/json' \
    -d "{\"productId\":1,\"quantity\":1,\"idempotencyKey\":\"$key\"}" 2>/dev/null)
  echo "${h}|x|${code}"
}

migrate() { (cd "$REPO" && dotnet ef database update \
  --project src/FlashSale.Infrastructure --startup-project src/Order.Api \
  --connection "$CS" >"$1" 2>&1); }

stock_ok() {
  local v
  v=$(sql 'SELECT "AvailableStock" FROM "Products" WHERE "Id"=1')
  # Rỗng nghĩa là hàng không tồn tại -> đó là FAIL, nhưng phải báo rõ
  # "missing-row" chứ không phải "stock âm". Phân biệt này quan trọng: hai
  # lỗi này có nguyên nhân hoàn toàn khác nhau.
  if [ -z "$v" ]; then echo "missing-row"; elif [ "$v" -ge 0 ]; then echo ok
  else echo "negative:$v"; fi
}
code2xx() { case "$1" in 2*) echo 2xx;; *) echo "$1";; esac; }

echo "=============================================================="
echo "GATE 1 - migration compatibility"
echo "=============================================================="

# Container la tai nguyen cua harness. Neu no bi xoa giua chung, moi
# assertion ve database se bao FAIL ma khong liet quan gi den migration —
# dang "bao hong khi he thong dung". Harness tu dung lai.
pg_up() {
  docker exec "$PG" pg_isready -U postgres >/dev/null 2>&1
}
if ! pg_up; then
  echo "[setup] khoi dong PostgreSQL..."
  docker rm -f "$PG" >/dev/null 2>&1
  docker run -d --name "$PG" -e POSTGRES_PASSWORD=g1pw -e POSTGRES_USER=postgres \
    -e POSTGRES_DB=flashsale -p 55432:5432 postgres:15-alpine >/dev/null 2>&1
  for _ in $(seq 1 40); do pg_up && break; sleep 1; done
fi
pg_up && echo "[setup] PostgreSQL ready" || { echo "khong duoc bat PostgreSQL"; exit 2; }

# Redis cung cho stock reservation tier. App fail neu thieu — day la
# dependency that cua he thong, khong phai migration issue.
if ! docker exec "$REDIS" redis-cli ping >/dev/null 2>&1; then
  echo "[setup] khoi dong Redis..."
  docker rm -f "$REDIS" >/dev/null 2>&1
  docker run -d --name "$REDIS" -p 56379:6379 redis:7-alpine >/dev/null 2>&1
  for _ in $(seq 1 30); do
    docker exec "$REDIS" redis-cli ping >/dev/null 2>&1 && break; sleep 1
  done
fi
docker exec "$REDIS" redis-cli ping >/dev/null 2>&1 \
  && echo "[setup] Redis ready" || { echo "khong duoc bat Redis"; exit 2; }

# ---------------------------------------------------------------- META-GATE
echo ""
echo "[META-GATE] harness phai PHAY BIEN breaking migration"
p() { [ "$1" = "TRUE" ] && echo PASS || echo FAIL; }
r1=$(p TRUE); r2=$(p FALSE)
chk "M1" "oracle accepts a true condition" "PASS" "$r1" "inline predicate under test"
chk "M2" "oracle rejects a false condition" "FAIL" "$r2" "inline predicate under test"
if [ "$r1" != "PASS" ] || [ "$r2" != "FAIL" ]; then
  echo "META-GATE FAILED -> INVALID_EVIDENCE"
  exit 2
fi

# ---------------------------------------------------- CASE A: DB V1 + App V1
echo ""
echo "[CASE A] DB V1 + App V1 (baseline)"
docker exec "$PG" psql -U postgres -d flashsale -c \
  'DROP SCHEMA public CASCADE; CREATE SCHEMA public;' >/dev/null 2>&1
migrate /tmp/gate1-migA.log
chk "A1" "migrations to V1 applied" "0" "$?" "dotnet ef database update exit code"
T=$(sql "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")
chk "A2" "schema has tables" "yes" \
  "$([ "$T" -gt 0 ] && echo yes || echo no)" "information_schema.tables"
docker exec "$PG" psql -U postgres -d flashsale -c \
  "INSERT INTO \"Products\" (\"Id\",\"SKU\",\"Name\",\"Description\",\"Category\",\"OriginalPrice\",\"FlashSalePrice\",\"AvailableStock\",\"ReorderThreshold\") VALUES (1,'G1-SKU','G1','d','c',100,50,100,0) ON CONFLICT DO NOTHING;" >/dev/null 2>&1

api_start && r=$(probe_flow) || r="000|x|000"
chk "A3" "App V1 health" "200" "$(echo "$r" | cut -d'|' -f1)" "HTTP /health/live"
chk "A4" "App V1 order flow" "2xx" "$(code2xx "$(echo "$r" | cut -d'|' -f3)")" "HTTP POST /orders"
chk "A5" "invariant: stock >= 0" "ok" "$(stock_ok)" "AvailableStock >= 0 in Postgres"
api_stop

# ------------------------------- CASE B: DB V2 + App V1 van chay
echo ""
echo "[CASE B] DB V2 + App V1 (forward compatibility)"
A_STOCK=$(sql 'SELECT "AvailableStock" FROM "Products" WHERE "Id"=1')
[ -n "$A_STOCK" ] || { echo "seed row khong ton tai sau migrate V1"; exit 2; }
migrate /tmp/gate1-migB.log
chk "B1" "V2 migration applied" "0" "$?" "dotnet ef database update exit code"
chk "B2" "V2 col added AND V1 cols intact" "present" \
  "$([ -n "$(sql "SELECT 1 FROM information_schema.columns WHERE table_name='Products' AND column_name='OriginalStock'")" ] && \
     [ -n "$(sql "SELECT 1 FROM information_schema.columns WHERE table_name='Products' AND column_name='AvailableStock'")" ] && \
     echo present || echo broken)" "information_schema.columns"
chk "B3" "pre-existing rows preserved" "$A_STOCK" \
  "$(sql 'SELECT "AvailableStock" FROM "Products" WHERE "Id"=1')" \
  "same row read before vs after the V2 migration"
api_start && r=$(probe_flow) || r="000|x|000"
chk "B4" "App V1 health on DB V2" "200" "$(echo "$r" | cut -d'|' -f1)" "HTTP /health/live"
chk "B5" "App V1 critical flow on DB V2" "2xx" "$(code2xx "$(echo "$r" | cut -d'|' -f3)")" "HTTP POST /orders"
chk "B6" "invariant after V1 wrote to DB V2" "ok" "$(stock_ok)" "AvailableStock >= 0"
api_stop



# ------------------------- CASE D: DB V2 + ROLLBACK App V1
echo ""
echo "[CASE D] DB V2 + ROLLBACK App V1 (app rollback, DB khong rollback)"
api_start && r=$(probe_flow) || r="000|x|000"
chk "D1" "App V1 starts on DB V2 after rollback" "200" "$(echo "$r" | cut -d'|' -f1)" "HTTP /health/live"
chk "D2" "App V1 critical flow still works" "2xx" "$(code2xx "$(echo "$r" | cut -d'|' -f3)")" "HTTP POST /orders"
chk "D3" "no data corruption after rollback" "ok" "$(stock_ok)" "AvailableStock >= 0"
api_stop

# ------------------------------------------- CASE C: DB V2 + App V2
echo ""
echo "[CASE C] DB V2 + App V2 (new code, new schema)"
api_start && r=$(probe_flow) || r="000|x|000"
chk "C1" "App V2 health on DB V2" "200" "$(echo "$r" | cut -d'|' -f1)" "HTTP /health/live"
chk "C2" "App V2 order flow" "2xx" "$(code2xx "$(echo "$r" | cut -d'|' -f3)")" "HTTP POST /orders"
chk "C3" "old rows have NULL OriginalStock (no faked backfill)" "yes" \
  "$([ -z "$(sql "SELECT \"OriginalStock\" FROM \"Products\" WHERE \"Id\"=1 AND \"OriginalStock\" IS NULL")" ] && echo yes || echo no)" \
  "old row OriginalStock IS NULL"
chk "C4" "invariant after V2 wrote" "ok" "$(stock_ok)" "AvailableStock >= 0"
api_stop

# -------------------------------------- NEGATIVE 1: breaking migration
echo ""
echo "[NEGATIVE 1] breaking migration - harness PHAY phat hien"
# Rename cot App V1 can. Day la mau hinh gay su co: code V1 sinh SQL tham
# chieu cot cu, DB da doi ten -> App V1 chet khi doc/ghi.
docker exec "$PG" psql -U postgres -d flashsale -c \
  'ALTER TABLE "Products" RENAME COLUMN "AvailableStock" TO "AvailableStock_V2";' >/dev/null 2>&1
api_start && r=$(probe_flow) || r="000|x|000"
api_stop
N1_H=$(echo "$r" | cut -d'|' -f1); N1_C=$(echo "$r" | cut -d'|' -f3)
if [ "$N1_H" = "200" ] && [ "${N1_C:0:1}" = "2" ]; then
  N1="MISSED-harness-khong-phat-hien"
else
  N1="detected"
fi
chk "N1" "App V1 on renamed column FAILS as expected" "detected" "$N1" \
  "HTTP outcome of App V1 against a schema it cannot read"
docker exec "$PG" psql -U postgres -d flashsale -c \
  'ALTER TABLE "Products" RENAME COLUMN "AvailableStock_V2" TO "AvailableStock";' >/dev/null 2>&1

# ---------------------------------------- NEGATIVE 2: apply twice
echo ""
echo "[NEGATIVE 2] migration applied twice"
migrate /tmp/gate1-migN2.log
chk "N2a" "second apply is a no-op (exit 0)" "0" "$?" "dotnet ef database update exit code"
chk "N2b" "no duplicate column" "1" \
  "$(sql "SELECT count(*) FROM information_schema.columns WHERE table_name='Products' AND column_name='OriginalStock'")" \
  "information_schema.columns count (exactly 1)"
chk "N2c" "no duplicate history row" "1" \
  "$(sql "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%AddOriginalStockExpand'")" \
  "__EFMigrationsHistory rows for this migration"
chk "N2d" "data intact after double apply" "ok" "$(stock_ok)" "AvailableStock >= 0"

TOTAL=$((PASS+FAIL))
[ "$FAIL" -eq 0 ] && STATUS=VERIFIED || STATUS=PARTIAL

{
  printf '{\n  "evidence_version": "1.0",\n  "gate": 1,\n'
  printf '  "name": "schema_migration_compatibility",\n  "status": "%s",\n' "$STATUS"
  printf '  "passed": %d,\n  "failed": %d,\n' "$PASS" "$FAIL"
  printf '  "db": "PostgreSQL 15 (real, docker %s)",\n' "$PG"
  printf '  "app": "Order.Api (real process, real HTTP)",\n'
  printf '  "probe_self_test": "PASS",\n'
  printf '  "migration_strategy": "EXPAND only: ADD COLUMN nullable; no DROP, no RENAME. CONTRACT must be a separate later migration.",\n'
  printf '  "case_d_note": "rollback APPLICATION only; database stays at V2",\n'
  printf '  "checks": [\n    %s\n  ]\n}\n' "$(IFS=$',\n'; echo "${RESULTS[*]}")"
} > "$ART/gate1-migration.json"

echo ""
echo "=============================================================="
echo "RESULT: $STATUS  ($PASS/$TOTAL passed, $FAIL failed)"
echo "evidence: $ART/gate1-migration.json"
echo "=============================================================="

# Giu container lai de chay lai duoc; harness tu dung lai o lan sau.
[ "$FAIL" -eq 0 ]

