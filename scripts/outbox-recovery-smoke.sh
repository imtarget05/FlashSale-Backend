#!/usr/bin/env bash
# LOCAL v2 / Phase 10: Transactional Outbox recovery verification (live, kind).
#
# Proves the three properties that were NOT true before the backoff/DLQ fix
# (docs/evidence/v2/outbox-inbox-2026-09-24.md recorded the gap):
#   A. A transient broker outage drains BY ITSELF once the broker returns —
#      no pod restart, no SQL by hand.
#   B. A longer outage dead-letters the row instead of hiding it: pending still
#      reports 0, but stuckCount must NOT be 0.
#   C. POST /api/outbox/requeue revives a dead-lettered row and it then drains.
#
# Requires: kubectl context kind-local-platform, ns flashsale, gateway or API
# reachable (see GATEWAY_URL / API_URL).
set -euo pipefail

NS=flashsale
GATEWAY="${GATEWAY_URL:-http://127.0.0.1:8088}"
HOST_HEADER="Host: flashsale.local"
API="${API_URL:-$GATEWAY}"
PASS=0
FAIL=0

check() {
  if [ "$2" = "$3" ]; then
    echo "  PASS: $1 (=$3)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $1 (expected $2, got $3)"
    FAIL=$((FAIL + 1))
  fi
}

contains() {
  if printf '%s' "$3" | grep -q -- "$2"; then
    echo "  PASS: $1 (contains '$2')"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $1 (missing '$2' in: $3)"
    FAIL=$((FAIL + 1))
  fi
}

field() { grep -o "\"$1\":[^,}]*" | head -1 | sed 's/.*://; s/"//g'; }

api() { curl -s --max-time 15 -H "$HOST_HEADER" "$API$1"; }

# The enqueue/requeue endpoints bind an optional JSON body, so an empty POST is
# rejected with 400 before it ever reaches the repository — always send `{}`.
api_post() {
  curl -s --max-time 15 -X POST -H "$HOST_HEADER" -H 'Content-Type: application/json' \
    -d '{}' "$API$1"
}

# Row-level truth straight from PostgreSQL — the API numbers are the claim,
# this is the proof.
psql_row() {
  kubectl exec -n "$NS" postgres-0 -- psql -U postgres -d FlashSaleDb -At -c "$1" 2>/dev/null | tr -d ' '
}

row_processed_flag() { psql_row "SELECT (\"ProcessedAt\" IS NOT NULL)::int FROM \"OutboxMessages\" WHERE \"Id\" = $1;"; }
row_dlq_flag() { psql_row "SELECT (\"DeadLetteredAt\" IS NOT NULL)::int FROM \"OutboxMessages\" WHERE \"Id\" = $1;"; }
row_retries() { psql_row "SELECT \"RetryCount\" FROM \"OutboxMessages\" WHERE \"Id\" = $1;"; }

# The current local event provider is Kafka. Scale the broker, not RabbitMQ.
# The trap below makes broker/Argo restoration mandatory even when an assertion fails.
ORIGINAL_AUTOMATED="$(kubectl get application flashsale -n argocd -o json | python3 -c 'import json,sys; print(json.dumps(json.load(sys.stdin)["spec"].get("syncPolicy",{}).get("automated")))')"
restore_runtime() {
  kubectl scale statefulset/kafka -n "$NS" --replicas=1 >/dev/null 2>&1 || true
  if [ "$ORIGINAL_AUTOMATED" != "None" ]; then
    kubectl patch application flashsale -n argocd --type merge \
      -p "{\"spec\":{\"syncPolicy\":{\"automated\":$ORIGINAL_AUTOMATED}}}" >/dev/null 2>&1 || true
  fi
}
trap restore_runtime EXIT
pause_autosync() {
  kubectl patch application flashsale -n argocd --type merge \
    -p '{"spec":{"syncPolicy":{"automated":null}}}' >/dev/null
}
broker_stop() {
  pause_autosync
  kubectl scale statefulset/kafka -n "$NS" --replicas=0
  kubectl wait --for=delete pod/kafka-0 -n "$NS" --timeout=60s
}
broker_start() {
  kubectl scale statefulset/kafka -n "$NS" --replicas=1
  kubectl wait --for=condition=Ready pod/kafka-0 -n "$NS" --timeout=180s
}

