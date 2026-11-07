#!/usr/bin/env bash
# Validate that all required Render environment variables are present (names only).
# Does NOT print values. Exit non-zero if any required key is missing/empty.
# Usage: ./scripts/validate-render-env.sh [preview|production]
set -euo pipefail

TARGET="${1:-preview}"
case "$TARGET" in
  preview|production) ;;
  *) echo "Usage: $0 [preview|production]" >&2; exit 1 ;;
esac

REQUIRED=(
  PORT SERVER_PORT SPRING_PROFILES_ACTIVE
  SPRING_DATASOURCE_URL SPRING_DATASOURCE_USERNAME SPRING_DATASOURCE_PASSWORD
  SPRING_DATA_REDIS_HOST SPRING_DATA_REDIS_PORT
  SPRING_RABBITMQ_HOST SPRING_RABBITMQ_PORT SPRING_RABBITMQ_USERNAME SPRING_RABBITMQ_PASSWORD
  OIDC_ISSUER_URI OIDC_AUDIENCE
)

MISSING=()
for k in "${REQUIRED[@]}"; do
  v="${!k:-}"
  if [[ -z "$v" ]]; then MISSING+=("$k"); fi
done

if [[ ${#MISSING[@]} -gt 0 ]]; then
  echo "Missing required Render env vars for $TARGET:" >&2
  for k in "${MISSING[@]}"; do echo "  - $k" >&2; done
  echo "Set them in the Render dashboard (Environment) before deploying." >&2
  exit 1
fi

echo "All required Render env vars present for $TARGET (values not shown)."
