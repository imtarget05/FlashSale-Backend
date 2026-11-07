#!/usr/bin/env bash
# Unit tests for scripts/render-deploy.sh.
# Plain bash assertions (bats/shunit2 not installed).
#
# Behaviours covered:
#   - invalid target exits non-zero
#   - missing RENDER_API_KEY exits non-zero
#   - missing service id exits non-zero
#   - Render HTTP 4xx/5xx exits non-zero (fail on error status)
#   - Render HTTP 2xx exits zero

set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT="$ROOT/scripts/render-deploy.sh"

PASS=0
FAIL=0

assert_exit() {
  local desc="$1" expected="$2"; shift 2
  set +e
  env "$@" >/dev/null 2>&1
  local code=$?
  set -e
  if [[ "$code" -eq "$expected" ]]; then
    echo "PASS: $desc (exit $code)"
    PASS=$((PASS + 1))
  else
    echo "FAIL: $desc (expected exit $expected, got $code)"
    FAIL=$((FAIL + 1))
  fi
}

# --- Mock curl that renders a controllable HTTP status code ---------------
# render-deploy.sh calls: curl -sS -o "$TMP_BODY" -w "%{http_code}" <url> ...
# So stdout of curl is the HTTP status code; -o writes the body to a file.
MOCK_DIR="$(mktemp -d)"
trap 'rm -rf "$MOCK_DIR"' EXIT

cat > "$MOCK_DIR/curl" <<'MOCK'
#!/usr/bin/env bash
HTTP_CODE="${MOCK_HTTP_CODE:-200}"
BODY="${MOCK_BODY:-ok}"
prev=""
for arg in "$@"; do
  if [[ "$prev" == "-o" ]]; then
    printf '%s' "$BODY" > "$arg"
  fi
  prev="$arg"
done
printf '%s' "$HTTP_CODE"
MOCK
chmod +x "$MOCK_DIR/curl"

PATH="$MOCK_DIR:$PATH"

# 1. invalid target -> exit 1
assert_exit "invalid target exits 1" 1 bash "$SCRIPT" invalid

# 2. missing RENDER_API_KEY -> exit 1
assert_exit "missing API key exits 1" 1 env -u RENDER_API_KEY RENDER_PREVIEW_SERVICE_ID=srv bash "$SCRIPT" preview

# 3. missing preview service id -> exit 1
assert_exit "missing preview service id exits 1" 1 env RENDER_API_KEY=key bash "$SCRIPT" preview

# 4. Render HTTP 404 -> exit non-zero (fail on error status)
assert_exit "HTTP 404 exits non-zero" 1 env RENDER_API_KEY=key RENDER_PREVIEW_SERVICE_ID=srv MOCK_HTTP_CODE=404 MOCK_BODY='{"error":"not found"}' bash "$SCRIPT" preview

# 5. Render HTTP 500 -> exit non-zero
assert_exit "HTTP 500 exits non-zero" 1 env RENDER_API_KEY=key RENDER_PREVIEW_SERVICE_ID=srv MOCK_HTTP_CODE=500 MOCK_BODY='{"error":"internal"}' bash "$SCRIPT" production

# 6. Render HTTP 200 -> exit zero
assert_exit "HTTP 200 exits zero" 0 env RENDER_API_KEY=key RENDER_PREVIEW_SERVICE_ID=srv MOCK_HTTP_CODE=200 MOCK_BODY='{"id":"dep-1"}' bash "$SCRIPT" preview

echo
echo "Results: $PASS passed, $FAIL failed"
[[ "$FAIL" -eq 0 ]]
