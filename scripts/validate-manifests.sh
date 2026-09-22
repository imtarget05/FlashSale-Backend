#!/usr/bin/env bash
# Validate the RENDERED prod overlay, not just the source files.
#
# This gate exists because of a real Phase 7A/7C finding: CI built, scanned,
# pushed and digest-verified the order-api image, and the release was green —
# while `kubectl kustomize overlays/prod` still emitted
# `acrflashsale-placeholder.azurecr.io/order-api:latest`, because the overlay's
# `images:[].name` did not match the base image name and Kustomize silently
# skipped the transform. Nothing in the pipeline rendered the manifest, so
# nothing could see it. Image-level proofs do not prove deployability.
#
# Runs on any machine with kubectl (kustomize is built in) — no cluster needed.
set -euo pipefail

cd "$(dirname "$0")/.."
OVERLAY=infrastructure/kubernetes/overlays/prod

# Usage: validate-manifests.sh [pre-rendered.yaml]
#
# With no argument, this script renders the prod overlay itself — the mode CI
# runs. With a file argument, it validates THAT file instead, which is how
# scripts/test-validate-manifests.sh proves each assertion can actually fail.
# Without this seam, testing the tests would need a cluster (or a kubectl able to
# render mutated fixtures) and the assertions would stay unproven.
INPUT_FILE="${1:-}"

RENDERED=$(mktemp)
trap 'rm -f "$RENDERED"' EXIT

fail=0
err() { echo "FAIL: $*" >&2; fail=1; }

if [ -n "$INPUT_FILE" ]; then
  [ -f "$INPUT_FILE" ] || { echo "FAIL: no such rendered file: $INPUT_FILE" >&2; exit 1; }
  cp "$INPUT_FILE" "$RENDERED"
  echo "== validating pre-rendered input: $INPUT_FILE =="
else
  echo "== render $OVERLAY =="
  if ! kubectl kustomize "$OVERLAY" >"$RENDERED" 2>/tmp/kustomize.err; then
    cat /tmp/kustomize.err >&2
    echo "FAIL: kustomize render failed" >&2
    exit 1
  fi
fi
echo "rendered $(grep -c '^kind: ' "$RENDERED") objects"

# Extract one rendered document by Kind + metadata.name.
# Needed because a repo-wide grep is NOT a per-workload assertion: an early
# version of this script checked `grep -q 'key: rabbitmq-connection'`, which
# passed even with the env var deleted from order-api, because order-worker
# also references that key. A check that cannot fail is worse than no check —
# it manufactures false confidence, which is the exact Phase 7C bug class.
doc_of() {
  awk -v kind="$1" -v name="$2" '
    { doc[n] = doc[n] $0 "\n" }
    /^---$/ { n++ }
    END {
      for (i = 0; i <= n; i++)
        if (doc[i] ~ ("(^|\n)kind: " kind "(\n|$)") &&
            doc[i] ~ ("(^|\n)  name: " name "(\n|$)")) { printf "%s", doc[i]; found = 1; exit }
      exit !found
    }' "$RENDERED"
}


echo "== assertions =="
# 1. No placeholder registry may survive into rendered output.
grep -q 'placeholder' "$RENDERED" \
  && err "rendered manifest still contains 'placeholder' (image override did not apply)"

# 2. Mutable tags are forbidden (ADR-011).
grep -E 'image: [^ ]+:latest$' "$RENDERED" >/dev/null \
  && err "rendered manifest pins an image to :latest"

# 3. Our own images must come from the real ACR, pinned to a 40-char SHA. This
#    is a PER-DOCUMENT check on purpose: a repo-wide grep is satisfied by ANY
#    one image line, so when the migration Job (same artifact as the API) still
#    carries the right pin, a wrong-registry API image slips through and the
#    rollout ImagePullBackOffs with a green gate. The api_wrong_registry
#    mutation test exists because the first version of this rule had that hole.
#    Workload -> expected IMAGE REPO (not the workload name): the Job runs the
#    API artifact, so order-migrate maps to order-api.
pin_check() { kind=$1; name=$2; repo=$3
  D=$(doc_of "$kind" "$name") || { err "rendered manifest has no ${kind}/${name}"; return; }
  printf '%s' "$D" | grep -Eq "^[[:space:]]+image: acrflashsalep6\\.azurecr\\.io/${repo}:[0-9a-f]{40}[[:space:]]*$" \
    || err "${kind}/${name} is not pinned to acrflashsalep6.azurecr.io/${repo}:<40-char sha>"
}
pin_check Deployment order-api order-api
pin_check Deployment order-worker order-worker
pin_check Job order-migrate order-api

# 4. Both workloads must exist: the async flow is API -> queue -> worker, and a
#    manifest set without the worker cannot satisfy the Phase 7C gate. The Job is
#    in the same list because the migration contract is part of the desired state,
#    not an operator ritual that lives outside git.
for kind_name in "Deployment/order-api" "Deployment/order-worker" "Job/order-migrate"; do
  res="${kind_name%%/*}"; nm="${kind_name##*/}"
  awk -v r="$res" -v n="$nm" '
    /^kind: /   { k = $2 }
    /^  name: / { if (k == r && $2 == n) found = 1 }
    END         { exit !found }' "$RENDERED" \
    || err "rendered manifest has no ${kind_name}"
done

