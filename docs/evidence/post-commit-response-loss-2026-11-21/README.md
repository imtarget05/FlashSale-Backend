# Post-commit response-loss crash drill — 2026-11-21

Run `DEMO_AUTH_PASSWORD=<disposable-demo-password> python3 scripts/post_commit_response_loss_drill.py` against the disposable Compose stack. The drill uses a one-request buffering proxy: the server completes a successful request and the proxy reads its 200 response, proving the transaction committed. The proxy then issues `docker kill` against the real payment container and closes the downstream connection without sending response bytes to the client. It checks PostgreSQL while the API is stopped, starts the container, retries the identical request, and checks PostgreSQL again.

The [result](result.json) records process kill, lost client response, retry 200, and exactly one `payment_records` row and one `payment_outbox` row before and after retry. The retry returned the same transaction ID as the response buffered by the proxy; no duplicate payment or outbox event was written.

Boundary: the server had already transmitted its response to the proxy before the kill. The drill proves a real process crash after DB commit and before the **client** received a response. It does not prove a JVM crash in the narrower interval before the server writes its first response byte, nor does it establish exactly-once event delivery.
