#!/usr/bin/env python3
"""Local-only Alertmanager webhook sink for observability drills."""

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


MAX_BODY_BYTES = 1_048_576
MAX_EVENTS = 100
events: list[dict] = []


class AlertReceiver(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if self.path == "/healthz":
            self._respond(200, {"status": "ok"})
        elif self.path == "/events":
            self._respond(200, {"events": events})
        else:
            self._respond(404, {"error": "not found"})

    def do_POST(self) -> None:
        if self.path != "/alerts":
            self._respond(404, {"error": "not found"})
            return

        try:
            content_length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self._respond(400, {"error": "invalid content length"})
            return
        if content_length <= 0 or content_length > MAX_BODY_BYTES:
            self._respond(413, {"error": "payload size is out of range"})
            return

        try:
            payload = json.loads(self.rfile.read(content_length))
        except json.JSONDecodeError:
            self._respond(400, {"error": "invalid JSON"})
            return
        if not isinstance(payload, dict) or not isinstance(payload.get("alerts"), list):
            self._respond(400, {"error": "expected an Alertmanager payload"})
            return

        events.append(payload)
        del events[:-MAX_EVENTS]
        alert_names = [alert.get("labels", {}).get("alertname") for alert in payload["alerts"]]
        print(
            json.dumps(
                {
                    "event": "alert_received",
                    "status": payload.get("status"),
                    "alert_names": alert_names,
                    "alert_count": len(payload["alerts"]),
                }
            ),
            flush=True,
        )
        self._respond(202, {"received": len(payload["alerts"])})

    def _respond(self, status: int, body: dict) -> None:
        encoded = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format: str, *args: object) -> None:
        print(json.dumps({"http": format % args}), flush=True)


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 9094), AlertReceiver).serve_forever()