# 5. The data tier must be in-cluster and unreachable from outside.
grep -q 'type: LoadBalancer' "$RENDERED" \
  && err "a Service is exposed as LoadBalancer (public IP) — data tier must stay ClusterIP"
grep -q 'containerPort: 15672' "$RENDERED" \
  && err "RabbitMQ management port is exposed"
for dep in postgres redis rabbitmq; do
  grep -Eq "^  name: ${dep}$" "$RENDERED" || err "rendered manifest has no ${dep}"
done


# 6. Each app container must receive the connection strings it actually needs.
API_DOC=$(doc_of Deployment order-api) || API_DOC=""
WORKER_DOC=$(doc_of Deployment order-worker) || WORKER_DOC=""

if [ -n "$API_DOC" ]; then
  for kv in "ConnectionStrings__DefaultConnection:pg-connection" \
            "ConnectionStrings__Redis:redis-connection" \
            "ConnectionStrings__RabbitMQ:rabbitmq-connection" \
            "Auth__Jwt__SigningKey:jwt-signing-key"; do
    env_name=${kv%%:*}; secret_key=${kv##*:}
    printf '%s' "$API_DOC" | grep -q "name: ${env_name}$" \
      || err "order-api is missing ${env_name} (the app reads ConnectionStrings; without it the API silently falls back to localhost)"
    printf '%s' "$API_DOC" | grep -q "key: ${secret_key}$" \
      || err "order-api does not reference secret key ${secret_key}"
  done
  # Provider must be pinned explicitly, never inferred (ADR-005).
  printf '%s' "$API_DOC" | grep -q "name: Messaging__Provider$" \
    || err "order-api does not set Messaging__Provider explicitly"
fi

if [ -n "$WORKER_DOC" ]; then
  for env_name in ConnectionStrings__DefaultConnection ConnectionStrings__RabbitMQ; do
    printf '%s' "$WORKER_DOC" | grep -q "name: ${env_name}$" \
      || err "order-worker is missing ${env_name}"
  done
  printf '%s' "$WORKER_DOC" | grep -q "name: Messaging__Provider$" \
    || err "order-worker does not set Messaging__Provider explicitly"
  # The worker consumes queues; it must not listen on a port.
  printf '%s' "$WORKER_DOC" | grep -q "containerPort:" \
    && err "order-worker declares a containerPort but should expose nothing"
fi

# 7. The migration Job must be a migration, not a second API instance: it runs
#    --migrate, and it must depend on PostgreSQL ONLY. A Job that also needs the
#    brokers can never run before them, which inverts the ordering the whole
#    contract exists to guarantee.
MIGRATE_DOC=$(doc_of Job order-migrate) || MIGRATE_DOC=""
if [ -n "$MIGRATE_DOC" ]; then
  # NB: match the RENDERED form. Kustomize/PyYAML normalize `- "--migrate"` to
  # `- --migrate`, so asserting on the quoted source form fails against a
  # perfectly good manifest — the same class of bug as grepping source files for
  # a value the overlay rewrites.
  printf '%s' "$MIGRATE_DOC" | grep -Eq '^[[:space:]]*- --migrate$' \
    || err "Job/order-migrate does not pass --migrate (it would start a full API)"
  printf '%s' "$MIGRATE_DOC" | grep -q "key: pg-connection$" \
    || err "Job/order-migrate has no PostgreSQL connection string"
  # redis-connection and jwt-signing-key are equally forbidden: the Job must not
  # need the cache, and it must not need auth config for the same reason — a
  # signing key the Job is asked to provide is a signing key the migration path
  # would end up validating (ADR-013 §2).
  for forbidden in rabbitmq-connection redis-connection jwt-signing-key; do
    printf '%s' "$MIGRATE_DOC" | grep -q "key: ${forbidden}$" \
      && err "Job/order-migrate depends on ${forbidden}; migrations must need Postgres only"
  done
  # ttlSecondsAfterFinished keeps a finished Job from pinning pods forever.
  printf '%s' "$MIGRATE_DOC" | grep -q "ttlSecondsAfterFinished:" \
    || err "Job/order-migrate has no ttlSecondsAfterFinished (pods are never garbage collected)"
fi

# 8. Only services that listen may be fronted by a Service.
SERVICES=$(awk '/^kind: Service$/{s=1} s&&/^  name: /{print $2; s=0}' "$RENDERED" | sort | tr '\n' ' ')
echo "services rendered: ${SERVICES}"
case " ${SERVICES}" in
  *" order-worker "*) err "order-worker has a Service but exposes no port";;
esac

# 8. Cross-reference the decoded objects with no cluster and no kubeconfig.
#    Deliberately NOT `kubectl apply/create --dry-run=client`: both still run API
#    discovery (GET /api, /openapi/v2) so kubectl can map Kind -> GVR, so against
#    a stopped cluster they fail on DNS, and they only appear to pass while
#    kubectl's cached discovery document is still fresh. A gate that flips with
#    cache expiry is not a gate. See scripts/validate-objects.py.
if ./scripts/validate-objects.py "$RENDERED"; then
  :
else
  err "object cross-reference failed (see FAIL lines above)"
fi

if [ "$fail" -ne 0 ]; then
  echo "== RESULT: FAIL ==" >&2
  exit 1
fi
echo "== RESULT: PASS (rendered prod overlay is deployable) =="
