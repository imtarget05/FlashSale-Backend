#!/usr/bin/env python3
"""Kill the local demo API after its committed response reaches a buffering proxy.

The proxy deliberately never forwards that response to the client. A retry must
return the committed payment without creating a second payment or outbox row.
This is a local fault drill, not a production fault-injection endpoint.
"""

import http.client
import json
import os
import socket
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse


BASE_URL = os.environ.get("PAYMENT_BASE_URL", "http://127.0.0.1:18081")
API_CONTAINER = os.environ.get("PAYMENT_CONTAINER", "flashsale-payment-service-java")
DB_CONTAINER = os.environ.get("POSTGRES_CONTAINER", "flashsale-codex-bench-postgres-1")
DEMO_PASSWORD = os.environ.get("DEMO_AUTH_PASSWORD")


def request_json(url, payload, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(url, data=json.dumps(payload).encode(), headers=headers)
    with urllib.request.urlopen(request, timeout=15) as response:
        return response.status, json.load(response)


def row_count(table, key):
    if table not in {"payment_records", "payment_outbox"}:
        raise ValueError("unexpected table")
    # The key is generated locally as a UUID suffix, never supplied by a caller.
    sql = f"SELECT count(*) FROM {table} WHERE idempotency_key = '{key}'"
    result = subprocess.run(
        ["docker", "exec", DB_CONTAINER, "psql", "-U", "postgres", "-d", "FlashSaleDb", "-Atc", sql],
        check=True, text=True, capture_output=True,
    )
    return int(result.stdout.strip())


def wait_for_api():
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(f"{BASE_URL}/healthz", timeout=2) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, TimeoutError, ConnectionResetError,
                http.client.RemoteDisconnected):
            pass
        time.sleep(1)
    raise TimeoutError("payment API did not recover after restart")


def main():
    if not DEMO_PASSWORD:
        raise ValueError("DEMO_AUTH_PASSWORD is required")
    parsed = urlparse(BASE_URL)
    if parsed.hostname not in {"127.0.0.1", "localhost"}:
        raise ValueError("fault drill requires a local payment API")
    wait_for_api()
    _, login = request_json(
        f"{BASE_URL}/api/auth/login",
        {"email": "staff@flashsale.local", "password": DEMO_PASSWORD},
    )
    token = login["token"]
    identity = str(uuid.uuid4())
    key = f"crash-drill-{identity}"
    payload = {"paymentId": identity, "orderId": 1, "idempotencyKey": key,
               "amount": "10.01", "currency": "USD"}
    observed = {}

    class BufferThenKill(BaseHTTPRequestHandler):
        def do_POST(self):
            length = int(self.headers["Content-Length"])
            connection = http.client.HTTPConnection(parsed.hostname, parsed.port, timeout=15)
            connection.request("POST", self.path, self.rfile.read(length),
                               {"Content-Type": "application/json",
                                "Authorization": self.headers["Authorization"]})
            upstream = connection.getresponse()
            body = upstream.read()
            observed["upstream_status"] = upstream.status
            observed["upstream_body"] = json.loads(body)
            connection.close()
            if upstream.status != 200:
                raise AssertionError(f"upstream returned {upstream.status}")
            # The transaction has completed before the controller generated this
            # response. Kill the real API process before sending any client bytes.
            subprocess.run(["docker", "kill", API_CONTAINER], check=True,
                           capture_output=True, text=True)
            observed["process_killed"] = True
            self.connection.shutdown(socket.SHUT_RDWR)
            self.connection.close()

        def log_message(self, *_args):
            pass

    server = HTTPServer(("127.0.0.1", 0), BufferThenKill)
    thread = threading.Thread(target=server.handle_request, daemon=True)
    thread.start()
    client_lost_response = False
    try:
        request_json(f"http://127.0.0.1:{server.server_port}/api/payments", payload, token)
    except (http.client.RemoteDisconnected, ConnectionResetError, urllib.error.URLError):
        client_lost_response = True
    finally:
        thread.join(timeout=20)
        server.server_close()

    if not client_lost_response or observed.get("upstream_status") != 200 or not observed.get("process_killed"):
        raise AssertionError("did not observe committed upstream response and downstream loss")
    before = {table: row_count(table, key) for table in ("payment_records", "payment_outbox")}
    if before != {"payment_records": 1, "payment_outbox": 1}:
        raise AssertionError(f"unexpected committed state: {before}")

    subprocess.run(["docker", "start", API_CONTAINER], check=True, capture_output=True, text=True)
    wait_for_api()
    retry_status, retry = request_json(f"{BASE_URL}/api/payments", payload, token)
    after = {table: row_count(table, key) for table in ("payment_records", "payment_outbox")}
    if retry_status != 200 or retry["transactionId"] != observed["upstream_body"]["transactionId"] or after != before:
        raise AssertionError(f"retry did not preserve the committed result: {after}")
    print(json.dumps({"result": "passed", "idempotency_key": key,
                      "upstream_status": observed["upstream_status"],
                      "process_killed": observed["process_killed"],
                      "client_response_lost": client_lost_response,
                      "retry_status": retry_status, "before": before, "after": after}, indent=2))


if __name__ == "__main__":
    main()
