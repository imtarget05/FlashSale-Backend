#!/usr/bin/env bash
# Measure two payment-service PostgreSQL query plans on a disposable local database.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../" && pwd)"
for required in python3 initdb pg_ctl createdb psql; do
  if ! command -v "$required" >/dev/null 2>&1; then
    echo "Missing required command: $required" >&2
    exit 2
  fi
done

pg_dir="$(mktemp -d /tmp/flashsale-query.XXXXXX)"
pg_port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
cleanup() {
  pg_ctl -D "$pg_dir" -m immediate stop >/dev/null 2>&1 || true
  BENCH_DIR="$pg_dir" python3 -c '
import os
import shutil
from pathlib import Path

path = Path(os.environ["BENCH_DIR"]).resolve()
if path.parent != Path("/tmp").resolve() or not path.name.startswith("flashsale-query."):
    raise SystemExit("Refusing to remove an unexpected database directory")
shutil.rmtree(path)
'
}
trap cleanup EXIT

initdb -D "$pg_dir" -A trust --no-instructions >/dev/null
pg_ctl -D "$pg_dir" -o "-h 127.0.0.1 -p $pg_port" -l "$pg_dir/postgres.log" start >/dev/null
createdb -h 127.0.0.1 -p "$pg_port" flashsale_query_test

psql -X -q -v ON_ERROR_STOP=1 -h 127.0.0.1 -p "$pg_port" \
  -d flashsale_query_test -f "$repo_dir/src/payment-service-java/docker/init-schema.sql"

psql -X -A -v ON_ERROR_STOP=1 -h 127.0.0.1 -p "$pg_port" -d flashsale_query_test <<'SQL'
SHOW server_version;
INSERT INTO payment_records (
  idempotency_key, payment_id, order_id, amount, currency, success,
  status, is_transient_error, processed_at
)
SELECT 'key-' || g, md5('payment-' || g)::uuid, g, 100.00, 'VND', true,
       'SUCCEEDED', false, now() - g * interval '1 second'
FROM generate_series(1, 100000) AS g;

INSERT INTO payment_outbox (
  id, idempotency_key, routing_key, payload, created_at,
  next_attempt_at, published_at
)
SELECT md5('outbox-' || g)::uuid, 'event-' || g, 'payment.processed', '{}',
       now() - (100000 - g) * interval '1 second',
       CASE WHEN g % 2 = 0 THEN now() - interval '1 minute'
            ELSE now() + interval '1 hour' END,
       CASE WHEN g % 10 = 0 THEN NULL ELSE now() END
FROM generate_series(1, 100000) AS g;

ANALYZE payment_records;
ANALYZE payment_outbox;
SELECT count(*) AS payment_rows FROM payment_records;
SELECT count(*) AS outbox_rows,
       count(*) FILTER (WHERE published_at IS NULL) AS pending_rows,
       count(*) FILTER (WHERE published_at IS NULL AND next_attempt_at <= now()) AS due_rows
FROM payment_outbox;

SELECT 'PAYMENT_LOOKUP_WITH_PRIMARY_KEY' AS query_case;
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM payment_records WHERE idempotency_key = 'key-50000';

SELECT 'OUTBOX_WITH_PARTIAL_INDEX' AS query_case;
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM payment_outbox
WHERE published_at IS NULL AND next_attempt_at <= now()
ORDER BY created_at ASC LIMIT 50 FOR UPDATE SKIP LOCKED;

-- This DDL runs only in the disposable database created above.
DROP INDEX ix_payment_outbox_pending;
ANALYZE payment_outbox;
SELECT 'OUTBOX_WITHOUT_PARTIAL_INDEX' AS query_case;
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM payment_outbox
WHERE published_at IS NULL AND next_attempt_at <= now()
ORDER BY created_at ASC LIMIT 50 FOR UPDATE SKIP LOCKED;
SQL
