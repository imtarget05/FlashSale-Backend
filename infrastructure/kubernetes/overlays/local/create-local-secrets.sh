#!/usr/bin/env bash
# L5 — create the LOCAL `flashsale-secrets` Secret with freshly generated values.
#
# Why a script instead of a manifest: the platform repo's contract says secret
# material never lives in Git. This script keeps the *shape* of the contract
# reviewable in Git while the values are generated at runtime on this machine.
# No generated value is ever printed — only lengths and a fingerprint.
#
# Idempotent: if the Secret already exists it is left untouched unless --rotate
# is passed (rotation restarts things: PostgreSQL keeps its old password in the
# data directory, so --rotate on a running PG requires a DB password change too).
set -euo pipefail

NS="${NS:-flashsale}"
KUBECTL="${KUBECTL:-kubectl}"
MODE="${1:-}"

command -v openssl >/dev/null 2>&1 || { echo "FAIL: openssl not found" >&2; exit 1; }

if $KUBECTL get secret flashsale-secrets -n "$NS" >/dev/null 2>&1 && [ "$MODE" != "--rotate" ]; then
  echo "flashsale-secrets already exists in ${NS} — leaving it untouched (use --rotate to replace)."
  exit 0
fi

hex() { openssl rand -hex "$1"; }

PG_PASSWORD="$(hex 24)"
RABBIT_PASSWORD="$(hex 24)"
# 48 random bytes, base64-encoded: >= 32 bytes for HS256 whether the app treats
# the value as raw UTF-8 or decodes it as base64.
JWT_SIGNING_KEY="$(openssl rand -base64 48 | tr -d '\n')"

$KUBECTL create secret generic flashsale-secrets -n "$NS" \
  --from-literal=pg-password="${PG_PASSWORD}" \
  --from-literal=pg-connection="Host=postgres;Port=5432;Database=FlashSaleDb;Username=postgres;Password=${PG_PASSWORD};Maximum Pool Size=80" \
  --from-literal=redis-connection="redis:6379" \
  --from-literal=rabbitmq-password="${RABBIT_PASSWORD}" \
  --from-literal=rabbitmq-connection="amqp://flashsale:${RABBIT_PASSWORD}@rabbitmq:5672" \
  --from-literal=jwt-signing-key="${JWT_SIGNING_KEY}" \
  --dry-run=client -o yaml | $KUBECTL apply -f - >/dev/null

echo "flashsale-secrets applied in namespace ${NS}."
echo "  pg-password        : ${#PG_PASSWORD} chars"
echo "  rabbitmq-password  : ${#RABBIT_PASSWORD} chars"
echo "  jwt-signing-key    : ${#JWT_SIGNING_KEY} chars (fingerprint $(printf '%s' "$JWT_SIGNING_KEY" | shasum -a 256 | cut -c1-12))"
echo "  values are NOT printed and NOT committed."