# Poll the DB until the row satisfies the given SQL predicate, or the deadline passes.
wait_flag() {
  local id="$1" kind="$2" timeout_s="$3"
  # Split from the line above on purpose: in a single `local` statement the
  # expansions run before the assignments, so `deadline` would read an unset
  # timeout_s and `set -u` aborts the whole run (found live).
  local deadline=$((SECONDS + timeout_s))
  while [ $SECONDS -lt $deadline ]; do
    if [ "$kind" = "processed" ]; then
      [ "$(row_processed_flag "$id")" = "1" ] && return 0
    else
      [ "$(row_dlq_flag "$id")" = "1" ] && return 0
    fi
    sleep 2
  done
  return 1
}

echo "=== LOCAL v2 / Phase 10: Outbox recovery verification ==="
echo "Gateway: $GATEWAY   API: $API"

echo ""
echo "--- Scenario A: transient outage must self-heal ---"
STARTED=$(api "/api/outbox/pending" | field "count")
echo "  baseline pending=$STARTED"

broker_stop
echo "  broker stopped"
ID_A=$(api_post "/api/outbox/enqueue" | field "id")
echo "  enqueued id=$ID_A while the broker is down"

sleep 7
PENDING_MID=$(api "/api/outbox/pending")
echo "  mid-outage: $PENDING_MID"
STUCK_MID=$(printf '%s' "$PENDING_MID" | field "stuckCount")
if [ "${STUCK_MID:-0}" -ge 1 ]; then
  echo "  PASS: mid-outage row is visible as stuck (stuckCount=$STUCK_MID), not silently pending=0"
  PASS=$((PASS + 1))
else
  echo "  FAIL: mid-outage row is invisible (stuckCount=${STUCK_MID:-none})"
  FAIL=$((FAIL + 1))
fi

BACKOFF=$(psql_row "SELECT (\"NextAttemptAt\" IS NOT NULL)::int FROM \"OutboxMessages\" WHERE \"Id\" = $ID_A;")
check "A backoff deadline was scheduled instead of burning every retry instantly" "1" "$BACKOFF"
NOT_YET=$(psql_row "SELECT (\"ProcessedAt\" IS NULL)::int FROM \"OutboxMessages\" WHERE \"Id\" = $ID_A;")
check "A row $ID_A is genuinely still undelivered mid-outage" "1" "$NOT_YET"
RETRIES_MID=$(row_retries "$ID_A")
echo "        (RetryCount=$RETRIES_MID after ~7s of outage — with a 2s/4s/8s schedule, not 5-in-one-second)"

broker_start
echo "  broker started; waiting for self-heal (no pod restart, no requeue, no SQL)"
if wait_flag "$ID_A" processed 90; then
  echo "  PASS: row $ID_A drained by itself after the broker returned (RetryCount=$(row_retries "$ID_A"))"
  PASS=$((PASS + 1))
else
  echo "  FAIL: row $ID_A never drained after the broker returned"
  FAIL=$((FAIL + 1))
fi

echo ""
echo "--- Scenario B: exhausted outage must be dead-lettered AND visible ---"
# Runtime contract: 5 x 5s bounded producer attempts + 2+4+8+16s backoff.
# Allow generous scheduling/DB overhead; the producer deadline is what makes this bounded.
broker_stop
echo "  broker stopped"
ID_B=$(api_post "/api/outbox/enqueue" | field "id")
echo "  enqueued id=$ID_B (dead-letter expected within 90s)"
if wait_flag "$ID_B" dlq 90; then
  echo "  PASS: row $ID_B reached the DLQ marker"
  PASS=$((PASS + 1))
