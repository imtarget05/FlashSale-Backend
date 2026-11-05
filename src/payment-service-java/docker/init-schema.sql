-- Init schema for payment-service-java
-- Creates payment_records table owned by the Java payment service

CREATE TABLE IF NOT EXISTS payment_records (
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
