CREATE DATABASE IF NOT EXISTS flashsale_analytics;

CREATE TABLE IF NOT EXISTS flashsale_analytics.order_events
(
    event_time    DateTime64(3) CODEC(Delta, LZ4),
    order_id      String CODEC(LZ4),
    event_type    LowCardinality(String) CODEC(LZ4),
    user_id       String CODEC(LZ4),
    product_id    String CODEC(LZ4),
    quantity      UInt16,
    amount_cents  UInt32,
    status        LowCardinality(String) CODEC(LZ4),
    trace_id      String CODEC(LZ4)
)
ENGINE = MergeTree
PARTITION BY toYYYYMM(event_time)
ORDER BY (event_type, event_time)
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS flashsale_analytics.daily_order_summary
(
    event_date    Date CODEC(LZ4),
    event_type    LowCardinality(String) CODEC(LZ4),
    order_count   UInt32,
    gross_cents   UInt64
)
ENGINE = AggregatingMergeTree
PARTITION BY toYYYYMM(event_date)
ORDER BY (event_date, event_type)
SETTINGS index_granularity = 8192;
