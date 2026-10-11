"""Correlation ID middleware for distributed tracing."""
import uuid

from opentelemetry import trace
from starlette.types import ASGIApp, Receive, Scope, Send


class CorrelationIdMiddleware:
    def __init__(self, app: ASGIApp) -> None:
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] not in ("http", "websocket"):
            await self.app(scope, receive, send)
            return

        # Get or generate correlation ID
        headers = dict(scope.get("headers", []))
        correlation_id = headers.get(b"x-correlation-id", b"").decode("utf-8")
        if not correlation_id:
            correlation_id = str(uuid.uuid4())

        request_id = headers.get(b"x-request-id", b"").decode("utf-8")
        if not request_id:
            request_id = str(uuid.uuid4())

        # Add to scope
        scope["headers"] = list(scope.get("headers", [])) + [
            (b"x-correlation-id", correlation_id.encode("utf-8")),
            (b"x-request-id", request_id.encode("utf-8")),
        ]

        # Add to tracing context
        current_span = trace.get_current_span()
        current_span.set_attribute("correlation_id", correlation_id)
        current_span.set_attribute("request_id", request_id)

        # Bọc send để gắn request-id vào MỌI response (docs/06:3).
        async def send_with_request_id(message: dict) -> None:
            if message["type"] == "http.response.start":
                raw = message.setdefault("headers", [])
                if not any(k.lower() == b"x-request-id" for k, _ in raw):
                    raw.append((b"x-request-id", request_id.encode("utf-8")))
                if not any(k.lower() == b"x-correlation-id" for k, _ in raw):
                    raw.append((b"x-correlation-id", correlation_id.encode("utf-8")))
            await send(message)

        await self.app(scope, receive, send_with_request_id)