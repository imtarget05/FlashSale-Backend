CREATE TABLE payment_records (
    idempotency_key VARCHAR(128) PRIMARY KEY,
    payment_id UUID NOT NULL,
    order_id INTEGER NOT NULL,
    amount NUMERIC(12, 2) NOT NULL,
    currency VARCHAR(8) NOT NULL,
    success BOOLEAN NOT NULL,
    status VARCHAR(32) NOT NULL,
    transaction_id VARCHAR(64),
    failure_reason VARCHAR(256),
    is_transient_error BOOLEAN NOT NULL,
    processed_at TIMESTAMP WITH TIME ZONE NOT NULL,
    version BIGINT NOT NULL DEFAULT 0
);

CREATE TABLE payment_outbox (
    id UUID PRIMARY KEY,
    idempotency_key VARCHAR(128) NOT NULL UNIQUE,
    routing_key VARCHAR(64) NOT NULL,
    payload TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    next_attempt_at TIMESTAMP WITH TIME ZONE NOT NULL,
    published_at TIMESTAMP WITH TIME ZONE,
    attempts INTEGER NOT NULL DEFAULT 0,
    last_error TEXT
);

CREATE INDEX ix_payment_outbox_pending
    ON payment_outbox (next_attempt_at, created_at)
    WHERE published_at IS NULL;