else
  echo "  FAIL: row $ID_B never dead-lettered"
  FAIL=$((FAIL + 1))
fi

PENDING_BODY=$(api "/api/outbox/pending")
PENDING_COUNT=$(printf '%s' "$PENDING_BODY" | field "count")
STUCK_COUNT=$(printf '%s' "$PENDING_BODY" | field "stuckCount")
check "B pending count is 0 (nothing the dispatcher can act on)" "0" "$PENDING_COUNT"
if [ "${STUCK_COUNT:-0}" -ge 1 ]; then
  echo "  PASS: B stuckCount=$STUCK_COUNT — the old bug was pending=0 with NO other signal"
  PASS=$((PASS + 1))
else
  echo "  FAIL: B stuckCount=${STUCK_COUNT:-none} — the dead-lettered row is invisible"
  FAIL=$((FAIL + 1))
fi
contains "B /api/outbox/stuck reports at least one dead-lettered row" '"deadLettered":[1-9]' "$(api "/api/outbox/stuck")"

broker_start
echo "  broker started; recovery is intentionally race-safe"
# The dispatcher polls independently of the operator endpoint. Once Kafka is
# healthy it may drain a dead-lettered row before /requeue runs. Check the
# database first so a successful automatic recovery cannot be reported as a
# requeue failure merely because the HTTP response returned revived=0.
if wait_flag "$ID_B" processed 15; then
  echo "  PASS: dispatcher drained the dead-lettered row before requeue; requeue remained a no-op"
  PASS=$((PASS + 1))
else
  REQUEUED=$(api_post "/api/outbox/requeue" | field "revived")
  if [ "${REQUEUED:-0}" -ge 1 ]; then
    echo "  PASS: requeue revived $REQUEUED row(s)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: dead-lettered row survived, but requeue revived nothing"
    FAIL=$((FAIL + 1))
  fi
fi

if wait_flag "$ID_B" processed 60; then
  echo "  PASS: row $ID_B drained after broker recovery/requeue"
  PASS=$((PASS + 1))
else
  echo "  FAIL: row $ID_B did not drain after broker recovery/requeue"
  FAIL=$((FAIL + 1))
fi

echo ""
echo "--- Scenario C: consumer replay must not double-apply ---"
MID=$(python3 -c "import uuid;print(uuid.uuid4())")
FIRST=$(curl -s --max-time 10 -X POST -H "$HOST_HEADER" -H 'Content-Type: application/json' \
  -d "{\"messageId\":\"$MID\",\"consumerName\":\"recovery-smoke\"}" "$API/api/inbox/consume")
DUPES=0
for _ in 1 2 3 4; do
  R=$(curl -s --max-time 10 -X POST -H "$HOST_HEADER" -H 'Content-Type: application/json' \
    -d "{\"messageId\":\"$MID\",\"consumerName\":\"recovery-smoke\"}" "$API/api/inbox/consume")
  printf '%s' "$R" | grep -q '"deduplicated"' && DUPES=$((DUPES + 1))
done
contains "C first delivery is processed" '"processed":true' "$FIRST"
check "C four replays are all deduplicated" "4" "$DUPES"
ROWS=$(psql_row "SELECT count(*) FROM \"InboxMessages\" WHERE \"MessageId\" = '$MID';")
check "C exactly one inbox row exists for that MessageId" "1" "$ROWS"

echo ""
echo "--- Final state ---"
echo "  pending: $(api "/api/outbox/pending")"
echo "  stuck:   $(api "/api/outbox/stuck")"
RESIDUAL=$(psql_row "SELECT count(*) FROM \"OutboxMessages\" WHERE \"ProcessedAt\" IS NULL;")
check "No unprocessed outbox rows remain" "0" "$RESIDUAL"

echo ""
echo "=========================================================="
echo "Outbox recovery verification: $PASS Passed, $FAIL Failed"
echo "=========================================================="

[ "$FAIL" -eq 0 ] || exit 1
