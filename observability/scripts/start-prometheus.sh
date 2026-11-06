#!/bin/sh
set -eu

escape_sed_replacement() {
  printf '%s' "$1" | sed 's/[\\&|]/\\&/g'
}

sed \
  -e "s|__PAYMENT_READINESS_URL__|$(escape_sed_replacement "${PAYMENT_READINESS_URL:-http://host.docker.internal:8081/actuator/health/readiness}")|g" \
  /etc/prometheus/prometheus.yml.template > /tmp/prometheus.yml

exec /bin/prometheus \
  --config.file=/tmp/prometheus.yml \
  --storage.tsdb.path=/prometheus \
  --web.enable-lifecycle
