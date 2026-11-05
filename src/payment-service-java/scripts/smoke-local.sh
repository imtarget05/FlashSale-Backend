#!/usr/bin/env bash
set -euo pipefail

base_url="${SMOKE_BASE_URL:-http://127.0.0.1:8081}"
email="${DEMO_AUTH_EMAIL:-staff@flashsale.local}"
password="${DEMO_AUTH_PASSWORD:?Set DEMO_AUTH_PASSWORD to the local demo account password}"

new_uuid() {
	python3 -c 'import uuid; print(uuid.uuid4())'
}

make_login_body() {
	python3 -c 'import json,sys; print(json.dumps({"email":sys.argv[1],"password":sys.argv[2]}))' \
		"$email" "$password"
}

make_payment_body() {
	python3 -c 'import json,sys; print(json.dumps({"paymentId":sys.argv[1],"orderId":int(sys.argv[2]),"idempotencyKey":sys.argv[3],"amount":sys.argv[4],"currency":"USD"}))' \
		"$1" "$2" "$3" "$4"
}

status_code() {
	printf '%s' "$1" | sed -n '$p'
}

response_body() {
	printf '%s' "$1" | sed '$d'
}

expect_status() {
	local response="$1"
	local expected="$2"
	local actual
	actual="$(status_code "$response")"
	if [[ "$actual" != "$expected" ]]; then
		printf 'Expected HTTP %s, received HTTP %s\n' "$expected" "$actual" >&2
		exit 1
	fi
}

post_payment() {
	curl --silent --show-error --write-out '\n%{http_code}' \
		"$base_url/api/payments" \
		-H "Authorization: Bearer $token" \
		-H 'Content-Type: application/json' \
		--data "$1"
}

assert_payment_status() {
	printf '%s' "$1" | python3 -c \
		'import json,sys; assert json.load(sys.stdin)["status"] == sys.argv[1]' "$2"
}

wait_for_audit() {
	local key="$1"
	local expected_status="$2"
	local response=''
	for _ in $(seq 1 40); do
		response="$(curl --silent --show-error --write-out '\n%{http_code}' \
			"$base_url/api/audit/payments/$key" \
			-H "Authorization: Bearer $token")"
		if [[ "$(status_code "$response")" == 200 ]]; then
			break
		fi
		sleep 0.25
	done
	expect_status "$response" 200
	printf '%s' "$(response_body "$response")" | python3 -c \
		'import json,sys; event=json.load(sys.stdin); assert event["status"] == sys.argv[1]; assert "transactionId" not in event' \
		"$expected_status"
}

login_response="$(curl --silent --show-error --fail \
	"$base_url/api/auth/login" \
	-H 'Content-Type: application/json' \
	--data "$(make_login_body)")"
token="$(printf '%s' "$login_response" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')"
key="smoke-$(new_uuid)"
payment_id="$(new_uuid)"
success_body="$(make_payment_body "$payment_id" 99001 "$key" 10.01)"

success_response="$(post_payment "$success_body")"
expect_status "$success_response" 200
assert_payment_status "$(response_body "$success_response")" Succeeded

replay_response="$(post_payment "$success_body")"
expect_status "$replay_response" 200
first_transaction="$(printf '%s' "$(response_body "$success_response")" | python3 -c 'import json,sys; print(json.load(sys.stdin)["transactionId"])')"
replay_transaction="$(printf '%s' "$(response_body "$replay_response")" | python3 -c 'import json,sys; print(json.load(sys.stdin)["transactionId"])')"
if [[ "$first_transaction" != "$replay_transaction" ]]; then
	printf 'Idempotent replay returned a different transaction\n' >&2
	exit 1
fi

conflict_response="$(post_payment "$(make_payment_body "$payment_id" 99001 "$key" 99.99)")"
expect_status "$conflict_response" 409

decline_key="$key-decline"
decline_response="$(post_payment "$(make_payment_body "$(new_uuid)" 99002 "$decline_key" 10.02)")"
expect_status "$decline_response" 200
assert_payment_status "$(response_body "$decline_response")" Declined

timeout_key="$key-timeout"
timeout_response="$(post_payment "$(make_payment_body "$(new_uuid)" 99003 "$timeout_key" 10.03)")"
expect_status "$timeout_response" 504
assert_payment_status "$(response_body "$timeout_response")" TimedOut

wait_for_audit "$key" Succeeded
wait_for_audit "$decline_key" Declined
wait_for_audit "$timeout_key" TimedOut

printf 'PASS login; success; replay; 409 conflict; decline; timeout; confirmed-event Mongo audit projection\n'
