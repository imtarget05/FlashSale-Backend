#!/usr/bin/env bash
# L5 — cold-start runbook for the LOCAL FlashSale overlay.
#
# WHY ORDERED APPLY (and why `kubectl apply -k` alone is wrong here):
#   On a FRESH database, three different workloads call EF MigrateAsync during
#   startup — the order-migrate Job (via `--migrate`), Order.Api (via
#   DatabaseInitializer) and Order.Worker (same). Applying the whole tree in one
#   shot lets them race, and the race corrupts the schema exactly as the Job's own
#   header predicts. Observed on the first attempt:
#       ERROR: relation "__EFMigrationsHistory" does not exist
#       ERROR: column "Category" of relation "Products" already exists
#   Argo CD orders this with sync-waves (annotations live in kustomization.yaml);
#   kubectl has no ordering, so this script applies the same waves explicitly.
#
# Usage:
#   ./bootstrap.sh            # ordered apply onto an existing namespace
#   ./bootstrap.sh --reset    # DESTRUCTIVE: delete+recreate the local namespace
#                             # (kind cluster only — never touches anything else)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
NS="${NS:-flashsale}"
K="${KUBECTL:-kubectl}"
WAIT="${WAIT:-300s}"

if [ "${1:-}" = "--reset" ]; then
  echo "== reset: deleting namespace ${NS} (local kind cluster only)"
  $K delete namespace "$NS" --ignore-not-found --wait=true --timeout=180s
fi

$K create namespace "$NS" --dry-run=client -o yaml | $K apply -f - >/dev/null
"${HERE}/create-local-secrets.sh"

RENDER="$(mktemp -t flashsale-local-XXXXXX.yaml)"
trap 'rm -f "$RENDER"' EXIT
$K kustomize "$HERE" > "$RENDER"

apply_docs() { # apply_docs <kind> [name]
  local kind="$1" name="${2:-}" out
  out="$(mktemp -t flashsale-doc-XXXXXX.yaml)"
  awk -v want_kind="$kind" -v want_name="$name" '
    function flush() {
      if (doc ~ ("kind: " want_kind "\n") && (want_name == "" || doc ~ ("name: " want_name "\n"))) printf "%s", doc
      doc = ""
    }
    /^---[[:space:]]*$/ { flush(); doc = "---\n"; next }
    { doc = doc $0 "\n" }
    END { flush() }
  ' "$RENDER" > "$out"
  if [ ! -s "$out" ]; then
    echo "ERROR: no rendered document matched kind=${kind} name=${name:-*}" >&2
    exit 1
  fi
  echo "-- apply ${kind}${name:+/${name}}"
  $K apply -f "$out" >/dev/null
  rm -f "$out"
}

echo "=== wave -3: data tier (source of truth first) ==="
apply_docs Service
apply_docs StatefulSet postgres
apply_docs StatefulSet rabbitmq
apply_docs Deployment redis
$K -n "$NS" rollout status statefulset/postgres --timeout="$WAIT"
$K -n "$NS" rollout status statefulset/rabbitmq --timeout="$WAIT"
$K -n "$NS" rollout status deployment/redis   --timeout="$WAIT"

echo "=== PreSync: schema migration (must complete before apps roll) ==="
$K -n "$NS" delete job order-migrate --ignore-not-found >/dev/null
apply_docs Job order-migrate
$K -n "$NS" wait --for=condition=complete job/order-migrate --timeout="$WAIT"
$K -n "$NS" logs job/order-migrate --tail=4

echo "=== wave 0: application workloads ==="
apply_docs Deployment order-api
apply_docs Deployment order-worker
$K -n "$NS" rollout status deployment/order-api    --timeout="$WAIT"
$K -n "$NS" rollout status deployment/order-worker --timeout="$WAIT"

echo "=== state ==="
$K -n "$NS" get pods -o wide
